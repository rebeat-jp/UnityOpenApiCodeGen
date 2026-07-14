#nullable enable

using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen;
using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;

using UnityEditor;
using UnityEditor.Build;

using UnityEngine.TestTools;

internal sealed class SourceGeneratorDefineSynchronizerTests
{
    static readonly NamedBuildTarget Target = NamedBuildTarget.Standalone;

    [UnityTest]
    public IEnumerator ActiveBuildTargetDefineMatchesSavedProviderAvailability()
    {
        // Let InitializeOnLoad registrations and the delayed synchronization complete.
        yield return null;
        yield return null;

        Task<ProjectSetting?> readTask =
            ApplicationConfig.ProjectSettingRepository.ReadAsync();
        while (!readTask.IsCompleted)
        {
            yield return null;
        }

        ProjectSetting? setting = readTask.GetAwaiter().GetResult();
        GenerationProviderResolution resolution =
            GenerationProviderRegistry.Shared.Resolve(GenerateProvider.SourceGenerator);
        bool shouldBeEnabled =
            setting?.GenerateProvider == GenerateProvider.SourceGenerator
            && resolution.Provider?.Descriptor.Availability.IsAvailable == true;

        BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(
            EditorUserBuildSettings.activeBuildTarget);
        NamedBuildTarget buildTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);
        PlayerSettings.GetScriptingDefineSymbols(buildTarget, out string[] defines);

