#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.Lib;
using UnityEngine;

internal sealed class DockerProcessCancellationTests
{
    string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        if (Application.platform == RuntimePlatform.WindowsEditor)
        {
            Assert.Ignore("Fake Docker CLI requires a POSIX shell.");
        }
        _directory = Path.Combine(Path.GetTempPath(), "openapi-docker-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (_directory != null)
        {
            string children = Path.Combine(_directory, "inherited-pipe-pids");
            if (File.Exists(children))
            {
                foreach (string pid in File.ReadAllLines(children))
                {
                    try
                    {
                        using var process = Process.GetProcessById(int.Parse(pid));
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
            }
        }
        if (_directory != null && Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Test]
    public void AlreadyCanceledTokenStartsNoProcess()
    {
        string dockerPath = FakeDocker();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AssertCanceled(new DockerProcess(dockerPath).SendContainerAsync(
            "run --rm --name test-container", "test-container", cancellation.Token));
        Assert.That(File.Exists(Path.Combine(_directory, "run-pid")), Is.False);
        Assert.That(File.Exists(Path.Combine(_directory, "removed")), Is.False);
    }

    [Test]
    public void InFlightCancellationKillsCliAndRemovesNamedContainer()
    {
        string dockerPath = FakeDocker();
        using var cancellation = new CancellationTokenSource();
        Task<ProcessResponse> execution = new DockerProcess(dockerPath).SendContainerAsync(
            "run --rm --name test-container", "test-container", cancellation.Token);
        WaitForFile("run-pid");
        int pid = int.Parse(File.ReadAllText(Path.Combine(_directory, "run-pid")));

        cancellation.Cancel();
        AssertCanceled(execution);
        Assert.That(File.ReadAllText(Path.Combine(_directory, "removed")), Does.Contain("rm --force test-container"));
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Test]
    public void MissingContainerDuringFirstCleanupAttemptIsRetried()
    {
        string dockerPath = FakeDocker(firstRemovalMissing: true);
        using var cancellation = new CancellationTokenSource();
        Task<ProcessResponse> execution = new DockerProcess(dockerPath).SendContainerAsync(
            "run --rm --name race-container", "race-container", cancellation.Token);
        WaitForFile("run-pid");

        cancellation.Cancel();
        AssertCanceled(execution);
        string[] removals = File.ReadAllLines(Path.Combine(_directory, "removed"));
        Assert.That(removals, Has.Length.EqualTo(2));
        Assert.That(removals[0], Is.EqualTo("rm --force race-container"));
        Assert.That(removals[1], Is.EqualTo(removals[0]));
    }

    [Test]
    public void AlreadyRemovedContainerKeepsCancellationResult()
    {
        string dockerPath = FakeDocker(alwaysRemovalMissing: true);
        using var cancellation = new CancellationTokenSource();
        Task<ProcessResponse> execution = new DockerProcess(dockerPath).SendContainerAsync(
            "run --rm --name removed-container", "removed-container", cancellation.Token);
        WaitForFile("run-pid");

        cancellation.Cancel();
        OperationCanceledException canceled = AssertCanceled(execution);
        Assert.That(canceled.Data.Contains("DockerContainerCleanup"), Is.False);
        Assert.That(File.ReadAllLines(Path.Combine(_directory, "removed")), Has.Length.EqualTo(3));
    }

    [Test]
    public void TimedOutCleanupProcessesAreKilledAndReported()
    {
        string dockerPath = FakeDocker(hangRemoval: true);
        using var cancellation = new CancellationTokenSource();
        Task<ProcessResponse> execution = new DockerProcess(dockerPath).SendContainerAsync(
            "run --rm --name slow-cleanup-container", "slow-cleanup-container", cancellation.Token);
        WaitForFile("run-pid");

        cancellation.Cancel();
        OperationCanceledException canceled = AssertCanceled(execution);
        Assert.That(canceled.Data["DockerContainerCleanup"], Does.Contain("timed out"));
        string[] pids = File.ReadAllLines(Path.Combine(_directory, "removal-pids"));
        Assert.That(pids, Has.Length.EqualTo(3));
        foreach (string pid in pids)
        {
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(int.Parse(pid)));
        }
    }

    [Test]
    public void InheritedCleanupPipesCannotBlockCancellation()
    {
        string dockerPath = FakeDocker(inheritRemovalPipes: true);
        using var cancellation = new CancellationTokenSource();
        Task<ProcessResponse> execution = new DockerProcess(dockerPath).SendContainerAsync(
            "run --rm --name inherited-pipes-container", "inherited-pipes-container", cancellation.Token);
        WaitForFile("run-pid");

        cancellation.Cancel();
        Task first = Task.WhenAny(execution, Task.Delay(5000)).GetAwaiter().GetResult();
        Assert.That(first, Is.SameAs(execution), "Cancellation must not wait for inherited cleanup pipes.");
        OperationCanceledException canceled = AssertCanceled(execution);
        Assert.That(canceled.Data["DockerContainerCleanup"], Does.Contain("output did not close"));
        Assert.That(File.ReadAllLines(Path.Combine(_directory, "removed")), Has.Length.EqualTo(3));
    }

    [Test]
    public void NormalExitPreservesStatusAndBothOutputStreams()
    {
        var docker = new DockerProcess(FakeDocker());

        ProcessResponse success = docker.SendAsync("success").GetAwaiter().GetResult();
        ProcessResponse failure = docker.SendAsync("failure").GetAwaiter().GetResult();

        Assert.That(success.Status, Is.EqualTo(ExitStatus.Success));
        Assert.That(success.Message, Is.EqualTo("standard" + Environment.NewLine + "diagnostic"));
        Assert.That(failure.Status, Is.EqualTo(ExitStatus.Error));
        Assert.That(failure.Message, Is.EqualTo("failed"));
    }

    [Test]
    public void GeneratorUsesUniqueKnownContainerNames()
    {
        string dockerPath = FakeDocker(slowRun: false);
        string document = Path.Combine(_directory, "openapi.json");
        File.WriteAllText(document, "{}");
        var generator = new OpenApiCodeGenerator();
        var project = new ProjectSetting(
            GenerateProvider.OpenApi,
            document,
            Path.Combine(_directory, "output"));
        var user = new UserSetting(dockerPath: dockerPath);

        Task.Run(() => generator.GenerateAsync(project, new GenerationCSharpSetting(), user))
            .GetAwaiter().GetResult();
        Task.Run(() => generator.GenerateAsync(project, new GenerationCSharpSetting(), user))
            .GetAwaiter().GetResult();

        string[] calls = File.ReadAllLines(Path.Combine(_directory, "runs"));
        Assert.That(calls, Has.Length.EqualTo(2));
        var names = new string[2];
        for (int index = 0; index < calls.Length; index++)
        {
            Match match = Regex.Match(calls[index], @"^run --rm --name (openapi-codegen-[0-9a-f]{32}) ");
            Assert.That(match.Success, Is.True, calls[index]);
            names[index] = match.Groups[1].Value;
        }
        Assert.That(names[0], Is.Not.EqualTo(names[1]));
    }

    void WaitForFile(string fileName)
    {
        string file = Path.Combine(_directory, fileName);
        for (int attempt = 0; attempt < 150 && !File.Exists(file); attempt++)
        {
            Thread.Sleep(20);
        }
        Assert.That(File.Exists(file), Is.True, "Fake Docker CLI did not start.");
    }

    static OperationCanceledException AssertCanceled(Task task)
    {
        OperationCanceledException? canceled = null;
        try { task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException e) { canceled = e; }
        Assert.That(canceled, Is.Not.Null, "Docker execution must report cancellation.");
        return canceled!;
    }

    string FakeDocker(
        bool firstRemovalMissing = false,
        bool slowRun = true,
        bool alwaysRemovalMissing = false,
        bool hangRemoval = false,
        bool inheritRemovalPipes = false)
    {
        string script = Path.Combine(_directory, "docker");
        string escapedDirectory = _directory.Replace("'", "'\\''");
        File.WriteAllText(script, $@"#!/bin/sh
case ""$1"" in
  run)
    printf '%s\n' ""$$"" > '{escapedDirectory}/run-pid'
    printf '%s\n' ""$*"" >> '{escapedDirectory}/runs'
    if [ '{(slowRun ? "yes" : "no")}' = yes ]; then exec /bin/sleep 30; fi
    printf generated
    exit 0
    ;;
  rm)
    printf '%s\n' ""$*"" >> '{escapedDirectory}/removed'
    if [ '{(hangRemoval ? "yes" : "no")}' = yes ]; then
      printf '%s\n' ""$$"" >> '{escapedDirectory}/removal-pids'
      exec /bin/sleep 30
    fi
    if [ '{(inheritRemovalPipes ? "yes" : "no")}' = yes ]; then
      /bin/sleep 30 &
      printf '%s\n' ""$!"" >> '{escapedDirectory}/inherited-pipe-pids'
      printf 'cleanup rejected\n' >&2
      exit 1
    fi
    if [ '{(alwaysRemovalMissing ? "yes" : "no")}' = yes ] ||
       {{ [ '{(firstRemovalMissing ? "yes" : "no")}' = yes ] && [ ""$(wc -l < '{escapedDirectory}/removed')"" -eq 1 ]; }}; then
      printf 'No such container\n' >&2
      exit 1
    fi
    exit 0
    ;;
  success)
    printf standard
    printf diagnostic >&2
    exit 0
    ;;
  failure)
    printf failed >&2
    exit 7
    ;;
esac
exit 2
");
        using var chmod = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/chmod",
            Arguments = "+x \"" + script + "\"",
            UseShellExecute = false
        });
        Assert.That(chmod, Is.Not.Null);
        chmod!.WaitForExit();
        Assert.That(chmod.ExitCode, Is.Zero);
        return script;
    }
}
