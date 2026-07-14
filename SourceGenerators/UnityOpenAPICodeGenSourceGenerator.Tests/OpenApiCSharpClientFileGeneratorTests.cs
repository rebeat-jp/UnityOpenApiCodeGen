using Xunit;

namespace ReBeat.OpenApiCodeGen.SourceGenerator.Tests
{
    public class OpenApiCSharpClientFileGeneratorTests
    {
        [Fact]
        public void Generate_EmitsMethodsAndHttpClientConstructors()
        {
            var json = TestAssetLoader.LoadAsset("petstore.json");
            json = json.Replace("\"summary\": \"Finds Pets by status\",", string.Empty);

            var document = JsonApiDocumentParser.Parse(json);
            var option = new ApiClientGenerateOption
            {
                ApiName = "OpenApiClient",
                Namespace = "ReBeat.OpenApiCodeGen.Generated",
                HttpLibraryType = HttpLibrary.HttpClient,
                JsonLibraryType = JsonLibrary.SystemTextJson,
                UseNullableReferenceTypes = true
            };

            var file = new OpenApiCSharpClientFileGenerator().Generate(document, option);
            var content = new RoslynCodeGenerator().Generate(file).Content;

            Assert.Contains("class OpenApiClient", content);
            Assert.Contains("public OpenApiClient()", content);
            Assert.Contains("public OpenApiClient(HttpClient httpClient)", content);
            Assert.Contains("public OpenApiClient(HttpMessageHandler handler)", content);

            Assert.Contains("public async Task addPet(", content);
            Assert.Contains("public async Task updatePet(", content);
            Assert.Contains("public async Task<System.Collections.Generic.IReadOnlyList<Pet>> findPetsByStatus(", content);
            Assert.Contains("/// <summary>findPetsByStatus</summary>", content);
        }

    }
}
