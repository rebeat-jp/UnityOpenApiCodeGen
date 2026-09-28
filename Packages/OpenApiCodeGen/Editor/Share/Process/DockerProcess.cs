#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Rhycol.OpenApiCodeGen.Core
{
    public class DockerProcess
    {
        public string Path { get; set; }
        public bool UseShellExecute { get; set; }
        public bool RedirectStandardOutput { get; set; }
        public bool RedirectStandardError { get; set; }

        public DockerProcess(
            string path = "docker",
            bool useShellExecute = false,
            bool redirectStandardOutput = true,
            bool redirectStandardError = true)
        {
            Path = path;
            UseShellExecute = useShellExecute;
            RedirectStandardOutput = redirectStandardOutput;
            RedirectStandardError = redirectStandardError;
        }

        public ProcessResponse Send(string argumentsSeparateWithSpace)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = Path,
                Arguments = argumentsSeparateWithSpace,
                UseShellExecute = UseShellExecute,
                RedirectStandardOutput = RedirectStandardOutput,
                RedirectStandardError = RedirectStandardError
            };
            using var process = new Process();

            var outputText = "";
            var errorText = "";

            process.StartInfo = processStartInfo;
            try
            {
                process.Start();

                var outputTask = RedirectStandardOutput
                    ? process.StandardOutput.ReadToEndAsync()
                    : Task.FromResult("");
                var errorTask = RedirectStandardError
                    ? process.StandardError.ReadToEndAsync()
                    : Task.FromResult("");

                process.WaitForExit();
                outputText = outputTask.GetAwaiter().GetResult();
                errorText = errorTask.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                throw new ExternalServiceException("Dockerプロセスの起動または実行に失敗しました。", e);
            }

            var message = string.IsNullOrEmpty(errorText)
                ? outputText
                : string.Concat(outputText, string.IsNullOrEmpty(outputText) ? "" : Environment.NewLine, errorText);

            return new ProcessResponse(
                process.ExitCode == 0 ? ExitStatus.Success : ExitStatus.Error,
                message
            );

        }

        public Task<ProcessResponse> SendAsync(
            string argumentsSeparateWithSpace,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(argumentsSeparateWithSpace, cancellationToken, null);
        }

        internal Task<ProcessResponse> SendContainerAsync(
            string argumentsSeparateWithSpace,
            string containerName,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(argumentsSeparateWithSpace, cancellationToken, containerName);
        }

        async Task<ProcessResponse> ExecuteAsync(
            string argumentsSeparateWithSpace,
            CancellationToken cancellationToken,
            string? containerName)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (containerName != null && !IsValidContainerName(containerName))
            {
                throw new ArgumentException("Invalid Docker container name.", nameof(containerName));
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path,
                    Arguments = argumentsSeparateWithSpace,
                    UseShellExecute = UseShellExecute,
                    RedirectStandardOutput = RedirectStandardOutput,
                    RedirectStandardError = RedirectStandardError
                }
            };
            Task? completionTask = null;
            bool started = false;
            try
            {
                started = process.Start();
                if (!started)
                {
                    throw new InvalidOperationException("Docker process did not start.");
                }

                Task<string> outputTask = RedirectStandardOutput
                    ? process.StandardOutput.ReadToEndAsync()
                    : Task.FromResult("");
                Task<string> errorTask = RedirectStandardError
                    ? process.StandardError.ReadToEndAsync()
                    : Task.FromResult("");
                Task exitTask = Task.Run(() => process.WaitForExit());
                var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => cancellation.TrySetResult(true)))
                {
                    completionTask = Task.WhenAll(exitTask, outputTask, errorTask);
                    _ = completionTask.ContinueWith(
                        task => { _ = task.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted);
                    await Task.WhenAny(completionTask, cancellation.Task).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    await completionTask.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                string outputText = await outputTask.ConfigureAwait(false);
                string errorText = await errorTask.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var message = string.IsNullOrEmpty(errorText)
                    ? outputText
                    : string.Concat(outputText, string.IsNullOrEmpty(outputText) ? "" : Environment.NewLine, errorText);
                return new ProcessResponse(
                    process.ExitCode == 0 ? ExitStatus.Success : ExitStatus.Error,
                    message);
            }
            catch (Exception e)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    if (started)
                    {
                        TryKill(process);
                        if (completionTask != null &&
                            await Task.WhenAny(completionTask, Task.Delay(2000)).ConfigureAwait(false) != completionTask)
                        {
                            try
                            {
                                if (RedirectStandardOutput) process.StandardOutput.Dispose();
                                if (RedirectStandardError) process.StandardError.Dispose();
                            }
                            catch (ObjectDisposedException) { }
                            catch (System.IO.IOException) { }
                            await Task.WhenAny(completionTask, Task.Delay(500)).ConfigureAwait(false);
                        }
                    }

                    string? cleanupFailure = containerName == null || !started
                        ? null
                        : await Task.Run(() => RemoveContainer(containerName)).ConfigureAwait(false);
                    var canceled = new OperationCanceledException("Docker execution was canceled.", e, cancellationToken);
                    if (cleanupFailure != null)
                    {
                        canceled.Data["DockerContainerCleanup"] = cleanupFailure;
                    }
                    throw canceled;
                }

                throw new ExternalServiceException("Dockerプロセスの起動または実行に失敗しました。", e);
            }
        }

        static bool IsValidContainerName(string name)
        {
            if (name.Length == 0 || !char.IsLetterOrDigit(name[0])) return false;
            foreach (char character in name)
            {
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.') return false;
            }
            return true;
        }

        static void TryKill(Process process)
        {
            try { process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        string? RemoveContainer(string containerName)
        {
            string? failure = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                failure = null;
                using var cleanup = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path,
                        Arguments = "rm --force " + containerName,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                try
                {
                    cleanup.Start();
                    Task<string> outputTask = cleanup.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = cleanup.StandardError.ReadToEndAsync();
                    Task outputDrain = Task.WhenAll(outputTask, errorTask);
                    _ = outputDrain.ContinueWith(
                        task => { _ = task.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted);
                    if (!cleanup.WaitForExit(2000))
                    {
                        TryKill(cleanup);
                        if (!cleanup.WaitForExit(1000))
                        {
                            TryKill(cleanup);
                            cleanup.WaitForExit(1000);
                        }
                        failure = "Docker container removal timed out.";
                    }
                    if (!outputDrain.Wait(500))
                    {
                        cleanup.StandardOutput.Dispose();
                        cleanup.StandardError.Dispose();
                        failure = "Docker container removal output did not close.";
                    }
                    else if (failure == null)
                    {
                        if (cleanup.ExitCode == 0)
                        {
                            return null;
                        }
                        failure = errorTask.Result;
                        if (failure.IndexOf("No such container", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            failure = null;
                        }
                    }
                }
                catch (Exception e)
                {
                    failure = e.Message;
                }

                if (attempt < 2) Thread.Sleep(150);
            }
            return failure;
        }
    }
}
