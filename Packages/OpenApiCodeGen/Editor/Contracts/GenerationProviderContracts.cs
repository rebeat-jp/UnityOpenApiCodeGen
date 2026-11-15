#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Unity.OpenApiCodeGen.Editor")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Unity.OpenApiCodeGen.SourceGenerator.Editor")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Unity.OpenApiCodeGen.Test")]

namespace Rhycol.OpenApiCodeGen.Editor.Generation
{
    /// <summary>
    /// Identifies the generation implementation selected in project settings.
    /// Numeric values are persisted and must remain stable.
    /// </summary>
    public enum GenerateProvider
    {
        OpenApi = 0,
        SourceGenerator = 1,
    }

    /// <summary>
    /// The HTTP implementation a provider should generate against.
    /// Numeric values are persisted and must remain stable.
    /// </summary>
    public enum GenerationHttpTransport
    {
        HttpClient = 0,
        UnityWebRequest = 1,
    }

    /// <summary>
    /// The JSON serializer a provider should generate against.
    /// Numeric values are persisted and must remain stable.
    /// </summary>
    public enum GenerationJsonSerializer
    {
        NewtonsoftJson = 0,
        SystemTextJson = 1,
    }

    public sealed class GenerationProviderAvailability
    {
        public bool IsAvailable { get; }
        public string Reason { get; }

        private GenerationProviderAvailability(bool isAvailable, string reason)
        {
            IsAvailable = isAvailable;
            Reason = reason ?? string.Empty;
        }

        public static GenerationProviderAvailability Available()
        {
            return new GenerationProviderAvailability(true, string.Empty);
        }

        public static GenerationProviderAvailability Unavailable(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("An unavailable provider requires a reason.", nameof(reason));
            }

