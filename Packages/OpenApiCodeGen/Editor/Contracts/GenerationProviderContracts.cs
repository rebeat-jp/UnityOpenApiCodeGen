#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Unity.OpenApiCodeGen.Editor")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Unity.OpenApiCodeGen.SourceGenerator.Editor")]

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

        internal IProgress<GenerationProgress>? Progress { get; set; }

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
