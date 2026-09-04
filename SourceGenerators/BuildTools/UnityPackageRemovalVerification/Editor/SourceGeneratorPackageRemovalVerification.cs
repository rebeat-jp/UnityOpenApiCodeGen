using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;

using UnityEngine;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.PackageRemovalVerification
{
    [InitializeOnLoad]
    public static class SourceGeneratorPackageRemovalVerification
    {
        const string PendingSessionKey =
            "OpenApiCodeGen.SourceGenerator.PackageRemovalVerification.Pending";
        const string SourceGeneratorPackageName =
            "jp.rhycol.openapicodegen.source-generator";
        const string DefineSymbol = "OPENAPI_CODEGEN_SOURCE_GENERATOR";
        const double TimeoutSeconds = 120d;

        static double _deadline;
        static bool _autoRefreshDisallowed;
        static bool _registeringPackagesVerified;

        static SourceGeneratorPackageRemovalVerification()
        {
            if (SessionState.GetBool(PendingSessionKey, false))
            {
                EditorApplication.delayCall += CompleteAfterDomainReload;
            }
        }

        public static void RemovePackageInLiveEditorDomain()
        {
            try
            {
                AssertDefineEnabledBeforeRemoval();
                _registeringPackagesVerified = false;
                SessionState.SetBool(PendingSessionKey, true);
                _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
                Events.registeringPackages += OnRegisteringPackages;
                Events.registeredPackages += OnRegisteredPackages;
                EditorApplication.update += CheckForTimeout;

                AssetDatabase.DisallowAutoRefresh();
                _autoRefreshDisallowed = true;

                Client.Remove(SourceGeneratorPackageName);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        static void OnRegisteringPackages(PackageRegistrationEventArgs args)
        {
            if (!args.removed.Any(package =>
                    string.Equals(
                        package.name,
                        SourceGeneratorPackageName,
                        StringComparison.Ordinal)))
            {
                return;
            }

            Events.registeringPackages -= OnRegisteringPackages;

            try
            {
                PlayerSettings.GetScriptingDefineSymbols(
                    GetActiveBuildTarget(),
                    out string[] defines);
                if (defines.Contains(DefineSymbol))
                {
                    throw new InvalidOperationException(
                        $"{DefineSymbol} was not removed before package registration.");
                }

                string guardPath = Path.Combine(
                    Application.dataPath,
                    "OpenApiCodeGen",
                    "SourceGeneratorRemovalGuard.cs");
                File.WriteAllText(
                    guardPath,
                    "#if OPENAPI_CODEGEN_SOURCE_GENERATOR\n"
                    + "#error Source Generator define was not removed before compilation.\n"
                    + "#endif\n");
                _registeringPackagesVerified = true;
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        static void OnRegisteredPackages(PackageRegistrationEventArgs args)
        {
            if (!args.removed.Any(package =>
                    string.Equals(
                        package.name,
                        SourceGeneratorPackageName,
                        StringComparison.Ordinal)))
            {
                return;
            }

            Events.registeredPackages -= OnRegisteredPackages;
            if (!_registeringPackagesVerified)
            {
                Fail(new InvalidOperationException(
                    "The Source Generator removal was registered without verifying "
                    + "the pre-registration define state."));
                return;
            }

            AllowAutoRefresh();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void CompleteAfterDomainReload()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += CompleteAfterDomainReload;
                return;
            }

            try
            {
                NamedBuildTarget buildTarget = GetActiveBuildTarget();
                PlayerSettings.GetScriptingDefineSymbols(
                    buildTarget,
                    out string[] defines);
                if (defines.Contains(DefineSymbol))
                {
                    throw new InvalidOperationException(
                        $"{DefineSymbol} remained enabled after package removal.");
                }

                if (UnityEditor.PackageManager.PackageInfo
                    .GetAllRegisteredPackages()
                    .Any(package =>
                        string.Equals(
                            package.name,
                            SourceGeneratorPackageName,
                            StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "The Source Generator package remains registered after removal.");
                }

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string manifestPath = Path.Combine(
                    projectRoot,
                    "Packages",
                    "manifest.json");
                string manifest = File.ReadAllText(manifestPath);
                if (Regex.IsMatch(
                        manifest,
                        $"\"{Regex.Escape(SourceGeneratorPackageName)}\"\\s*:"))
                {
                    throw new InvalidOperationException(
                        "The Source Generator dependency remains in manifest.json.");
                }

                SessionState.EraseBool(PendingSessionKey);
                Debug.Log(
                    "OpenApiCodeGen live Source Generator package removal verification passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        static void AssertDefineEnabledBeforeRemoval()
        {
            PlayerSettings.GetScriptingDefineSymbols(
                GetActiveBuildTarget(),
                out string[] defines);
            if (!defines.Contains(DefineSymbol))
            {
                throw new InvalidOperationException(
                    $"{DefineSymbol} must be enabled before verifying package removal.");
            }
        }

        static NamedBuildTarget GetActiveBuildTarget()
        {
            BuildTarget activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup targetGroup =
                BuildPipeline.GetBuildTargetGroup(activeBuildTarget);
            if (targetGroup == BuildTargetGroup.Unknown)
            {
                throw new InvalidOperationException(
                    "The active build target group is unknown.");
            }

            if (targetGroup == BuildTargetGroup.Standalone
                && EditorUserBuildSettings.standaloneBuildSubtarget
                    == StandaloneBuildSubtarget.Server)
            {
                return NamedBuildTarget.Server;
            }

            return NamedBuildTarget.FromBuildTargetGroup(targetGroup);
        }

        static void CheckForTimeout()
        {
            if (EditorApplication.timeSinceStartup < _deadline)
            {
                return;
            }

            Fail(new TimeoutException(
                "Unity did not register the Source Generator package removal in time."));
        }

        static void AllowAutoRefresh()
        {
            if (!_autoRefreshDisallowed)
            {
                return;
            }

            _autoRefreshDisallowed = false;
            AssetDatabase.AllowAutoRefresh();
        }

        static void Fail(Exception exception)
        {
            Events.registeringPackages -= OnRegisteringPackages;
            Events.registeredPackages -= OnRegisteredPackages;
            EditorApplication.update -= CheckForTimeout;
            AllowAutoRefresh();
            SessionState.EraseBool(PendingSessionKey);
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}
