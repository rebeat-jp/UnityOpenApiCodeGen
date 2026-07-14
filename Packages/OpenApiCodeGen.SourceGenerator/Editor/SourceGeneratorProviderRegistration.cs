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
        }

        internal static void EnsureRegistered()
        {
            GenerationProviderRegistry registry = GenerationProviderRegistry.Shared;
            if (registry.Resolve(GenerateProvider.SourceGenerator).IsResolved)
            {
                return;
            }

            TryRegister(
                registry,
                new SourceGeneratorAvailabilityProbe(
                    new PackageInfoSourceGeneratorPackagePathResolver()),
                out _);
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
