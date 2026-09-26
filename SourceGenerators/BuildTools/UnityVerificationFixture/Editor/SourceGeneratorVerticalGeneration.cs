using System;

using Rhycol.OpenApiCodeGen.Editor.Generation;

using UnityEngine;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Editor
{
    /// <summary>
    /// Runs the public Source Generator provider route from a clean Unity project.
    /// </summary>
    public static class SourceGeneratorVerticalGeneration
    {
        private const string RawSpecPath =
            "Assets/OpenApiCodeGen/SourceGeneratorVerification/Specs/openapi.json";
        private const string OutputFolderPath =
            "Assets/OpenApiCodeGen/SourceGeneratorVerification/Target/Generated";
        private const string ApiName = "Api";
        private const string GeneratedNamespace = "Rhycol.OpenApiCodeGen.Generated";

        public static void Generate()
        {
#if !OPENAPI_CODEGEN_SOURCE_GENERATOR
            throw new InvalidOperationException(
                "OPENAPI_CODEGEN_SOURCE_GENERATOR was not enabled from the selected project setting.");
#else
            GenerationProviderRegistry registry = GenerationProviderRegistry.Shared;
            DisableDockerProvider(registry);
            GenerationProviderResolution resolution =
                registry.Resolve(GenerateProvider.SourceGenerator);
            if (!resolution.IsResolved || resolution.Provider == null)
            {
                throw new InvalidOperationException(
                    "The registered Source Generator provider could not be resolved. "
                    + resolution.FailureReason);
            }

            IGenerationProvider provider = resolution.Provider;
            if (provider.Descriptor.Provider != GenerateProvider.SourceGenerator)
            {
                throw new InvalidOperationException(
                    $"The resolved provider was '{provider.Descriptor.Provider}', not SourceGenerator.");
            }

            if (!provider.Descriptor.Availability.IsAvailable)
            {
                throw new InvalidOperationException(
                    "The Source Generator provider is unavailable. "
                    + provider.Descriptor.Availability.Reason);
            }

            GenerationResult result = provider.Generate(
                new GenerationRequest(
                    RawSpecPath,
                    OutputFolderPath,
                    ApiName,
                    GeneratedNamespace));
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    "The Source Generator provider failed. " + result.Message);
            }

            Debug.Log(
                "OpenApiCodeGen Unity vertical generation passed with provider SourceGenerator."
                + Environment.NewLine
                + result.Message);
#endif
        }

        private static void DisableDockerProvider(GenerationProviderRegistry registry)
        {
            if (registry.Resolve(GenerateProvider.OpenApi).IsResolved
                && !registry.TryUnregister(
                    GenerateProvider.OpenApi,
                    out string failureReason))
            {
                throw new InvalidOperationException(
                    "The Docker provider could not be disabled for vertical verification. "
                    + failureReason);
            }

            if (registry.Resolve(GenerateProvider.OpenApi).IsResolved)
            {
                throw new InvalidOperationException(
                    "The Docker provider remained resolved during Source Generator verification.");
            }
        }
    }
}
