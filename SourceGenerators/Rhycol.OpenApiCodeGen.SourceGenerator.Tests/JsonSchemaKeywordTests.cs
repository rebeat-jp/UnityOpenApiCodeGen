using System;
using System.IO;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public class JsonSchemaKeywordTests
    {
        [Fact]
        public void ParseJsonSchemaKeywords()
        {
            var json = File.ReadAllText(GetAssetPath("schema-sample.json"));

            var document = OpenApiDocumentMapper.Parse(TestBundleFactory.ReadRoot(json));

            Assert.Equal("3.1.0", document.OpenApi);
            Assert.True(document.Components.Schemas.TryGetValue("BooleanTrue", out var booleanTrue));
            Assert.True(document.Components.Schemas.TryGetValue("BooleanFalse", out var booleanFalse));
            Assert.True(booleanTrue.BooleanSchema);
            Assert.False(booleanFalse.BooleanSchema);

            Assert.True(document.Components.Schemas.TryGetValue("Composite", out var composite));
            Assert.Equal("https://json-schema.org/draft/2020-12/schema", composite.Schema);
            Assert.Equal("https://example.com/schema", composite.Id);
            Assert.Equal("root", composite.Anchor);
            Assert.Equal("#meta", composite.DynamicRef);
            Assert.Equal("meta", composite.DynamicAnchor);
            Assert.Equal("note", composite.Comment);

            Assert.Equal("Composite", composite.Title);
            Assert.Equal("Test schema", composite.Description);
            Assert.Contains("object", composite.Types);
            Assert.Equal("object", composite.Type);
            Assert.Equal("custom", composite.Format);

            Assert.NotNull(composite.Const);
            Assert.Equal(3, composite.EnumValues.Count);
            Assert.NotNull(composite.Default);
            Assert.Equal(2, composite.Examples.Count);

            Assert.Equal(2, composite.MultipleOf);
            Assert.Equal(10, composite.Maximum);
            Assert.Equal(11, composite.ExclusiveMaximum);
            Assert.Equal(1, composite.Minimum);
            Assert.Equal(0, composite.ExclusiveMinimum);
            Assert.Equal(5, composite.MaxLength);
            Assert.Equal(1, composite.MinLength);
            Assert.Equal("^a+$", composite.Pattern);
            Assert.Equal(5, composite.MaxItems);
            Assert.Equal(1, composite.MinItems);
            Assert.True(composite.UniqueItems);
            Assert.Equal(2, composite.MaxContains);
            Assert.Equal(1, composite.MinContains);
            Assert.Equal(5, composite.MaxProperties);
            Assert.Equal(1, composite.MinProperties);

            Assert.True(composite.Properties.ContainsKey("flag"));
            Assert.True(composite.PatternProperties.ContainsKey("^S_"));
            Assert.NotNull(composite.AdditionalPropertiesSchema);
            Assert.Equal("number", composite.AdditionalPropertiesSchema!.Type);

            Assert.Equal(2, composite.PrefixItems.Count);
            Assert.False(composite.ItemsAllowed);

            Assert.NotNull(composite.Contains);
            Assert.False(composite.UnevaluatedItemsAllowed);
            Assert.NotNull(composite.UnevaluatedProperties);
            Assert.Equal("boolean", composite.UnevaluatedProperties!.Type);
            Assert.NotNull(composite.PropertyNames);

            Assert.True(composite.DependentRequired.ContainsKey("credit_card"));
            Assert.True(composite.DependentSchemas.ContainsKey("details"));
            Assert.NotNull(composite.If);
            Assert.NotNull(composite.Then);
            Assert.NotNull(composite.Else);

            Assert.Equal(2, composite.AllOf.Count);
            Assert.Equal(2, composite.AnyOf.Count);
            Assert.Equal(2, composite.OneOf.Count);
            Assert.NotNull(composite.Not);

            Assert.True(composite.Defs.ContainsKey("Inner"));
            Assert.True(composite.Defs.ContainsKey("Always"));
            Assert.True(composite.Defs["Always"].BooleanSchema);

            Assert.True(composite.Vocabulary.ContainsKey("https://json-schema.org/draft/2020-12/vocab/core"));
            Assert.Equal("base64", composite.ContentEncoding);
            Assert.Equal("application/json", composite.ContentMediaType);
            Assert.NotNull(composite.ContentSchema);
        }

        private static string GetAssetPath(string fileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "TestAssets", fileName);
        }
    }
}
