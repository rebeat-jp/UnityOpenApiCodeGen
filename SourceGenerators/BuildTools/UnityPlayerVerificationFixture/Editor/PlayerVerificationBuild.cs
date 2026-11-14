using System;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

using UnityEngine;

namespace PlayerVerification.Editor
{
    /// <summary>
    /// Editor entry points of the Player gate. <see cref="SelectCombos"/> only sets scripting defines (a script
    /// compilation is needed before they take effect, so it runs in its own Unity launch); <see cref="Build"/>
    /// applies the backend and stripping settings and builds one Player.
    /// </summary>
    public static class PlayerVerificationBuild
    {
        private const string ScenePath = "Assets/PlayerVerification/Scenes/Empty.unity";

        // -executeMethod: -pvCombos HttpNewtonsoft,HttpStj,UwrNewtonsoft,UwrStj
        public static void SelectCombos()
        {
            // "none" clears the selection (no client is compiled), so that the project compiles before the
            // definition files are active on a freshly switched build target.
            string argument = RequireArgument("-pvCombos");
            string[] combos = argument == "none" ? new string[0] : argument.Split(',');
            string[] defines = combos.Select(DefineFor).ToArray();
            foreach (NamedBuildTarget target in new[] { NamedBuildTarget.Standalone, NamedBuildTarget.WebGL })
            {
                string existing = PlayerSettings.GetScriptingDefineSymbols(target);
                string[] kept = existing
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(symbol => !symbol.StartsWith("PV_", StringComparison.Ordinal))
                    .ToArray();
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", kept.Concat(defines)));
            }

            Debug.Log("PlayerVerification selected combos: " + string.Join(",", combos));
        }

        public static void EnsureScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("PlayerVerification scene saved: " + ScenePath);
        }

        // -executeMethod: -pvTarget standalone|webgl -pvBackend il2cpp|mono -pvStripping minimal|high
        //                 -pvOutput <directory>
        public static void Build()
        {
            string target = RequireArgument("-pvTarget");
            string backend = RequireArgument("-pvBackend");
            string stripping = RequireArgument("-pvStripping");
            string output = RequireArgument("-pvOutput");

            NamedBuildTarget namedTarget;
            BuildTarget buildTarget;
            string locationPath;
            switch (target)
            {
                case "standalone":
                    namedTarget = NamedBuildTarget.Standalone;
                    buildTarget = EditorUserBuildSettings.activeBuildTarget;
                    if (buildTarget != BuildTarget.StandaloneWindows64
                        && buildTarget != BuildTarget.StandaloneLinux64
                        && buildTarget != BuildTarget.StandaloneOSX)
                    {
                        throw new InvalidOperationException("Start Unity with -buildTarget for a desktop Standalone target; it is " + buildTarget);
                    }

                    locationPath = Path.Combine(
                        output,
                        buildTarget == BuildTarget.StandaloneWindows64 ? "Player.exe"
                            : buildTarget == BuildTarget.StandaloneLinux64 ? "Player.x86_64" : "Player.app");
                    break;
                case "webgl":
                    namedTarget = NamedBuildTarget.WebGL;
                    buildTarget = BuildTarget.WebGL;
                    locationPath = output;
                    if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                    {
                        throw new InvalidOperationException("Start Unity with -buildTarget WebGL.");
                    }

                    // The gate serves the build from a plain static server, so compression stays off.
                    PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                    PlayerSettings.WebGL.decompressionFallback = false;
                    break;
                default:
                    throw new InvalidOperationException("Unknown -pvTarget " + target);
            }

            PlayerSettings.SetScriptingBackend(
                namedTarget,
                backend == "il2cpp" ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(
                namedTarget,
                stripping == "high" ? ManagedStrippingLevel.High
                    : stripping == "medium" ? ManagedStrippingLevel.Medium
                    : stripping == "low" ? ManagedStrippingLevel.Low
                    : stripping == "disabled" ? ManagedStrippingLevel.Disabled : ManagedStrippingLevel.Minimal);
            PlayerSettings.productName = "PlayerVerification";
            PlayerSettings.runInBackground = true;

            Directory.CreateDirectory(output);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = buildTarget,
                targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget),
                locationPathName = locationPath,
                options = BuildOptions.StrictMode,
            };

            Debug.Log(
                "PlayerVerification building target=" + target + " backend=" + backend + " stripping=" + stripping
                + " defines=" + PlayerSettings.GetScriptingDefineSymbols(namedTarget));
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log(
                "PlayerVerification build result=" + summary.result + " errors=" + summary.totalErrors
                + " warnings=" + summary.totalWarnings + " seconds=" + summary.totalTime.TotalSeconds.ToString("F0"));
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("The Player build did not succeed: " + summary.result);
            }
        }

        private static string DefineFor(string combo)
        {
            switch (combo)
            {
                case "HttpNewtonsoft": return "PV_HTTP_NEWTONSOFT";
                case "HttpStj": return "PV_HTTP_STJ";
                case "UwrNewtonsoft": return "PV_UWR_NEWTONSOFT";
                case "UwrStj": return "PV_UWR_STJ";
                default: throw new InvalidOperationException("Unknown combo " + combo);
            }
        }

        private static string RequireArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (arguments[index] == name)
                {
                    return arguments[index + 1];
                }
            }

            throw new InvalidOperationException("The " + name + " argument is required.");
        }
    }
}
