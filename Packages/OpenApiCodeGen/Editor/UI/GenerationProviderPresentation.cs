#nullable enable
using System;
using System.IO;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rhycol.OpenApiCodeGen.UI
{
    internal static class GenerationProviderPresentation
    {
        internal const string SourceGeneratorDisplayName = "Source Generator (Beta)";
        internal const string SupportMatrixRelativePath =
            "Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.html";

        internal static string GetDisplayName(GenerateProvider provider)
        {
            if (provider == GenerateProvider.SourceGenerator) return SourceGeneratorDisplayName;
            GenerationProviderResolution resolution = GenerationProviderRegistry.Shared.Resolve(provider);
            return resolution.IsResolved
                ? resolution.Provider!.Descriptor.DisplayName
                : provider == GenerateProvider.OpenApi ? "Docker" : $"Unknown provider ({(int)provider})";
        }

        internal static void UpdateBetaNotice(VisualElement? notice, GenerateProvider provider)
        {
            notice?.EnableInClassList("source-generator-beta-hidden", provider != GenerateProvider.SourceGenerator);
        }

        internal static void BindSupportLink(VisualElement root)
        {
            Button? link = root.Q<Button>("SourceGeneratorSupportMatrix");
            if (link != null) link.clicked += OpenSupportMatrix;
        }

        internal static string GetSupportMatrixUrl(string packageRoot)
        {
            string path = Path.GetFullPath(Path.Combine(packageRoot, SupportMatrixRelativePath));
            if (!File.Exists(path)) throw new FileNotFoundException("The bundled Source Generator support matrix is missing.", path);
            return new Uri(path).AbsoluteUri;
        }

        private static void OpenSupportMatrix()
        {
            PackageInfo? package = PackageInfo.FindForAssembly(typeof(GenerationProviderPresentation).Assembly);
            if (package == null)
            {
                Debug.LogError("Cannot locate the OpenApiCodeGen package documentation.");
                return;
            }

            try { Application.OpenURL(GetSupportMatrixUrl(package.resolvedPath)); }
            catch (IOException exception) { Debug.LogError(exception.Message); }
        }
    }
}
