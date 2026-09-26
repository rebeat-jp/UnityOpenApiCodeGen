using System;
using System.IO;
using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public class SwaggerDocumentMapperTests
    {
        [Fact]
        public void ParseSwagger2Document()
        {
            var json = File.ReadAllText(GetAssetPath("swagger2-sample.json"));
            var parser = new SwaggerDocumentMapper();

            var document = parser.Parse(TestBundleFactory.ReadRoot(json));

            Assert.Equal("2.0", document.Swagger);
            Assert.Equal("Swagger Sample", document.Info.Title);
            Assert.Equal("1.0.0", document.Info.Version);
            Assert.Equal("Sample swagger 2.0", document.Info.Description);
            Assert.Equal("api.example.com", document.Host);
            Assert.Equal("/v1", document.BasePath);
            Assert.Contains("https", document.Schemes);

            Assert.True(document.Paths.TryGetValue("/items", out var itemsPath));
            Assert.True(itemsPath.Operations.TryGetValue("get", out var getOperation));
            Assert.Equal("listItems", getOperation.OperationId);
            Assert.Equal("List items", getOperation.Summary);
            Assert.Contains("items", getOperation.Tags);
            Assert.Single(getOperation.Parameters);
            Assert.Equal("limit", getOperation.Parameters[0].Name);

            Assert.True(getOperation.Responses.TryGetValue("200", out var response200));
            Assert.Equal("OK", response200.Description);
            Assert.NotNull(response200.Schema);
            Assert.Equal("array", response200.Schema!.Type);
            Assert.NotNull(response200.Schema.Items);
            Assert.Equal("#/definitions/Item", response200.Schema.Items!.Ref);
            Assert.True(response200.Headers.ContainsKey("X-Request-Id"));

            Assert.True(document.Definitions.TryGetValue("Item", out var itemSchema));
            Assert.Equal("Item schema", itemSchema.Description);
            Assert.Equal(2, itemSchema.AllOf.Count);

            Assert.True(document.Definitions.TryGetValue("Extended", out var extendedSchema));
            Assert.Equal("http://json-schema.org/draft-04/schema#", extendedSchema.Schema);
            Assert.Equal("https://example.com/extended", extendedSchema.Id);
            Assert.Contains("object", extendedSchema.Types);
            Assert.Contains("null", extendedSchema.Types);
            Assert.Equal("object", extendedSchema.Type);
            Assert.True(extendedSchema.PatternProperties.ContainsKey("^S_"));
            Assert.True(extendedSchema.DependenciesRequired.ContainsKey("code"));
            Assert.True(extendedSchema.DependenciesSchemas.ContainsKey("meta"));
            Assert.False(extendedSchema.AdditionalPropertiesAllowed);
            Assert.Equal(2, extendedSchema.ItemSchemas.Count);
            Assert.NotNull(extendedSchema.AdditionalItemsSchema);
            Assert.Equal("boolean", extendedSchema.AdditionalItemsSchema!.Type);
            Assert.Equal(2, extendedSchema.AnyOf.Count);
            Assert.Equal(2, extendedSchema.OneOf.Count);
            Assert.NotNull(extendedSchema.Not);
            Assert.True(extendedSchema.Definitions.ContainsKey("Local"));
            Assert.Equal(2, extendedSchema.MultipleOf);
            Assert.Equal(10, extendedSchema.Maximum);
            Assert.True(extendedSchema.ExclusiveMaximum);
            Assert.Equal(1, extendedSchema.Minimum);
            Assert.False(extendedSchema.ExclusiveMinimum);
            Assert.Equal(5, extendedSchema.MaxLength);
            Assert.Equal(1, extendedSchema.MinLength);
            Assert.Equal("^a+$", extendedSchema.Pattern);
            Assert.Equal(5, extendedSchema.MaxItems);
            Assert.Equal(1, extendedSchema.MinItems);
            Assert.True(extendedSchema.UniqueItems);
            Assert.Equal(5, extendedSchema.MaxProperties);
            Assert.Equal(1, extendedSchema.MinProperties);
            Assert.True(extendedSchema.ReadOnly);
            Assert.Equal(2, extendedSchema.EnumValues.Count);
            Assert.NotNull(extendedSchema.Default);
            Assert.NotNull(extendedSchema.Example);
        }

        private static string GetAssetPath(string fileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "TestAssets", fileName);
        }
    }
}
