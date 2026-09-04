#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using UnityEditor.PackageManager;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal interface ISourceGeneratorPackagePathResolver
    {
        string? GetResolvedPath();
    }

    internal sealed class PackageInfoSourceGeneratorPackagePathResolver
        : ISourceGeneratorPackagePathResolver
    {
        public string? GetResolvedPath()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(
                typeof(PackageInfoSourceGeneratorPackagePathResolver).Assembly);
            return packageInfo?.resolvedPath;
        }
    }

    internal sealed class SourceGeneratorAvailabilityProbe
    {
        internal const string AnalyzerFileName =
            "Rhycol.OpenApiCodeGen.SourceGenerator.dll";
        internal const string RoslynAnalyzerLabel = "RoslynAnalyzer";

        readonly ISourceGeneratorPackagePathResolver _packagePathResolver;

        internal SourceGeneratorAvailabilityProbe(
            ISourceGeneratorPackagePathResolver packagePathResolver)
        {
            _packagePathResolver = packagePathResolver
                ?? throw new ArgumentNullException(nameof(packagePathResolver));
        }

        internal GenerationProviderAvailability Probe()
        {
            try
            {
                string? resolvedPath = _packagePathResolver.GetResolvedPath();
                if (string.IsNullOrWhiteSpace(resolvedPath))
                {
                    return GenerationProviderAvailability.Unavailable(
                        "The Source Generator package resolved path could not be determined.");
                }

                if (!Directory.Exists(resolvedPath))
                {
                    return GenerationProviderAvailability.Unavailable(
                        $"The Source Generator package resolved path does not exist: '{resolvedPath}'.");
                }

                string analyzerPath = Path.Combine(
                    resolvedPath,
                    "Runtime",
                    "Analyzers",
                    AnalyzerFileName);
                if (!File.Exists(analyzerPath))
                {
                    return GenerationProviderAvailability.Unavailable(
                        $"The Source Generator analyzer DLL was not found: '{analyzerPath}'.");
                }

                string metadataPath = analyzerPath + ".meta";
                if (!File.Exists(metadataPath))
                {
                    return GenerationProviderAvailability.Unavailable(
                        $"The Source Generator analyzer metadata was not found: '{metadataPath}'.");
                }

                if (!HasRoslynAnalyzerLabel(File.ReadLines(metadataPath)))
                {
                    return GenerationProviderAvailability.Unavailable(
                        $"The Source Generator analyzer metadata is missing the "
                        + $"'{RoslynAnalyzerLabel}' label: '{metadataPath}'.");
                }

                return GenerationProviderAvailability.Available();
            }
            catch (Exception exception)
            {
                return GenerationProviderAvailability.Unavailable(
                    "The Source Generator availability check failed: " + exception.Message);
            }
        }

        static bool HasRoslynAnalyzerLabel(IEnumerable<string> metadataLines)
        {
            bool readingLabels = false;
            foreach (string line in metadataLines)
            {
                string trimmedLine = line.Trim();
                if (!readingLabels)
                {
                    readingLabels = string.Equals(
                        trimmedLine,
                        "labels:",
                        StringComparison.Ordinal);
                    continue;
                }

                if (string.Equals(
                        trimmedLine,
                        "- " + RoslynAnalyzerLabel,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                if (trimmedLine.Length > 0
                    && !trimmedLine.StartsWith("-", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return false;
        }
    }
}
