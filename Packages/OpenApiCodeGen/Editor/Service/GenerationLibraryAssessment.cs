#nullable enable

using System;
using System.IO;

using Rhycol.OpenApiCodeGen.Editor.Generation;

namespace Rhycol.OpenApiCodeGen.Core
{
    /// <summary>
    /// The library state of the selected provider for one output folder. It is independent of provider
    /// availability: an unavailable provider would lose its scripting define and break existing definitions.
    /// </summary>
    internal sealed class GenerationLibraryAssessment
    {
        GenerationLibraryAssessment(
            bool isApplicable,
            GenerationLibraryReport? report,
            GenerationHttpTransport httpTransport,
            GenerationJsonSerializer jsonSerializer)
        {
            IsApplicable = isApplicable;
            Report = report;
            HttpTransport = httpTransport;
            JsonSerializer = jsonSerializer;
        }

        /// <summary>False when the selected provider does not report libraries (for example Docker).</summary>
        public bool IsApplicable { get; }

        /// <summary>Null when the provider reports libraries but no output folder is set yet.</summary>
        public GenerationLibraryReport? Report { get; }

        public bool IsEvaluated => Report != null;

        public GenerationHttpTransport HttpTransport { get; }

        public GenerationJsonSerializer JsonSerializer { get; }

        public static GenerationLibraryAssessment NotApplicable { get; } =
            new GenerationLibraryAssessment(
                false,
                null,
                GenerationHttpTransport.HttpClient,
                GenerationJsonSerializer.NewtonsoftJson);

        public static GenerationLibraryAssessment Assess(
            GenerationProviderRegistry registry,
            GenerateProvider provider,
            string? outputFolderPath,
            GenerationHttpTransport httpTransport,
            GenerationJsonSerializer jsonSerializer)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            GenerationProviderResolution resolution = registry.Resolve(provider);
            if (!resolution.IsResolved ||
                !(resolution.Provider is IGenerationLibraryInspector inspector))
            {
                return NotApplicable;
            }

            if (string.IsNullOrWhiteSpace(outputFolderPath))
            {
                return new GenerationLibraryAssessment(true, null, httpTransport, jsonSerializer);
            }

            GenerationLibraryReport report;
            try
            {
                // GenerationService passes the same absolute form to the provider.
                report = inspector.Inspect(Path.GetFullPath(outputFolderPath));
            }
            catch (Exception exception)
            {
                report = GenerationLibraryReport.Unresolved(
                    "The library check failed: " + exception.Message);
            }

            return new GenerationLibraryAssessment(true, report, httpTransport, jsonSerializer);
        }
    }
}
