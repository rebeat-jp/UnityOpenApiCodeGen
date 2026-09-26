using System;

using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class YamlParserFoldingTests
    {
        private const string SourcePath = "Assets/Specs/openapi.yaml";

        [Theory]
        [InlineData(">", 1, "first\nsecond\n")]
        [InlineData(">", 2, "first\n\nsecond\n")]
        [InlineData(">-", 1, "first\nsecond")]
        [InlineData(">-", 2, "first\n\nsecond")]
        [InlineData(">+", 1, "first\nsecond\n")]
        [InlineData(">+", 2, "first\n\nsecond\n")]
        public void InteriorEmptyLinesContributeOneFewerLineFeed(
            string header,
            int emptyLineCount,
            string expected)
        {
            SpecObjectNode root = ParseObject(
                "value: " + header + "\n" +
                "  first\n" +
                new string('\n', emptyLineCount) +
                "  second\n" +
                "next: value\n");

            Assert.Equal(expected, StringValue(root, "value"));
            Assert.Equal("value", StringValue(root, "next"));
        }

        [Fact]
        public void OrdinaryFoldingAndMoreIndentedBoundariesRemainDistinct()
        {
            SpecObjectNode root = ParseObject(
                "value: >\n" +
                "  first\n" +
                "  second\n" +
                "\n" +
                "    indented\n" +
                "\n" +
                "  last\n");

            Assert.Equal("first second\n\n  indented\n\nlast\n", StringValue(root, "value"));
        }

        [Fact]
        public void TrailingEmptyLinesRemainForKeepChomping()
        {
            SpecObjectNode root = ParseObject(
                "value: >+\n" +
                "  first\n" +
                "  second\n" +
                "\n" +
                "\n" +
                "next: value\n");

            Assert.Equal("first second\n\n\n", StringValue(root, "value"));
        }

        [Fact]
        public void ExplicitIndentFoldingStopsAtDedentedKey()
        {
            SpecObjectNode root = ParseObject(
                "value: >2-\n" +
                "  first\n" +
                "\n" +
                "  second\n" +
                "next: value\n");

            Assert.Equal("first\nsecond", StringValue(root, "value"));
            Assert.Equal("value", StringValue(root, "next"));
        }

        private static SpecObjectNode ParseObject(string source)
        {
            return (SpecObjectNode)YamlParser.Parse(source, SourcePath);
        }

        private static string StringValue(SpecObjectNode node, string name)
        {
            foreach (Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecProperty property in node.Properties)
            {
                if (property.Name == name)
                {
                    return ((SpecStringNode)property.Value).Value;
                }
            }

            throw new InvalidOperationException("Missing property: " + name);
        }
    }
}