        Assert.That(
            defines.Contains(SourceGeneratorDefineSynchronizer.DefineSymbol),
            Is.EqualTo(shouldBeEnabled));
    }

    [Test]
    public void AvailableSourceGeneratorSelectionAddsDefine()
    {
        var registry = CreateRegistry(GenerationProviderAvailability.Available());
        var store = new FakeDefineStore("EXISTING");
        var synchronizer = CreateSynchronizer(GenerateProvider.SourceGenerator, registry, store);

        bool changed = synchronizer.SynchronizeAsync(Target).GetAwaiter().GetResult();

        Assert.That(changed, Is.True);
        Assert.That(store.SetCount, Is.EqualTo(1));
        Assert.That(
            store.Defines,
            Is.EqualTo(new[] { "EXISTING", SourceGeneratorDefineSynchronizer.DefineSymbol }));
    }

    [Test]
    public void EqualDefinesDoNotWritePlayerSettings()
    {
        var registry = CreateRegistry(GenerationProviderAvailability.Available());
        var store = new FakeDefineStore(
            "EXISTING",
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        var synchronizer = CreateSynchronizer(GenerateProvider.SourceGenerator, registry, store);

        bool changed = synchronizer.SynchronizeAsync(Target).GetAwaiter().GetResult();

        Assert.That(changed, Is.False);
        Assert.That(store.SetCount, Is.Zero);
    }

    [TestCase(GenerateProvider.OpenApi)]
    [TestCase((GenerateProvider)99)]
    public void NonSourceGeneratorSelectionRemovesOnlySourceGeneratorDefine(
        GenerateProvider selectedProvider)
    {
        var registry = CreateRegistry(GenerationProviderAvailability.Available());
        var store = new FakeDefineStore(
            SourceGeneratorDefineSynchronizer.DefineSymbol,
            "EXISTING",
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        var synchronizer = CreateSynchronizer(selectedProvider, registry, store);

        synchronizer.SynchronizeAsync(Target).GetAwaiter().GetResult();

        Assert.That(store.Defines, Is.EqualTo(new[] { "EXISTING" }));
    }

    [Test]
    public void UnregisteredSourceGeneratorRemovesDefine()
    {
        var store = new FakeDefineStore(SourceGeneratorDefineSynchronizer.DefineSymbol);
        var synchronizer = CreateSynchronizer(
            GenerateProvider.SourceGenerator,
            new GenerationProviderRegistry(),
            store);

        synchronizer.SynchronizeAsync(Target).GetAwaiter().GetResult();

        Assert.That(store.Defines, Is.Empty);
    }

    [Test]
    public void UnavailableSourceGeneratorRemovesDefine()
    {
        var registry = CreateRegistry(
            GenerationProviderAvailability.Unavailable("Analyzer is missing."));
        var store = new FakeDefineStore(SourceGeneratorDefineSynchronizer.DefineSymbol);
        var synchronizer = CreateSynchronizer(GenerateProvider.SourceGenerator, registry, store);

        synchronizer.SynchronizeAsync(Target).GetAwaiter().GetResult();

        Assert.That(store.Defines, Is.Empty);
    }

    [Test]
    public void ConcurrentSynchronizationsApplyTheLatestSavedProviderLast()
    {
        var registry = CreateRegistry(GenerationProviderAvailability.Available());
        var store = new FakeDefineStore();
        var repository = new DelayedProjectSettingRepository(
            new ProjectSetting(generateProvider: GenerateProvider.OpenApi));
        var synchronizer = new SourceGeneratorDefineSynchronizer(
            repository,
            registry,
            store);

        Task<bool> firstSynchronization =
            Task.Run(() => synchronizer.SynchronizeAsync(Target));
        repository.FirstReadStarted.Task.GetAwaiter().GetResult();

        repository.Value =
            new ProjectSetting(generateProvider: GenerateProvider.SourceGenerator);
        Task<bool> latestSynchronization =
            Task.Run(() => synchronizer.SynchronizeAsync(Target));
        repository.ReleaseFirstRead();

        Task.WhenAll(firstSynchronization, latestSynchronization)
            .GetAwaiter()
            .GetResult();

        Assert.That(repository.ReadCount, Is.EqualTo(2));
        Assert.That(
            store.Defines,
            Is.EqualTo(new[] { SourceGeneratorDefineSynchronizer.DefineSymbol }));
    }

    [Test]
    public void ActiveStandaloneServerResolvesToServerNamedBuildTarget()
    {
        NamedBuildTarget buildTarget =
            SourceGeneratorDefineSynchronization.ResolveActiveNamedBuildTarget(
                BuildTarget.StandaloneOSX,
                StandaloneBuildSubtarget.Server);

        Assert.That(buildTarget, Is.EqualTo(NamedBuildTarget.Server));
    }

    static SourceGeneratorDefineSynchronizer CreateSynchronizer(
        GenerateProvider provider,
        GenerationProviderRegistry registry,
        FakeDefineStore store)
    {
        return new SourceGeneratorDefineSynchronizer(
            new FakeProjectSettingRepository(new ProjectSetting(generateProvider: provider)),
            registry,
            store);
    }

    static GenerationProviderRegistry CreateRegistry(
        GenerationProviderAvailability availability)
    {
        var registry = new GenerationProviderRegistry();
        Assert.That(
            registry.TryRegister(
                new StubGenerationProvider(GenerateProvider.SourceGenerator, availability),
                out string failureReason),
            Is.True,
            failureReason);
        return registry;
    }

    sealed class FakeProjectSettingRepository : IAsyncRepository<ProjectSetting>
    {
        readonly ProjectSetting _setting;

        public FakeProjectSettingRepository(ProjectSetting setting)
        {
            _setting = setting;
        }

        public Task<ProjectSetting?> ReadAsync()
        {
            return Task.FromResult<ProjectSetting?>(_setting);
        }

        public Task SaveAsync(ProjectSetting value)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync()
        {
            return Task.CompletedTask;
        }
    }

    sealed class FakeDefineStore : IScriptingDefineStore
    {
        public string[] Defines { get; private set; }
        public int SetCount { get; private set; }

        public FakeDefineStore(params string[] defines)
        {
            Defines = defines;
        }

        public string[] Get(NamedBuildTarget buildTarget)
        {
            return (string[])Defines.Clone();
        }

        public void Set(NamedBuildTarget buildTarget, string[] defines)
        {
            SetCount++;
            Defines = (string[])defines.Clone();
        }
    }

    sealed class DelayedProjectSettingRepository : IAsyncRepository<ProjectSetting>
    {
        readonly TaskCompletionSource<bool> _firstReadRelease =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> FirstReadStarted { get; } =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProjectSetting Value { get; set; }
        public int ReadCount { get; private set; }

        public DelayedProjectSettingRepository(ProjectSetting value)
        {
            Value = value;
        }

        public async Task<ProjectSetting?> ReadAsync()
        {
            ReadCount++;
            if (ReadCount == 1)
            {
                ProjectSetting firstValue = Value;
                FirstReadStarted.TrySetResult(true);
                await _firstReadRelease.Task;
                return firstValue;
            }

            return Value;
        }

        public Task SaveAsync(ProjectSetting value)
        {
            Value = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync()
        {
            return Task.CompletedTask;
        }

        public void ReleaseFirstRead()
        {
            _firstReadRelease.TrySetResult(true);
        }
    }

    sealed class StubGenerationProvider : IGenerationProvider
    {
        public GenerationProviderDescriptor Descriptor { get; }

        public StubGenerationProvider(
            GenerateProvider provider,
            GenerationProviderAvailability availability)
        {
            Descriptor = new GenerationProviderDescriptor(
                provider,
                provider.ToString(),
                availability);
        }

        public GenerationResult Generate(GenerationRequest request)
        {
            return GenerationResult.Success();
        }

        public Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GenerationResult.Success());
        }
    }
}
