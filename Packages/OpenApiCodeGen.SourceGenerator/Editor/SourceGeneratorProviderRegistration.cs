#nullable enable

using System;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using UnityEditor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    [InitializeOnLoad]
    internal static class SourceGeneratorProviderRegistration
    {
        static SourceGeneratorProviderRegistration()
        {
            EnsureRegistered();
            // Imports are deferred until Unity has finished loading assemblies.
            EditorApplication.delayCall += RecoverPublications;
        }

        internal static void EnsureRegistered()
        {
            GenerationProviderRegistry registry = GenerationProviderRegistry.Shared;
            if (!registry.Resolve(GenerateProvider.SourceGenerator).IsResolved)
            {
                TryRegister(
                    registry,
                    new SourceGeneratorAvailabilityProbe(
                        new PackageInfoSourceGeneratorPackagePathResolver()),
                    out _);
            }

        }

        private static void RecoverPublications()
        {
            try
            {
                NormalizedSpecCacheService.CreateForCurrentProject().RecoverPendingCompilations();
            }
            catch (Exception)
            {
                UnityEngine.Debug.LogError("Source Generator publication recovery could not acquire the project lock. Close the other generation process and retry Generate.");
            }
        }

        internal static bool TryRegister(
            GenerationProviderRegistry registry,
            SourceGeneratorAvailabilityProbe availabilityProbe,
            out string failureReason)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (availabilityProbe == null)
            {
                throw new ArgumentNullException(nameof(availabilityProbe));
            }

            var provider = new SourceGeneratorGenerationProvider(
                availabilityProbe.Probe());
            return registry.TryRegister(provider, out failureReason);
        }
    }
}
