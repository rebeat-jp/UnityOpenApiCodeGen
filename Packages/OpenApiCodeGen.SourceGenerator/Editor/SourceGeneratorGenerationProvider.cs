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
                "Source Generator",
                availability ?? throw new ArgumentNullException(nameof(availability)));
            this.cacheService = cacheService
                ?? throw new ArgumentNullException(nameof(cacheService));
            this.definitionWriter = definitionWriter
                ?? throw new ArgumentNullException(nameof(definitionWriter));
            this.requestScriptCompilation = requestScriptCompilation
                ?? throw new ArgumentNullException(nameof(requestScriptCompilation));
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
                cancellationToken.ThrowIfCancellationRequested();
                OpenApiDocumentFormat requestedDocumentFormat = DetectLocalDocumentFormat(
                    request.ApiDocumentFilePathOrUrl);
                OpenApiClientDefinitionPlan definitionPlan = definitionWriter.Prepare(
                    request.OutputFolderPath,
                    request.ApiName,
                    request.GeneratedNamespace,
                    requestedDocumentFormat);
                bool definitionChanged = false;
                NormalizedSpecCacheResult cacheResult = await cacheService.NormalizeAndCacheAsync(
                    request.ApiDocumentFilePathOrUrl,
                    definitionPlan.SpecId,
                    cancellationToken,
                    definitionPlan.DefinitionPath,
                    definitionPlan.DefinitionAssetPath,
                    format => PublishDefinition(definitionPlan, request, format, ref definitionChanged));

                return CompleteGeneration(cacheResult, definitionChanged);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return GenerationResult.Failure(SanitizeFailureMessage(exception));
            }
        }

        private GenerationResult GenerateSyncCore(GenerationRequest request)
        {
            try
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
                            CancellationToken.None),
                    CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                bool definitionChanged = false;
                NormalizedSpecCacheResult cacheResult = cacheService.PublishGraph(
                    graph,
                    definitionPlan.DefinitionPath,
                    definitionPlan.DefinitionAssetPath,
                    format => PublishDefinition(definitionPlan, request, format, ref definitionChanged));

                return CompleteGeneration(cacheResult, definitionChanged);
            }
            catch (Exception exception)
            {
                return GenerationResult.Failure(SanitizeFailureMessage(exception));
            }
        }

        private Action PublishDefinition(
            OpenApiClientDefinitionPlan definitionPlan,
            GenerationRequest request,
            OpenApiDocumentFormat format,
            ref bool definitionChanged)
        {
            if (definitionPlan.DocumentFormat != format)
            {
                definitionPlan = definitionWriter.Prepare(
                    request.OutputFolderPath,
                    request.ApiName,
                    request.GeneratedNamespace,
                    format);
            }

            DefinitionPublication publication = definitionWriter.PublishTransactional(definitionPlan);
            definitionChanged = publication.Changed;
            return publication.Rollback;
        }

        private GenerationResult CompleteGeneration(
            NormalizedSpecCacheResult cacheResult,
            bool definitionChanged)
        {
            bool compilerInputChanged = cacheResult.MirrorChanged || definitionChanged;
            if (compilerInputChanged)
            {
                requestScriptCompilation();
            }

            return GenerationResult.Success(
                (compilerInputChanged
                    ? GenerationSucceededMessage
                    : GenerationAlreadyCurrentMessage) + Environment.NewLine +
                "Spec ID: " + cacheResult.SpecId +
                (string.IsNullOrEmpty(cacheResult.WarningMessage)
                    ? string.Empty
                    : Environment.NewLine + "Warning: " + cacheResult.WarningMessage));
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

        private static string SanitizeFailureMessage(Exception exception)
        {
            string message = exception == null ? string.Empty : exception.Message;
            if (string.IsNullOrEmpty(message))
            {
                return "Source Generator generation failed.";
            }

            return message;
        }
    }
}
