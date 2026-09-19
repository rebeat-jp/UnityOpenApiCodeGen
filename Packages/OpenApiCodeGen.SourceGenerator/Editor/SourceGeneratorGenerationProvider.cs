#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Rhycol.OpenApiCodeGen.SourceGenerator;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using UnityEditor.Compilation;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class SourceGeneratorGenerationProvider : IGenerationProvider
    {
        internal const string GenerationSucceededMessage =
            "Source Generator cache and definition were published; script compilation was requested.";
        internal const string GenerationAlreadyCurrentMessage =
            "Source Generator cache and definition are already current; no script compilation was requested.";

        private readonly NormalizedSpecCacheService cacheService;
        private readonly OpenApiClientDefinitionWriter definitionWriter;
        private readonly Action requestScriptCompilation;

        public GenerationProviderDescriptor Descriptor { get; }

        internal SourceGeneratorGenerationProvider(
            GenerationProviderAvailability availability)
            : this(
                availability,
                NormalizedSpecCacheService.CreateForCurrentProject(),
                OpenApiClientDefinitionWriter.CreateForCurrentProject(),
                CompilationPipeline.RequestScriptCompilation)
        {
        }

        internal SourceGeneratorGenerationProvider(
            GenerationProviderAvailability availability,
            NormalizedSpecCacheService cacheService,
            OpenApiClientDefinitionWriter definitionWriter,
            Action requestScriptCompilation)
        {
            Descriptor = new GenerationProviderDescriptor(
                GenerateProvider.SourceGenerator,
                "Source Generator (Beta)",
                availability ?? throw new ArgumentNullException(nameof(availability)));
            this.cacheService = cacheService
                ?? throw new ArgumentNullException(nameof(cacheService));
            this.definitionWriter = definitionWriter
                ?? throw new ArgumentNullException(nameof(definitionWriter));
            this.requestScriptCompilation = requestScriptCompilation
                ?? throw new ArgumentNullException(nameof(requestScriptCompilation));
            this.cacheService.CompilationRequester = this.requestScriptCompilation;
        }

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return GenerateSyncCore(request);
        }

        public Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Keep the caller's Unity SynchronizationContext. The cache service performs the
            // network/parse portion asynchronously, then this continuation publishes Assets and
            // requests compilation on the Editor thread.
            return GenerateAsyncCore(request, cancellationToken);
        }

        private async Task<GenerationResult> GenerateAsyncCore(
            GenerationRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                using (cacheService.AcquireGenerationLock())
                {
                cancellationToken.ThrowIfCancellationRequested();
                request.ReportProgress(0.1, "Preparing generation.");
                OpenApiDocumentFormat requestedDocumentFormat = DetectLocalDocumentFormat(
                    request.ApiDocumentFilePathOrUrl);
                OpenApiClientDefinitionPlan definitionPlan = definitionWriter.Prepare(
                    request.OutputFolderPath,
                    request.ApiName,
                    request.GeneratedNamespace,
                    requestedDocumentFormat);
                cacheService.RepairPendingPublicationForSpec(
                    definitionPlan.SpecId,
                    definitionPlan.DefinitionPath,
                    definitionPlan.DefinitionAssetPath);
                NormalizedSpecGraph graph = await cacheService.LoadGraphAsync(
                    request.ApiDocumentFilePathOrUrl,
                    definitionPlan.SpecId,
                    cancellationToken,
                    request.Progress);
                cancellationToken.ThrowIfCancellationRequested();
                definitionPlan = PrepareForGraphFormat(definitionPlan, graph.Format);
                bool definitionChanged = false;
                NormalizedSpecCacheResult cacheResult = cacheService.PublishGraph(
                    graph,
                    definitionPlan.DefinitionPath,
                    definitionPlan.DefinitionAssetPath,
                    format => PublishDefinition(definitionPlan, format, ref definitionChanged),
                    definitionPlan.Content,
                    definitionPlan.DefinitionArtifactPaths,
                    definitionPlan.DefinitionArtifactOutputs,
                    definitionPlan.RequiresPublication);

                request.ReportProgress(0.9, "Publishing generated inputs.");
                return CompleteGeneration(cacheResult, definitionChanged, request);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return GenerationResult.Failure(GetSafeFailureMessage(exception));
            }
        }

        private GenerationResult GenerateSyncCore(GenerationRequest request)
        {
            try
            {
                using (cacheService.AcquireGenerationLock())
                {
                OpenApiDocumentFormat requestedDocumentFormat = DetectLocalDocumentFormat(
                    request.ApiDocumentFilePathOrUrl);
                OpenApiClientDefinitionPlan definitionPlan = definitionWriter.Prepare(
                    request.OutputFolderPath,
                    request.ApiName,
                    request.GeneratedNamespace,
                    requestedDocumentFormat);
                cacheService.RepairPendingPublicationForSpec(
                    definitionPlan.SpecId,
                    definitionPlan.DefinitionPath,
                    definitionPlan.DefinitionAssetPath);
                // Match the Docker provider's synchronous boundary for fetch/parse, then publish
                // Unity assets on this caller thread after the worker has returned.
                NormalizedSpecGraph graph = Task.Run(
                        () => cacheService.LoadGraphAsync(
                            request.ApiDocumentFilePathOrUrl,
                    definitionPlan.SpecId,
                    CancellationToken.None,
                    request.Progress),
                    CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                definitionPlan = PrepareForGraphFormat(definitionPlan, graph.Format);
                bool definitionChanged = false;
                NormalizedSpecCacheResult cacheResult = cacheService.PublishGraph(
                    graph,
                    definitionPlan.DefinitionPath,
                    definitionPlan.DefinitionAssetPath,
                    format => PublishDefinition(definitionPlan, format, ref definitionChanged),
                    definitionPlan.Content,
                    definitionPlan.DefinitionArtifactPaths,
                    definitionPlan.DefinitionArtifactOutputs,
                    definitionPlan.RequiresPublication);

                return CompleteGeneration(cacheResult, definitionChanged, request);
                }
            }
            catch (Exception exception)
            {
                return GenerationResult.Failure(GetSafeFailureMessage(exception));
            }
        }

        private Action PublishDefinition(
            OpenApiClientDefinitionPlan definitionPlan,
            OpenApiDocumentFormat format,
            ref bool definitionChanged)
        {
            if (definitionPlan.DocumentFormat != format)
            {
                definitionPlan = definitionWriter.WithDocumentFormat(definitionPlan, format);
            }

            DefinitionPublication publication = definitionWriter.PublishTransactional(definitionPlan);
            definitionChanged = publication.Changed;
            return publication.Rollback;
        }

        private OpenApiClientDefinitionPlan PrepareForGraphFormat(
            OpenApiClientDefinitionPlan plan,
            string graphFormat)
        {
            OpenApiDocumentFormat format = string.Equals(graphFormat, "yaml", StringComparison.Ordinal)
                ? OpenApiDocumentFormat.Yaml
                : OpenApiDocumentFormat.Json;
            return plan.DocumentFormat == format
                ? plan
                : definitionWriter.WithDocumentFormat(plan, format);
        }

        private GenerationResult CompleteGeneration(
            NormalizedSpecCacheResult cacheResult,
            bool definitionChanged,
            GenerationRequest request)
        {
            bool compilerInputChanged = cacheResult.CompilationRequired;
            if (compilerInputChanged)
            {
                // This callback reaches the Editor synchronization context supplied by the UI.
                // This report is intentionally after publication, before requesting Unity compilation.
                cacheService.MarkCompilationRequested(cacheResult.SpecId);
                requestScriptCompilation();
                request.ReportProgress(1.0, "Script compilation was requested.");
                cacheService.AcknowledgeCompilationRequest(cacheResult.SpecId);
            }
            else
            {
                cacheService.AcknowledgeCompilationRequest(cacheResult.SpecId);
                request.ReportProgress(1.0, "Generation is current.");
            }

            var warnings = string.IsNullOrEmpty(cacheResult.WarningMessage)
                ? Array.Empty<string>()
                : new[] { cacheResult.WarningMessage };
            return GenerationResult.Success(
                (compilerInputChanged
                    ? GenerationSucceededMessage
                    : GenerationAlreadyCurrentMessage) + Environment.NewLine +
                "Spec ID: " + cacheResult.SpecId,
                warnings);
        }

        private static OpenApiDocumentFormat DetectLocalDocumentFormat(string documentPath)
        {
            if (string.IsNullOrWhiteSpace(documentPath))
            {
                throw new ArgumentException("A local OpenAPI JSON or YAML document path is required.");
            }

            Uri absoluteUri;
            if (!Path.IsPathRooted(documentPath) &&
                Uri.TryCreate(documentPath, UriKind.Absolute, out absoluteUri))
            {
                if (absoluteUri.Scheme == Uri.UriSchemeHttp ||
                    absoluteUri.Scheme == Uri.UriSchemeHttps)
                {
                    // URL content type/final URL extension is authoritative and is resolved by
                    // ExternalSpecGraphLoader after the request has been fetched. Json is only the
                    // temporary definition-plan format used to calculate the stable spec ID.
                    return OpenApiDocumentFormat.Json;
                }

                throw new NotSupportedException(
                    "Source Generator generation supports local paths and HTTP(S) URLs only.");
            }

            string extension = Path.GetExtension(documentPath);
            if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                return OpenApiDocumentFormat.Json;
            }

            if (string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
            {
                return OpenApiDocumentFormat.Yaml;
            }

            throw new NotSupportedException(
                "Source Generator generation supports local .json, .yaml, and .yml documents, or HTTP(S) URLs.");
        }

        private static string GetSafeFailureMessage(Exception exception)
        {
            // Loader/normalizer diagnostics are already redacted. Do not surface arbitrary
            // framework exception text: it may contain a local path, request URI, or headers.
            return exception is SafeGenerationException || exception is NormalizedSpecException || exception is TimeoutException ||
                   exception is NotSupportedException || exception is ArgumentException
                ? exception.Message
                : "Source Generator generation failed.";
        }
    }
}