            return new GenerationProviderAvailability(false, reason);
        }
    }

    public sealed class GenerationProviderDescriptor
    {
        public GenerateProvider Provider { get; }
        public string DisplayName { get; }
        public GenerationProviderAvailability Availability { get; }

        public GenerationProviderDescriptor(
            GenerateProvider provider,
            string displayName,
            GenerationProviderAvailability availability)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A provider display name is required.", nameof(displayName));
            }

            Provider = provider;
            DisplayName = displayName;
            Availability = availability ?? throw new ArgumentNullException(nameof(availability));
        }
    }

    public sealed class GenerationRequest
    {
        public string ApiDocumentFilePathOrUrl { get; }
        public string OutputFolderPath { get; }
        public string ApiName { get; }
        public string GeneratedNamespace { get; }

        /// <summary>
        /// The HTTP implementation the generated client should use. Providers that do not
        /// support a choice ignore it. Defaults to <see cref="GenerationHttpTransport.HttpClient"/>.
        /// </summary>
        public GenerationHttpTransport HttpTransport { get; set; } = GenerationHttpTransport.HttpClient;

        /// <summary>
        /// The JSON serializer the generated client should use. Providers that do not
        /// support a choice ignore it. Defaults to <see cref="GenerationJsonSerializer.NewtonsoftJson"/>.
        /// </summary>
        public GenerationJsonSerializer JsonSerializer { get; set; } = GenerationJsonSerializer.NewtonsoftJson;

        internal IProgress<GenerationProgress>? Progress { get; set; }

        /// <summary>
        /// User-local GitHub token. Providers must send it only where
        /// <see cref="GitHubAuthorizationPolicy"/> allows and never persist or log it.
        /// </summary>
        internal string? GitHubToken { get; set; }

        internal void ReportProgress(double value, string stage)
        {
            Progress?.Report(new GenerationProgress(value, stage));
        }

        public GenerationRequest(string apiDocumentFilePathOrUrl, string outputFolderPath)
            : this(
                apiDocumentFilePathOrUrl,
                outputFolderPath,
                "Api",
                "Rhycol.OpenApiCodeGen")
        {
        }

        public GenerationRequest(
            string apiDocumentFilePathOrUrl,
            string outputFolderPath,
            string apiName,
            string generatedNamespace)
        {
            ApiDocumentFilePathOrUrl = apiDocumentFilePathOrUrl
                ?? throw new ArgumentNullException(nameof(apiDocumentFilePathOrUrl));
            OutputFolderPath = outputFolderPath
                ?? throw new ArgumentNullException(nameof(outputFolderPath));
            ApiName = apiName ?? throw new ArgumentNullException(nameof(apiName));
            GeneratedNamespace = generatedNamespace
                ?? throw new ArgumentNullException(nameof(generatedNamespace));
        }
    }

    internal readonly struct GenerationProgress
    {
        internal GenerationProgress(double value, string stage)
        {
            Value = value < 0 ? 0 : value > 1 ? 1 : value;
            Stage = stage ?? string.Empty;
        }

        internal double Value { get; }
        internal string Stage { get; }
    }


    public sealed class GenerationResult
    {
        public bool IsSuccess { get; }
        public string Message { get; }
        public IReadOnlyList<string> Warnings { get; }

        private GenerationResult(bool isSuccess, string message, IReadOnlyList<string>? warnings)
        {
            IsSuccess = isSuccess;
            Message = message ?? string.Empty;
            Warnings = warnings == null ? Array.Empty<string>() : new List<string>(warnings).AsReadOnly();
        }

        // Keep the original binary-compatible member for consumers compiled against 0.4.x.
        public static GenerationResult Success(string message = "")
        {
            return new GenerationResult(true, message, Array.Empty<string>());
        }

        public static GenerationResult Success(string message, IReadOnlyList<string>? warnings)
        {
            return new GenerationResult(true, message, warnings);
        }

        public static GenerationResult Failure(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A failed generation requires a message.", nameof(message));
            }

            return new GenerationResult(false, message, Array.Empty<string>());
        }
    }

    public interface IGenerationProvider
    {
        GenerationProviderDescriptor Descriptor { get; }

        GenerationResult Generate(GenerationRequest request);

        Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The state of one library a generated client can depend on.
    /// Numeric values are not persisted.
    /// </summary>
    public enum GenerationLibraryStatus
    {
        /// <summary>The target assembly references the library at a supported version.</summary>
        Installed = 0,

        /// <summary>The library is not present in the project.</summary>
        Missing = 1,

        /// <summary>The library is in the project but the output assembly does not reference it.</summary>
        NotReferenced = 2,

        /// <summary>The referenced library is older than the minimum supported version.</summary>
        TooOld = 3,
    }

    /// <summary>
    /// Describes one library for the output assembly: its status, detected version and how to install it.
    /// </summary>
    public sealed class GenerationLibraryState
    {
        public string Name { get; }
        public GenerationLibraryStatus Status { get; }

        /// <summary>The detected version, or an empty string when unknown or absent.</summary>
        public string Version { get; }

        /// <summary>The minimum supported version, or an empty string when there is none.</summary>
        public string RequiredVersion { get; }

        /// <summary>A human-readable instruction for resolving a non-installed status. Empty when installed.</summary>
        public string InstallGuidance { get; }

        public bool IsInstalled => Status == GenerationLibraryStatus.Installed;

        public GenerationLibraryState(
            string name,
            GenerationLibraryStatus status,
            string? version = null,
            string? requiredVersion = null,
            string? installGuidance = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A library name is required.", nameof(name));
            }

            Name = name;
            Status = status;
            Version = version ?? string.Empty;
            RequiredVersion = requiredVersion ?? string.Empty;
            InstallGuidance = installGuidance ?? string.Empty;
        }

        /// <summary>A one-line status description such as "Newtonsoft.Json 13.0.3 (installed)".</summary>
        public string Describe()
        {
            string versionText = Version.Length == 0 ? string.Empty : " " + Version;
            switch (Status)
            {
                case GenerationLibraryStatus.Installed:
                    return Name + versionText + " (installed)";
                case GenerationLibraryStatus.NotReferenced:
                    return Name + " (present in the project, but not referenced by the output assembly)";
                case GenerationLibraryStatus.TooOld:
                    return Name + versionText + " (version " + RequiredVersion + " or later is required)";
                default:
                    return Name + " (not installed)";
            }
        }
    }

    /// <summary>
    /// The library states of one output folder, evaluated by <see cref="IGenerationLibraryInspector"/>.
    /// </summary>
    public sealed class GenerationLibraryReport
    {
        public GenerationLibraryState NewtonsoftJson { get; }
        public GenerationLibraryState SystemTextJson { get; }
        public GenerationLibraryState HttpClient { get; }
        public GenerationLibraryState UnityWebRequest { get; }

        /// <summary>Non-empty when the output folder could not be evaluated; every selection is then not ready.</summary>
        public string ResolutionFailure { get; }

        public GenerationLibraryReport(
            GenerationLibraryState newtonsoftJson,
            GenerationLibraryState systemTextJson,
            GenerationLibraryState httpClient,
            GenerationLibraryState unityWebRequest)
            : this(newtonsoftJson, systemTextJson, httpClient, unityWebRequest, string.Empty)
        {
        }

        private GenerationLibraryReport(
            GenerationLibraryState newtonsoftJson,
            GenerationLibraryState systemTextJson,
            GenerationLibraryState httpClient,
            GenerationLibraryState unityWebRequest,
            string resolutionFailure)
        {
            NewtonsoftJson = newtonsoftJson ?? throw new ArgumentNullException(nameof(newtonsoftJson));
            SystemTextJson = systemTextJson ?? throw new ArgumentNullException(nameof(systemTextJson));
            HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            UnityWebRequest = unityWebRequest ?? throw new ArgumentNullException(nameof(unityWebRequest));
            ResolutionFailure = resolutionFailure ?? string.Empty;
        }

        /// <summary>Creates a report for an output folder that could not be evaluated.</summary>
        public static GenerationLibraryReport Unresolved(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("An unresolved report requires a reason.", nameof(reason));
            }

            GenerationLibraryState Unknown(string name)
            {
                return new GenerationLibraryState(name, GenerationLibraryStatus.Missing);
            }

            return new GenerationLibraryReport(
                Unknown("Newtonsoft.Json"),
                Unknown("System.Text.Json"),
                Unknown("System.Net.Http"),
                Unknown("UnityWebRequest"),
                reason);
        }

        public GenerationLibraryState GetJsonSerializer(GenerationJsonSerializer serializer)
        {
            return serializer == GenerationJsonSerializer.SystemTextJson ? SystemTextJson : NewtonsoftJson;
        }

        public GenerationLibraryState GetHttpTransport(GenerationHttpTransport transport)
        {
            return transport == GenerationHttpTransport.UnityWebRequest ? UnityWebRequest : HttpClient;
        }

        /// <summary>
        /// Whether the combination can be generated. UnityWebRequest also needs the System.Net.Http
        /// types because the generated adapter builds <c>HttpRequestMessage</c> / <c>HttpResponseMessage</c>.
        /// </summary>
        public bool IsReady(GenerationHttpTransport transport, GenerationJsonSerializer serializer)
        {
            return GetProblems(transport, serializer).Count == 0;
        }

        /// <summary>
        /// The blocking problems of the combination, each with the way to resolve it. Empty when ready.
        /// </summary>
        public IReadOnlyList<string> GetProblems(
            GenerationHttpTransport transport,
            GenerationJsonSerializer serializer)
        {
            var problems = new List<string>();
            if (ResolutionFailure.Length != 0)
            {
                problems.Add(ResolutionFailure);
                return problems;
            }

            AddProblem(problems, GetJsonSerializer(serializer));
            AddProblem(problems, GetHttpTransport(transport));
            if (transport == GenerationHttpTransport.UnityWebRequest)
            {
                AddProblem(problems, HttpClient);
            }

            return problems;
        }

        private static void AddProblem(List<string> problems, GenerationLibraryState state)
        {
            if (state.IsInstalled)
            {
                return;
            }

            string text = state.Describe();
            if (state.InstallGuidance.Length != 0)
            {
                text += ": " + state.InstallGuidance;
            }

            if (!problems.Contains(text))
            {
                problems.Add(text);
            }
        }
    }

    /// <summary>
    /// Optionally implemented by a provider that depends on libraries the project must supply.
    /// Existing providers need not implement it; <see cref="IGenerationProvider"/> is unchanged.
    /// </summary>
    public interface IGenerationLibraryInspector
    {
        /// <summary>
        /// Evaluates the libraries available to the assembly that will contain the generated output.
        /// Must not throw; return <see cref="GenerationLibraryReport.Unresolved"/> when evaluation fails.
        /// </summary>
        GenerationLibraryReport Inspect(string outputFolderPath);
    }

    public sealed class GenerationProviderResolution
    {
        public bool IsResolved => Provider != null;
        public IGenerationProvider? Provider { get; }
        public string FailureReason { get; }

        private GenerationProviderResolution(
            IGenerationProvider? provider,
            string failureReason)
        {
            Provider = provider;
            FailureReason = failureReason ?? string.Empty;
        }

        internal static GenerationProviderResolution Resolved(IGenerationProvider provider)
        {
            return new GenerationProviderResolution(
                provider ?? throw new ArgumentNullException(nameof(provider)),
                string.Empty);
        }

        internal static GenerationProviderResolution Unresolved(string reason)
        {
            return new GenerationProviderResolution(null, reason);
        }
    }
}
