#nullable enable
using Rhycol.OpenApiCodeGen.Editor.Generation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rhycol.OpenApiCodeGen.UI
{
    internal static class GenerationProviderPresentation
    {
        internal const string SourceGeneratorDisplayName = "Source Generator (Beta)";
        internal const string SupportMatrixUrl =
            "https://github.com/rebeat-jp/UnityOpenApiCodeGen/blob/feature/hirohiro/original-source-generator/SourceGenerators/OpenApiMvpSupportMatrix.md";

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
            if (link != null) link.clicked += () => Application.OpenURL(SupportMatrixUrl);
        }
    }
}
