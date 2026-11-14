#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;

namespace Rhycol.OpenApiCodeGen.UI
{
    /// <summary>What a button next to a library does.</summary>
    internal enum LibraryInstallAction
    {
        None = 0,
        AddNewtonsoftJsonPackage = 1,
        EnableUnityWebRequestModule = 2,
        ShowSystemTextJsonSteps = 3,
    }

    /// <summary>One selectable library in the Project Settings view.</summary>
    internal sealed class LibraryChoiceDto
    {
        public LibraryChoiceDto(
            string label,
            string statusText,
            bool isInstalled,
            bool isSelectable,
            string guidance,
            LibraryInstallAction action,
            string actionLabel)
        {
            Label = label;
            StatusText = statusText;
            IsInstalled = isInstalled;
            IsSelectable = isSelectable;
            Guidance = guidance;
            Action = action;
            ActionLabel = actionLabel;
        }

        public string Label { get; }
        public string StatusText { get; }
        public bool IsInstalled { get; }

        /// <summary>False when the choice cannot be picked because its library is not ready.</summary>
        public bool IsSelectable { get; }
        public string Guidance { get; }
        public LibraryInstallAction Action { get; }
        public string ActionLabel { get; }
    }

    /// <summary>The Source Generator Libraries section of the Project Settings view.</summary>
    internal sealed class SourceGeneratorLibrariesDto
    {
        public SourceGeneratorLibrariesDto(
            bool isVisible,
            string message,
            IReadOnlyList<LibraryChoiceDto> httpChoices,
            IReadOnlyList<LibraryChoiceDto> jsonChoices,
            string webGlNotice,
            string webGlWarning)
        {
            IsVisible = isVisible;
            Message = message;
            HttpChoices = httpChoices;
            JsonChoices = jsonChoices;
            WebGlNotice = webGlNotice;
            WebGlWarning = webGlWarning;
        }

        public static SourceGeneratorLibrariesDto Hidden { get; } = new SourceGeneratorLibrariesDto(
            false,
            string.Empty,
            Array.Empty<LibraryChoiceDto>(),
            Array.Empty<LibraryChoiceDto>(),
            string.Empty,
            string.Empty);

        public bool IsVisible { get; }

        /// <summary>A section-level note, for example that no output folder is set.</summary>
        public string Message { get; }

        /// <summary>Indexed by <see cref="GenerationHttpTransport"/>.</summary>
        public IReadOnlyList<LibraryChoiceDto> HttpChoices { get; }

        /// <summary>Indexed by <see cref="GenerationJsonSerializer"/>.</summary>
        public IReadOnlyList<LibraryChoiceDto> JsonChoices { get; }

        /// <summary>Recommendation shown when the active build target is WebGL.</summary>
        public string WebGlNotice { get; }

        /// <summary>Warning shown when the active build target is WebGL and HttpClient is selected.</summary>
        public string WebGlWarning { get; }
    }

    /// <summary>The library line and blocking reasons of the Generator window.</summary>
    internal sealed class GenerationLibraryReadinessDto
    {
        public GenerationLibraryReadinessDto(
            bool isApplicable,
            string summary,
            bool isBlocking,
            IReadOnlyList<string> problems,
            IReadOnlyList<string> warnings)
        {
            IsApplicable = isApplicable;
            Summary = summary;
            IsBlocking = isBlocking;
            Problems = problems;
            Warnings = warnings;
        }

        public static GenerationLibraryReadinessDto NotApplicable { get; } =
            new GenerationLibraryReadinessDto(
                false,
                string.Empty,
                false,
                Array.Empty<string>(),
                Array.Empty<string>());

        /// <summary>False when the provider does not report libraries; the view hides the line.</summary>
        public bool IsApplicable { get; }

        /// <summary>For example "Libraries: HttpClient + Newtonsoft.Json (ready)".</summary>
        public string Summary { get; }

        /// <summary>True when Generate must be disabled.</summary>
        public bool IsBlocking { get; }

        public IReadOnlyList<string> Problems { get; }

        public IReadOnlyList<string> Warnings { get; }

