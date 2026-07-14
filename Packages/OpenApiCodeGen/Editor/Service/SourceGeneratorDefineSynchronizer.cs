#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Editor.Generation;

using UnityEditor;
using UnityEditor.Build;

using UnityEngine;

namespace Rhycol.OpenApiCodeGen.Core
{
    internal interface IScriptingDefineStore
    {
        string[] Get(NamedBuildTarget buildTarget);

        void Set(NamedBuildTarget buildTarget, string[] defines);
    }

    internal sealed class PlayerSettingsScriptingDefineStore : IScriptingDefineStore
    {
        public string[] Get(NamedBuildTarget buildTarget)
        {
            PlayerSettings.GetScriptingDefineSymbols(buildTarget, out string[] defines);
            return defines;
        }

        public void Set(NamedBuildTarget buildTarget, string[] defines)
        {
            PlayerSettings.SetScriptingDefineSymbols(buildTarget, defines);
        }
    }

    internal sealed class SourceGeneratorDefineSynchronizer
    {
        internal const string DefineSymbol = "OPENAPI_CODEGEN_SOURCE_GENERATOR";

        readonly IAsyncRepository<ProjectSetting> _projectSettingRepository;
        readonly GenerationProviderRegistry _providerRegistry;
        readonly IScriptingDefineStore _defineStore;
        readonly SemaphoreSlim _synchronizationGate = new SemaphoreSlim(1, 1);

        public SourceGeneratorDefineSynchronizer(
            IAsyncRepository<ProjectSetting> projectSettingRepository,
            GenerationProviderRegistry providerRegistry,
            IScriptingDefineStore defineStore)
        {
            _projectSettingRepository = projectSettingRepository
                ?? throw new ArgumentNullException(nameof(projectSettingRepository));
            _providerRegistry = providerRegistry
                ?? throw new ArgumentNullException(nameof(providerRegistry));
            _defineStore = defineStore
                ?? throw new ArgumentNullException(nameof(defineStore));
        }

        public async Task<bool> SynchronizeAsync(NamedBuildTarget buildTarget)
        {
            await _synchronizationGate.WaitAsync();
            try
            {
                ProjectSetting? projectSetting = await _projectSettingRepository.ReadAsync();
                bool shouldEnable = ShouldEnable(projectSetting);
                string[] current = _defineStore.Get(buildTarget);
                string[] desired = Reconcile(current, shouldEnable);

                if (current.SequenceEqual(desired, StringComparer.Ordinal))
                {
                    return false;
                }

                _defineStore.Set(buildTarget, desired);
                return true;
            }
            finally
            {
                _synchronizationGate.Release();
            }
        }

        bool ShouldEnable(ProjectSetting? projectSetting)
        {
            if (projectSetting?.GenerateProvider != GenerateProvider.SourceGenerator)
            {
                return false;
            }

            GenerationProviderResolution resolution =
                _providerRegistry.Resolve(GenerateProvider.SourceGenerator);
            return resolution.Provider?.Descriptor.Availability.IsAvailable == true;
        }

        internal static string[] Reconcile(
            IReadOnlyList<string> current,
            bool shouldEnable)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            var result = new List<string>(current.Count + 1);
            bool symbolAdded = false;

            foreach (string symbol in current)
            {
                if (!string.Equals(symbol, DefineSymbol, StringComparison.Ordinal))
                {
                    result.Add(symbol);
                    continue;
                }

                if (shouldEnable && !symbolAdded)
                {
                    result.Add(symbol);
                    symbolAdded = true;
                }
            }

            if (shouldEnable && !symbolAdded)
            {
                result.Add(DefineSymbol);
            }

            return result.ToArray();
        }
    }

    [InitializeOnLoad]
    internal static class SourceGeneratorDefineSynchronization
    {
        static readonly SourceGeneratorDefineSynchronizer Synchronizer;
        static bool _scheduled;
        static bool _hasObservedBuildTarget;
        static NamedBuildTarget _observedBuildTarget;

        static SourceGeneratorDefineSynchronization()
        {
            Synchronizer = new SourceGeneratorDefineSynchronizer(
                ApplicationConfig.ProjectSettingRepository,
                GenerationProviderRegistry.Shared,
                new PlayerSettingsScriptingDefineStore());

            GenerationProviderRegistry.Shared.ProvidersChanged += Schedule;
            EditorApplication.update += ObserveActiveBuildTarget;
            ObserveActiveBuildTarget();
            Schedule();
        }

        internal static void Schedule()
        {
            if (_scheduled)
            {
                return;
            }

            _scheduled = true;
            EditorApplication.delayCall += SynchronizeActiveBuildTarget;
        }

        // Unity's -runTests request is not retained when a define change causes
        // a domain reload. The repository verification script invokes this
        // method in a separate batch before starting the test runner.
        public static async void SynchronizeActiveBuildTargetForBatchMode()
        {
            try
            {
                if (!TryGetActiveBuildTarget(out NamedBuildTarget buildTarget))
                {
                    EditorApplication.Exit(1);
                    return;
                }

                await Synchronizer.SynchronizeAsync(buildTarget);
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        static async void SynchronizeActiveBuildTarget()
        {
            _scheduled = false;

            try
            {
                if (!TryGetActiveBuildTarget(out NamedBuildTarget buildTarget))
                {
                    return;
                }

                await Synchronizer.SynchronizeAsync(buildTarget);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "OpenApiCodeGen failed to synchronize "
                    + $"{SourceGeneratorDefineSynchronizer.DefineSymbol}.\n{exception}");
            }
        }

        static bool TryGetActiveBuildTarget(
            out NamedBuildTarget buildTarget,
            bool logWarning = true)
        {
            BuildTarget activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(activeBuildTarget);
            if (targetGroup == BuildTargetGroup.Unknown)
            {
                buildTarget = default;
                if (logWarning)
                {
                    Debug.LogWarning(
                        "OpenApiCodeGen could not synchronize its scripting define because "
                        + "the active build target group is unknown.");
                }
                return false;
            }

            buildTarget = ResolveActiveNamedBuildTarget(
                activeBuildTarget,
                EditorUserBuildSettings.standaloneBuildSubtarget);
            return true;
        }

        internal static NamedBuildTarget ResolveActiveNamedBuildTarget(
            BuildTarget activeBuildTarget,
            StandaloneBuildSubtarget standaloneBuildSubtarget)
        {
            BuildTargetGroup targetGroup =
                BuildPipeline.GetBuildTargetGroup(activeBuildTarget);
            if (targetGroup == BuildTargetGroup.Standalone
                && standaloneBuildSubtarget == StandaloneBuildSubtarget.Server)
            {
                return NamedBuildTarget.Server;
            }

            return NamedBuildTarget.FromBuildTargetGroup(targetGroup);
        }

        static void ObserveActiveBuildTarget()
        {
            if (!TryGetActiveBuildTarget(
                    out NamedBuildTarget activeBuildTarget,
                    logWarning: false))
            {
                return;
            }

            if (_hasObservedBuildTarget && _observedBuildTarget == activeBuildTarget)
            {
                return;
            }

            _hasObservedBuildTarget = true;
            _observedBuildTarget = activeBuildTarget;
            Schedule();
        }
    }

    internal sealed class SourceGeneratorActiveBuildTargetChanged : IActiveBuildTargetChanged
    {
        public int callbackOrder => 0;

        public void OnActiveBuildTargetChanged(
            BuildTarget previousTarget,
            BuildTarget newTarget)
        {
            SourceGeneratorDefineSynchronization.Schedule();
        }
    }
}
