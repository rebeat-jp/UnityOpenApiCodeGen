#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

            return GenerateCore(request);
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
            return Task.FromResult(GenerateCore(request));
        }

        private GenerationResult GenerateCore(GenerationRequest request)
        {
            try
            {
                ValidateLocalJsonPath(request.ApiDocumentFilePathOrUrl);
                OpenApiClientDefinitionPlan definitionPlan = definitionWriter.Prepare(
                    request.OutputFolderPath,
                    request.ApiName,
                    request.GeneratedNamespace);
                NormalizedSpecCacheResult cacheResult = cacheService.NormalizeAndCache(
                    request.ApiDocumentFilePathOrUrl,
                    definitionPlan.SpecId);
                bool definitionChanged = definitionWriter.Publish(definitionPlan);
                bool compilerInputChanged = cacheResult.MirrorChanged || definitionChanged;
                if (compilerInputChanged)
                {
                    requestScriptCompilation();
                }

                return GenerationResult.Success(
                    (compilerInputChanged
                        ? GenerationSucceededMessage
                        : GenerationAlreadyCurrentMessage) + Environment.NewLine +
                    "Spec ID: " + cacheResult.SpecId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return GenerationResult.Failure(exception.Message);
            }
        }

        private static void ValidateLocalJsonPath(string documentPath)
        {
            if (string.IsNullOrWhiteSpace(documentPath))
            {
                throw new ArgumentException("A local OpenAPI JSON document path is required.");
            }

            Uri absoluteUri;
            if (!Path.IsPathRooted(documentPath) &&
                Uri.TryCreate(documentPath, UriKind.Absolute, out absoluteUri))
            {
                throw new NotSupportedException(
                    "Source Generator generation supports local files only; URLs are not supported.");
            }

            if (!string.Equals(
                    Path.GetExtension(documentPath),
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    "Source Generator generation currently supports local .json documents only; YAML is not supported.");
            }
        }
    }
}
