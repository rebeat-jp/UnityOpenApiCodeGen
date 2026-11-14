using System;
using System.IO;

using Rhycol.OpenApiCodeGen.Editor.Generation;

using UnityEditor;

using UnityEngine;

namespace PlayerVerification.Editor
{
    /// <summary>
    /// Generates the four verification clients through the public Source Generator provider route
    /// (Editor Writer, normalized spec bundle, analyzer) and writes one probe next to each client.
    /// </summary>
    public static class PlayerVerificationGenerate
    {
        private const string FixtureRoot = "Assets/PlayerVerification";
        private const string SpecPath = FixtureRoot + "/Specs/player-spec.json";

        private readonly struct Combo
        {
            public Combo(string name, GenerationHttpTransport transport, GenerationJsonSerializer serializer)
            {
                Name = name;
                Transport = transport;
                Serializer = serializer;
            }

            public string Name { get; }
            public GenerationHttpTransport Transport { get; }
            public GenerationJsonSerializer Serializer { get; }
        }

        private static readonly Combo[] Combos =
        {
            new Combo("HttpNewtonsoft", GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson),
            new Combo("HttpStj", GenerationHttpTransport.HttpClient, GenerationJsonSerializer.SystemTextJson),
            new Combo("UwrNewtonsoft", GenerationHttpTransport.UnityWebRequest, GenerationJsonSerializer.NewtonsoftJson),
            new Combo("UwrStj", GenerationHttpTransport.UnityWebRequest, GenerationJsonSerializer.SystemTextJson),
        };

        // Entry point for -executeMethod.
        public static void Generate()
        {
#if !OPENAPI_CODEGEN_SOURCE_GENERATOR
            throw new InvalidOperationException(
                "OPENAPI_CODEGEN_SOURCE_GENERATOR was not enabled from the selected project setting.");
#else
            GenerationProviderRegistry registry = GenerationProviderRegistry.Shared;
            if (registry.Resolve(GenerateProvider.OpenApi).IsResolved
                && !registry.TryUnregister(GenerateProvider.OpenApi, out string failureReason))
            {
                throw new InvalidOperationException("The Docker provider could not be disabled. " + failureReason);
            }

            GenerationProviderResolution resolution = registry.Resolve(GenerateProvider.SourceGenerator);
            if (!resolution.IsResolved || resolution.Provider == null)
            {
                throw new InvalidOperationException(
                    "The Source Generator provider could not be resolved. " + resolution.FailureReason);
            }

            string template = File.ReadAllText(
                Path.Combine(Application.dataPath, "PlayerVerification", "Templates~", "ClientProbe.cs.txt"));
            foreach (Combo combo in Combos)
            {
                string outputFolder = FixtureRoot + "/Clients/" + combo.Name + "/Generated";
                var request = new GenerationRequest(SpecPath, outputFolder, combo.Name + "Api", "PlayerVerification." + combo.Name)
                {
                    HttpTransport = combo.Transport,
                    JsonSerializer = combo.Serializer,
                };
                GenerationResult result = resolution.Provider.Generate(request);
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(
                        "Generation failed for " + combo.Name + ": " + result.Message);
                }

                string probe = template
                    .Replace("__COMBO__", combo.Name)
                    .Replace("__TRANSPORT__", combo.Transport.ToString())
                    .Replace("__SERIALIZER__", combo.Serializer.ToString())
                    .Replace(
                        "__CREATE_CLIENT__",
                        combo.Transport == GenerationHttpTransport.HttpClient
                            ? "return new " + combo.Name + "Api(new System.Net.Http.HttpClient { BaseAddress = origin });"
                            : "return new " + combo.Name + "Api(origin);");
                File.WriteAllText(
                    Path.Combine(Application.dataPath, "PlayerVerification", "Clients", combo.Name, "Probe.cs"),
                    probe);
                Debug.Log("PlayerVerification generated " + combo.Name + ": " + result.Message);
            }

            PlayerVerificationBuild.EnsureScene();
            AssetDatabase.Refresh();
#endif
        }
    }
}
