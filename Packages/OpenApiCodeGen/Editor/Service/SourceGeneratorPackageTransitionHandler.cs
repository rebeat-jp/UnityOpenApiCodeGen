#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using Rhycol.OpenApiCodeGen.Editor.Generation;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;

using UnityEngine;

namespace Rhycol.OpenApiCodeGen.Core
{
    internal sealed class SourceGeneratorPackageTransitionHandler
    {
        internal const string PackageName =
            "jp.rhycol.openapicodegen.source-generator";

        readonly GenerationProviderRegistry _providerRegistry;
        readonly IScriptingDefineStore _defineStore;

        internal SourceGeneratorPackageTransitionHandler(
            GenerationProviderRegistry providerRegistry,
            IScriptingDefineStore defineStore)
        {
            _providerRegistry = providerRegistry
                ?? throw new ArgumentNullException(nameof(providerRegistry));
            _defineStore = defineStore
                ?? throw new ArgumentNullException(nameof(defineStore));
        }

        internal bool DisableSourceGenerator(NamedBuildTarget buildTarget)
        {
            _providerRegistry.TryUnregister(
                GenerateProvider.SourceGenerator,
                out _);

            string[] current = _defineStore.Get(buildTarget);
            string[] desired = SourceGeneratorDefineSynchronizer.Reconcile(
                current,
                shouldEnable: false);
            if (current.SequenceEqual(desired, StringComparer.Ordinal))
            {
                return false;
            }

            _defineStore.Set(buildTarget, desired);
            return true;
        }

        internal static bool ContainsSourceGeneratorPackage(
            IEnumerable<string> added,
            IEnumerable<string> removed,
            IEnumerable<string> changedFrom,
            IEnumerable<string> changedTo)
        {
            return ContainsSourceGeneratorPackage(added)
                || ContainsSourceGeneratorPackage(removed)
                || ContainsSourceGeneratorPackage(changedFrom)
                || ContainsSourceGeneratorPackage(changedTo);
        }

        static bool ContainsSourceGeneratorPackage(IEnumerable<string> packageNames)
        {
            if (packageNames == null)
            {
                throw new ArgumentNullException(nameof(packageNames));
            }

            return packageNames.Any(packageName =>
                string.Equals(packageName, PackageName, StringComparison.Ordinal));
        }
    }

    // registeringPackages is raised before Unity applies the package graph
    // change. That timing lets the old domain remove the define before the
    // add-on assemblies disappear and the next compilation starts.
    [InitializeOnLoad]
    internal static class SourceGeneratorPackageTransitionSynchronization
    {
        static readonly SourceGeneratorPackageTransitionHandler Handler;

        static SourceGeneratorPackageTransitionSynchronization()
        {
            Handler = new SourceGeneratorPackageTransitionHandler(
                GenerationProviderRegistry.Shared,
                new PlayerSettingsScriptingDefineStore());
            Events.registeringPackages += OnRegisteringPackages;
        }

        static void OnRegisteringPackages(PackageRegistrationEventArgs args)
        {
            if (!SourceGeneratorPackageTransitionHandler.ContainsSourceGeneratorPackage(
                    args.added.Select(package => package.name),
                    args.removed.Select(package => package.name),
                    args.changedFrom.Select(package => package.name),
                    args.changedTo.Select(package => package.name)))
            {
                return;
            }

            // Unregister even when Unity cannot map the active build target.
            // This prevents the old provider instance from being used while
            // the package graph is changing.
            if (!TryGetActiveBuildTarget(out NamedBuildTarget buildTarget))
            {
                GenerationProviderRegistry.Shared.TryUnregister(
                    GenerateProvider.SourceGenerator,
                    out _);
                return;
            }

            Handler.DisableSourceGenerator(buildTarget);
            Debug.Log(
                "OpenApiCodeGen disabled the Source Generator provider and "
                + "scripting define before applying its package transition.");
        }

        static bool TryGetActiveBuildTarget(out NamedBuildTarget buildTarget)
        {
            BuildTarget activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(
                activeBuildTarget);
            if (targetGroup == BuildTargetGroup.Unknown)
            {
                buildTarget = default;
                Debug.LogWarning(
                    "OpenApiCodeGen could not remove its Source Generator "
                    + "scripting define because the active build target group is unknown.");
                return false;
            }

            buildTarget = SourceGeneratorDefineSynchronization.ResolveActiveNamedBuildTarget(
                activeBuildTarget,
                EditorUserBuildSettings.standaloneBuildSubtarget);
            return true;
        }
    }
}
