using System;
using Xunit;

namespace ReBeat.OpenApiCodeGen.SourceGenerator.Tests
{
    public class SwaggerCSharpClientFileGeneratorTests
    {
        [Fact]
        public void Generate_EmitsMethodsAndHttpClientConstructors()
        {
            var json = TestAssetLoader.LoadAsset("swagger2-sample.json");
            json = json.Replace("\"summary\": \"List items\",", string.Empty);
            var insert = "\"/upload\": {\"post\": {\"operationId\": \"uploadItem\", \"consumes\": [\"application/xml\"], \"parameters\": [{\"name\": \"body\", \"in\": \"body\", \"required\": true, \"schema\": {\"type\": \"string\"}}], \"responses\": {\"200\": {\"description\": \"OK\", \"schema\": {\"type\": \"string\"}}}}},";
            var marker = "\"paths\": {";
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                json = json.Insert(index + marker.Length, insert);
            }

            var document = new SwaggerApiDocumentParser().Parse(json);
            var option = new ApiClientGenerateOption
            {
                ApiName = "SwaggerApi",
                Namespace = "ReBeat.OpenApiCodeGen.Generated",
                HttpLibraryType = HttpLibrary.HttpClient,
                JsonLibraryType = JsonLibrary.SystemTextJson,
                UseNullableReferenceTypes = true
            };

            var file = new SwaggerCSharpClientFileGenerator().Generate(document, option);
            var content = new RoslynCodeGenerator().Generate(file).Content;

            Assert.Contains("class SwaggerApi", content);
            Assert.Contains("public SwaggerApi()", content);
            Assert.Contains("public SwaggerApi(HttpClient httpClient)", content);
            Assert.Contains("public SwaggerApi(HttpMessageHandler handler)", content);

            Assert.Contains("public async Task<System.Collections.Generic.IReadOnlyList<Item>> listItems(int? limit)", content);
            Assert.Contains("/// <summary>listItems</summary>", content);

            Assert.Contains("public async Task<string> uploadItem(string body)", content);
            Assert.Contains("new StringContent(json, Encoding.UTF8, \"application/xml\")", content);
        }

    }
}
