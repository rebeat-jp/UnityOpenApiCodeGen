using System;
using System.IO;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public class OpenApiPetstoreTests
    {
        [Fact]
        public void ParsePetstoreJson()
        {
            var json = File.ReadAllText(GetAssetPath("petstore.json"));

            var document = OpenApiDocumentMapper.Parse(TestBundleFactory.ReadRoot(json));

            Assert.Equal("3.0.0", document.OpenApi);
            Assert.Equal("OpenAPI Petstore", document.Info.Title);
            Assert.Equal("1.0.0", document.Info.Version);

            Assert.True(document.Paths.TryGetValue("/pet", out var petPath));
            Assert.True(petPath.Operations.TryGetValue("post", out var postOperation));
            Assert.Equal("addPet", postOperation.OperationId);
            Assert.Equal("Add a new pet to the store", postOperation.Summary);
            Assert.True(postOperation.Responses.TryGetValue("405", out var response405));
            Assert.Equal("Invalid input", response405.Description);

            Assert.True(document.Components.Schemas.TryGetValue("Pet", out var petSchema));
            Assert.Contains("name", petSchema.Required);
            Assert.Contains("photoUrls", petSchema.Required);

            Assert.True(petSchema.Properties.TryGetValue("photoUrls", out var photoUrlsSchema));
            Assert.Equal("array", photoUrlsSchema.Type);
            Assert.NotNull(photoUrlsSchema.Items);
            Assert.Equal("string", photoUrlsSchema.Items!.Type);

            Assert.True(petSchema.Properties.TryGetValue("status", out var statusSchema));
            Assert.Equal(3, statusSchema.EnumValues.Count);
        }

        private static string GetAssetPath(string fileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "TestAssets", fileName);
        }
    }
}
