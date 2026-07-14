#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Rhycol.OpenApiCodeGen.Editor.Generation;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class SourceGeneratorGenerationProvider : IGenerationProvider
    {
        internal const string GenerationNotImplementedMessage =
            "Source Generator generation is not implemented until Phase 3.";

        public GenerationProviderDescriptor Descriptor { get; }

        internal SourceGeneratorGenerationProvider(
            GenerationProviderAvailability availability)
        {
            Descriptor = new GenerationProviderDescriptor(
                GenerateProvider.SourceGenerator,
                "Source Generator",
                availability);
        }

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return GenerationResult.Failure(GenerationNotImplementedMessage);
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
            return Task.FromResult(
                GenerationResult.Failure(GenerationNotImplementedMessage));
        }
    }
}