        /// <summary>The text that explains a blocked generation, one problem per line.</summary>
        public string BuildBlockingMessage()
        {
            var builder = new StringBuilder(Summary);
            foreach (string problem in Problems)
            {
                builder.AppendLine().Append("- ").Append(problem);
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Turns a library assessment into view models. Pure so the state rules can be tested without a window.
    /// </summary>
    internal static class SourceGeneratorLibraryPresentation
    {
        internal const string UnityNuGetRegistryUrl = "https://unitynuget-registry.openupm.com";
        internal const string SystemTextJsonPackageId = "org.nuget.system.text.json";
        internal const string SystemTextJsonPackageVersion = "8.0.5";
        internal const string NewtonsoftJsonPackageSpecifier = "com.unity.nuget.newtonsoft-json@3.2.2";
        internal const string UnityWebRequestModuleSpecifier = "com.unity.modules.unitywebrequest";

        internal static string GetHttpTransportName(GenerationHttpTransport transport)
        {
            return transport == GenerationHttpTransport.UnityWebRequest ? "UnityWebRequest" : "HttpClient";
        }

        internal static string GetJsonSerializerName(GenerationJsonSerializer serializer)
        {
            return serializer == GenerationJsonSerializer.SystemTextJson ? "System.Text.Json" : "Newtonsoft.Json";
        }

        internal static string DescribeSelection(
            GenerationHttpTransport transport,
            GenerationJsonSerializer serializer)
        {
            return GetHttpTransportName(transport) + " + " + GetJsonSerializerName(serializer);
        }

        /// <summary>Whether the active build target is WebGL, where HttpClient does not work.</summary>
        internal static bool IsWebGlBuildTarget()
        {
            return UnityEditor.EditorUserBuildSettings.activeBuildTarget == UnityEditor.BuildTarget.WebGL;
        }

        internal static GenerationLibraryReadinessDto BuildReadiness(
            GenerationLibraryAssessment assessment,
            bool isWebGlTarget)
        {
            if (assessment == null)
            {
                throw new ArgumentNullException(nameof(assessment));
            }

            if (!assessment.IsApplicable)
            {
                return GenerationLibraryReadinessDto.NotApplicable;
            }

            string selection = DescribeSelection(assessment.HttpTransport, assessment.JsonSerializer);
            var warnings = new List<string>();
            if (isWebGlTarget && assessment.HttpTransport == GenerationHttpTransport.HttpClient)
            {
                warnings.Add(
                    "The active build target is WebGL, where HttpClient does not work. Select UnityWebRequest in Project Settings.");
            }

            GenerationLibraryReport? report = assessment.Report;
            if (report == null)
            {
                return new GenerationLibraryReadinessDto(
                    true,
                    "Libraries: " + selection + " (set the output folder to check them)",
                    false,
                    Array.Empty<string>(),
                    warnings);
            }

            if (report.ResolutionFailure.Length != 0)
            {
                // The definition checks report an unusable output folder; do not block here.
                return new GenerationLibraryReadinessDto(
                    true,
                    "Libraries: " + selection + " (not checked: " + report.ResolutionFailure + ")",
                    false,
                    Array.Empty<string>(),
                    warnings);
            }

            IReadOnlyList<string> problems = report.GetProblems(assessment.HttpTransport, assessment.JsonSerializer);
            return new GenerationLibraryReadinessDto(
                true,
                "Libraries: " + selection + (problems.Count == 0 ? " (ready)" : " (not ready)"),
                problems.Count != 0,
                problems,
                warnings);
        }

        internal static SourceGeneratorLibrariesDto BuildSettings(
            GenerationLibraryAssessment assessment,
            bool isWebGlTarget)
        {
            if (assessment == null)
            {
                throw new ArgumentNullException(nameof(assessment));
            }

            if (!assessment.IsApplicable)
            {
                return SourceGeneratorLibrariesDto.Hidden;
            }

            GenerationLibraryReport? report = assessment.Report;
            bool resolved = report != null && report.ResolutionFailure.Length == 0;
            string message = report == null
                ? "Set the Default Output Folder Path to check which libraries the output assembly can use."
                : report.ResolutionFailure;

            var httpChoices = new LibraryChoiceDto[2];
            httpChoices[(int)GenerationHttpTransport.HttpClient] = BuildChoice(
                "HttpClient",
                resolved ? report!.HttpClient : null,
                LibraryInstallAction.None,
                string.Empty);
            httpChoices[(int)GenerationHttpTransport.UnityWebRequest] = BuildUnityWebRequestChoice(
                resolved ? report! : null);

            var jsonChoices = new LibraryChoiceDto[2];
            jsonChoices[(int)GenerationJsonSerializer.NewtonsoftJson] = BuildChoice(
                "Newtonsoft.Json",
                resolved ? report!.NewtonsoftJson : null,
                LibraryInstallAction.AddNewtonsoftJsonPackage,
                "Install Newtonsoft.Json (" + NewtonsoftJsonPackageSpecifier + ")");
            jsonChoices[(int)GenerationJsonSerializer.SystemTextJson] = BuildChoice(
                "System.Text.Json",
                resolved ? report!.SystemTextJson : null,
                LibraryInstallAction.ShowSystemTextJsonSteps,
                "Show installation steps");

            string notice = string.Empty;
            string warning = string.Empty;
            if (isWebGlTarget)
            {
                notice = "The active build target is WebGL. UnityWebRequest is recommended: HttpClient does not work in WebGL players.";
                if (assessment.HttpTransport == GenerationHttpTransport.HttpClient)
                {
                    warning = "HttpClient is selected while the active build target is WebGL. The analyzer reports OACG014 and the generated client will not work in a WebGL player.";
                }
            }

            return new SourceGeneratorLibrariesDto(true, message, httpChoices, jsonChoices, notice, warning);
        }

        /// <summary>
        /// The manual steps for System.Text.Json: the manifest is never edited automatically. Also covers
        /// System.Text.Encodings.Web, which the generated code needs at run time.
        /// </summary>
        internal static string BuildSystemTextJsonSteps()
        {
            return "System.Text.Json " + "8.0 or later is installed from the UnityNuGet registry. "
                + "NuGetForUnity or manually placed DLLs also work, as long as the output assembly references them." + Environment.NewLine
                + Environment.NewLine
                + "1. Add this scoped registry to Packages/manifest.json (or Project Settings > Package Manager):" + Environment.NewLine
                + "\"scopedRegistries\": [" + Environment.NewLine
                + "  {" + Environment.NewLine
                + "    \"name\": \"Unity NuGet\"," + Environment.NewLine
                + "    \"url\": \"" + UnityNuGetRegistryUrl + "\"," + Environment.NewLine
                + "    \"scopes\": [\"org.nuget\"]" + Environment.NewLine
                + "  }" + Environment.NewLine
                + "]" + Environment.NewLine
                + Environment.NewLine
                + "2. Add this dependency (8.0 or later):" + Environment.NewLine
                + "\"" + SystemTextJsonPackageId + "\": \"" + SystemTextJsonPackageVersion + "\"" + Environment.NewLine
                + Environment.NewLine
                + "System.Text.Encodings.Web is installed with it. The generated code needs it at run time, "
                + "so keep it when you install the DLLs by another route.";
        }

        static LibraryChoiceDto BuildUnityWebRequestChoice(GenerationLibraryReport? report)
        {
            return BuildChoice(
                "UnityWebRequest",
                report?.UnityWebRequest,
                LibraryInstallAction.EnableUnityWebRequestModule,
                "Enable the Unity Web Request module",
                report?.HttpClient);
        }

        static LibraryChoiceDto BuildChoice(
            string label,
            GenerationLibraryState? state,
            LibraryInstallAction missingAction,
            string missingActionLabel,
            GenerationLibraryState? companion = null)
        {
            if (state == null)
            {
                // Not evaluated: leave every choice selectable.
                return new LibraryChoiceDto(
                    label,
                    "Not checked",
                    false,
                    true,
                    string.Empty,
                    LibraryInstallAction.None,
                    string.Empty);
            }

            string statusText = state.Describe();
            string guidance = state.InstallGuidance;
            bool installed = state.IsInstalled;
            if (installed && companion != null && !companion.IsInstalled)
            {
                // UnityWebRequest builds on the System.Net.Http request and response types.
                installed = false;
                statusText += "; " + companion.Describe();
                guidance = companion.InstallGuidance;
            }

            bool offersAction = !installed &&
                (state.Status == GenerationLibraryStatus.Missing ||
                 state.Status == GenerationLibraryStatus.TooOld);
            return new LibraryChoiceDto(
                label,
                statusText,
                installed,
                installed,
                guidance,
                offersAction ? missingAction : LibraryInstallAction.None,
                offersAction ? missingActionLabel : string.Empty);
        }
    }
}
