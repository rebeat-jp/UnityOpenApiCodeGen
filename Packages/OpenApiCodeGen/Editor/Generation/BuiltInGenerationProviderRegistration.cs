#nullable enable

using UnityEditor;
using UnityEngine;

namespace Rhycol.OpenApiCodeGen.Editor.Generation
{
    [InitializeOnLoad]
    internal static class BuiltInGenerationProviderRegistration
    {
        static BuiltInGenerationProviderRegistration()
        {
            EnsureRegistered();
        }

        internal static void EnsureRegistered()
        {
            GenerationProviderRegistry registry = GenerationProviderRegistry.Shared;
            if (registry.Resolve(GenerateProvider.OpenApi).IsResolved)
            {
                return;
            }

            if (registry.TryRegister(new DockerGenerationProvider(), out string failureReason))
            {
                return;
            }

            // Registration may have raced with another initializer. First registration
            // wins by contract, so only report an error when the provider is still absent.
            if (!registry.Resolve(GenerateProvider.OpenApi).IsResolved)
            {
                Debug.LogError(
                    $"Failed to register the built-in Docker generation provider: "
                    + failureReason);
            }
        }
    }
}
