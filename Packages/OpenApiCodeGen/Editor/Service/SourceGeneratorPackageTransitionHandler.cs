#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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

            return RemoveDefine(buildTarget);
        }

        bool RemoveDefine(NamedBuildTarget buildTarget)
        {
            return RemoveDefine(buildTarget, _defineStore.Get(buildTarget));
        }

        bool RemoveDefine(NamedBuildTarget buildTarget, string[] current)
        {
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

        internal bool DisableSourceGeneratorForAllTargets()
        {
            return DisableSourceGeneratorForTargets(GetKnownBuildTargets());
        }

        internal bool DisableSourceGeneratorForTargets(
            IEnumerable<NamedBuildTarget> buildTargets)
        {
            if (buildTargets == null)
            {
                throw new ArgumentNullException(nameof(buildTargets));
            }

            _providerRegistry.TryUnregister(GenerateProvider.SourceGenerator, out _);
            bool changed = false;
            foreach (NamedBuildTarget buildTarget in buildTargets.Distinct())
            {
                string[] current;
                try
                {
                    current = _defineStore.Get(buildTarget);
                }
                catch (ArgumentException exception)
                {
                    Debug.LogWarning(
                        $"OpenApiCodeGen skipped unsupported build target "
                        + $"{buildTarget.TargetName}: {exception.Message}");
                    continue;
                }
                catch (NotSupportedException exception)
                {
                    Debug.LogWarning(
                        $"OpenApiCodeGen skipped unsupported build target "
                        + $"{buildTarget.TargetName}: {exception.Message}");
                    continue;
                }

                changed |= RemoveDefine(buildTarget, current);
            }

            return changed;
        }

        internal static IReadOnlyList<NamedBuildTarget> GetKnownBuildTargets()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            Type targetType = typeof(NamedBuildTarget);
            var targets = new HashSet<NamedBuildTarget>();

            foreach (FieldInfo field in targetType.GetFields(flags))
            {
                if (field.FieldType == targetType)
                {
                    targets.Add((NamedBuildTarget)field.GetValue(null));
                }
            }

            foreach (PropertyInfo property in targetType.GetProperties(flags))
            {
                if (property.PropertyType == targetType
                    && property.GetIndexParameters().Length == 0)
                {
                    targets.Add((NamedBuildTarget)property.GetValue(null, null));
                }
            }

            // Some platform targets have no public NamedBuildTarget member,
            // but Unity still exposes them through BuildTargetGroup.
            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown)
                {
                    continue;
                }

                try
                {
                    targets.Add(NamedBuildTarget.FromBuildTargetGroup(group));
                }
                catch (ArgumentException)
                {
                    // Removed or unsupported groups can remain in the enum.
                }
                catch (NotSupportedException)
                {
                    // Platform availability differs between Unity versions.
                }
            }

            targets.Remove(NamedBuildTarget.Unknown);
            return targets.Where(target => !string.IsNullOrEmpty(target.TargetName))
                .OrderBy(target => target.TargetName, StringComparer.Ordinal)
                .ToArray();
        }

        internal static bool IsSourceGeneratorPackageRemoval(
            IEnumerable<string> added,
            IEnumerable<string> removed,
            IEnumerable<string> changedTo)
        {
            return ContainsSourceGeneratorPackage(removed)
                && !ContainsSourceGeneratorPackage(added)
                && !ContainsSourceGeneratorPackage(changedTo);
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

            if (SourceGeneratorPackageTransitionHandler.IsSourceGeneratorPackageRemoval(
                    args.added.Select(package => package.name),
                    args.removed.Select(package => package.name),
                    args.changedTo.Select(package => package.name)))
            {
                Handler.DisableSourceGeneratorForAllTargets();
                Debug.Log(
                    "OpenApiCodeGen disabled the Source Generator provider and "
                    + "scripting define before applying its package transition.");
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
