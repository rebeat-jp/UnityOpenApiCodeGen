using System;
using System.Text;

using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    using SpecArrayNode = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecArrayNode;
    using SpecBooleanNode = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecBooleanNode;
    using SpecNode = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecNode;
    using SpecNumberNode = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecNumberNode;
    using SpecObjectNode = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecObjectNode;
    using SpecProperty = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecProperty;
    using SpecStringNode = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecStringNode;

    /// <summary>
    /// A plain scalar in block context may continue on more indented lines (YAML 1.2 7.3.3); the lines are folded.
    /// PyYAML writes long descriptions in this form.
    /// </summary>
    public sealed class YamlPlainMultiLineTests
    {
        private const string SourcePath = "Assets/Specs/openapi.yaml";

        [Fact]
        public void ReadsATwoLineMappingValue()
        {
            SpecNode root = YamlParser.Parse("a: one\n  two\nb: 1\n", SourcePath);

            Assert.Equal("{\"a\":\"one two\",\"b\":1}", Render(root));
        }

        [Fact]
        public void ReadsAThreeLineMappingValue()
        {
            SpecNode root = YamlParser.Parse("a: one\n  two\n  three\n", SourcePath);

            Assert.Equal("{\"a\":\"one two three\"}", Render(root));
        }

        [Fact]
        public void ReadsAJapaneseDescriptionWrappedByAWriter()
        {
            SpecNode root = YamlParser.Parse("paths:\n  /x:\n    get:\n      description: 一段目の説明は長いので折り返される。上限を超えると競合\n        (409)、不足は別のエラーになる。\n      summary: 取得\n", SourcePath);

            Assert.Equal("{\"paths\":{\"/x\":{\"get\":{\"description\":\"一段目の説明は長いので折り返される。上限を超えると競合 (409)、不足は別のエラーになる。\",\"summary\":\"取得\"}}}}", Render(root));
        }

        [Fact]
        public void ReadsAnEmptyLineAsALineFeed()
        {
            SpecNode root = YamlParser.Parse("a: one\n\n  two\nb: 1\n", SourcePath);

            Assert.Equal("{\"a\":\"one\\ntwo\",\"b\":1}", Render(root));
        }

        [Fact]
        public void ReadsTwoEmptyLinesAsTwoLineFeeds()
        {
            SpecNode root = YamlParser.Parse("a: one\n\n\n  two\n", SourcePath);

            Assert.Equal("{\"a\":\"one\\n\\ntwo\"}", Render(root));
        }

        [Fact]
        public void ReadsAWrappedSequenceItem()
        {
            SpecNode root = YamlParser.Parse("- one\n  two\n- three\n", SourcePath);

            Assert.Equal("[\"one two\",\"three\"]", Render(root));
        }

        [Fact]
        public void ReadsAWrappedCompactMappingValue()
        {
            SpecNode root = YamlParser.Parse("- k: one\n    two\n  j: 1\n- k: x\n", SourcePath);

            Assert.Equal("[{\"k\":\"one two\",\"j\":1},{\"k\":\"x\"}]", Render(root));
        }

        [Fact]
        public void ReadsAWrappedValueFollowedByItsSiblingKeyAtTheParentLevel()
        {
            SpecNode root = YamlParser.Parse("a:\n  b: one\n    two\nc: 1\n", SourcePath);

            Assert.Equal("{\"a\":{\"b\":\"one two\"},\"c\":1}", Render(root));
        }

        [Fact]
        public void StopsAtACommentLineAfterTheWrappedValue()
        {
            SpecNode root = YamlParser.Parse("a: one\n  two\n# note\nb: 1\n", SourcePath);

            Assert.Equal("{\"a\":\"one two\",\"b\":1}", Render(root));
        }

        [Fact]
        public void StopsAtATrailingCommentOfTheLastLine()
        {
            SpecNode root = YamlParser.Parse("a: one\n  two # note\nb: 1\n", SourcePath);

            Assert.Equal("{\"a\":\"one two\",\"b\":1}", Render(root));
        }

        [Fact]
        public void TrimsTheWhitespaceAroundEveryLine()
        {
            SpecNode root = YamlParser.Parse("a: one  \n    two	 \n  three  \nb: 1\n", SourcePath);

            Assert.Equal("{\"a\":\"one two three\",\"b\":1}", Render(root));
        }

        [Fact]
        public void ReadsAWrappedValueWithCrLfLineBreaks()
        {
            SpecNode root = YamlParser.Parse("a: one\r\n  two\r\n\r\n  three\r\nb: 1\r\n", SourcePath);

            Assert.Equal("{\"a\":\"one two\\nthree\",\"b\":1}", Render(root));
        }

        [Fact]
        public void ReadsAWrappedValueWithAColonThatIsNotAMappingIndicator()
        {
            SpecNode root = YamlParser.Parse("a: see\n  http://x.example/y\n", SourcePath);

            Assert.Equal("{\"a\":\"see http://x.example/y\"}", Render(root));
        }

        [Fact]
        public void ReadsAWrappedValueAfterAnAnchor()
        {
            SpecNode root = YamlParser.Parse("a: &x one\n  two\nb: *x\n", SourcePath);

            Assert.Equal("{\"a\":\"one two\",\"b\":\"one two\"}", Render(root));
        }

        [Fact]
        public void ReadsWrappedValuesInsideIndentlessSequences()
        {
            SpecNode root = YamlParser.Parse("list:\n- one\n  two\n- k: x\n    y\n  j: z\n    w\nnext: 1\n", SourcePath);

            Assert.Equal("{\"list\":[\"one two\",{\"k\":\"x y\",\"j\":\"z w\"}],\"next\":1}", Render(root));
        }

        [Fact]
        public void ReadsAWrappedValueThatLooksLikeAScalarOfAnotherType()
        {
            SpecNode root = YamlParser.Parse("a: true\n  false\nb: 1\n  2\nc: null\n  x\n", SourcePath);

            Assert.Equal("{\"a\":\"true false\",\"b\":\"1 2\",\"c\":\"null x\"}", Render(root));
        }

        [Fact]
        public void KeepsTheTypeResolutionOfSingleLinePlainScalars()
        {
            SpecNode root = YamlParser.Parse("a: true\nb: 1\nc: null\nd: 1.5\ne: text\nf: false\n", SourcePath);

            Assert.Equal("{\"a\":true,\"b\":1,\"c\":null,\"d\":1.5,\"e\":\"text\",\"f\":false}", Render(root));
        }

        [Fact]
        public void KeepsAFlowSequenceOverSeveralLines()
        {
            SpecNode root = YamlParser.Parse("a: [x,\n  y]\nb: 1\n", SourcePath);

            Assert.Equal("{\"a\":[\"x\",\"y\"],\"b\":1}", Render(root));
        }

        [Fact]
        public void KeepsALineWithTheIndentationOfTheKeyAsAnEntry()
        {
            SpecNode root = YamlParser.Parse("a: one\nb: two\n", SourcePath);

            Assert.Equal("{\"a\":\"one\",\"b\":\"two\"}", Render(root));
        }

        [Fact]
        public void ARowAtTheKeyIndentationIsNotAContinuation()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("a:\n  b: one\n  two\n", SourcePath));

            Assert.Equal("YAML002", exception.DiagnosticId);
        }

        [Fact]
        public void AMappingValueInAContinuationLineIsRejected()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("a: one\n  b: two\n", SourcePath));

            Assert.Equal("YAML004", exception.DiagnosticId);
        }

        [Theory]
        [InlineData("a: &x one\n  b: two\n", "YAML004")]
        [InlineData("- k: &x one\n    b: two\n", "YAML004")]
        [InlineData("- &x one\n  b: two\n", "YAML011")]
        public void AMappingValueAfterAnAnchorIsRejectedLikeWithoutAnAnchor(string source, string expectedCode)
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(() => YamlParser.Parse(source, SourcePath));

            Assert.Equal(expectedCode, exception.DiagnosticId);
        }

        [Fact]
        public void AMultiLinePlainScalarCannotBeAKey()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("- one\n  b: two\n", SourcePath));

            Assert.Equal("YAML011", exception.DiagnosticId);
        }

        [Fact]
        public void ACommentLineEndsThePlainScalar()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("a: one\n  # note\n  two\n", SourcePath));

            Assert.Equal("YAML002", exception.DiagnosticId);
        }

        [Fact]
        public void ACompactMappingContinuationAtTheKeyIndentationIsNotAContinuation()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("- k: one\n  two\n", SourcePath));

            Assert.Equal("YAML002", exception.DiagnosticId);
        }

        [Fact]
        public void TheLocationOfTheFollowingKeyAndOfAMappingValueErrorAreExact()
        {
            SpecObjectNode root = (SpecObjectNode)YamlParser.Parse("a: one\n  two\nb: 1\n", SourcePath);
            Assert.Equal(1, Property(root, "a").Value.Line);
            Assert.Equal(4, Property(root, "a").Value.Column);
            Assert.Equal(3, Property(root, "b").Value.Line);

            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("a: one\n  two: x\n", SourcePath));
            Assert.Equal(2, exception.Line);
            Assert.Equal(6, exception.Column);
            Assert.Equal(12, exception.Offset);
        }

        [Fact]
        public void ARootScalarCannotContinueWithoutAKeyOrADash()
        {
            Assert.Throws<YamlParseException>(() => YamlParser.Parse("one\n  two\n", SourcePath));
        }

        private static SpecObjectNode ParseObject(string source)
        {
            return (SpecObjectNode)YamlParser.Parse(source, SourcePath);
        }

        private static SpecProperty Property(SpecObjectNode node, string name)
        {
            foreach (SpecProperty property in node.Properties)
            {
                if (property.Name == name)
                {
                    return property;
                }
            }

            throw new InvalidOperationException("Missing property: " + name);
        }

        // A compact JSON-like text of the tree (numbers and booleans are bare, strings are quoted).
        private static string Render(SpecNode node)
        {
            var builder = new StringBuilder();
            Render(node, builder);
            return builder.ToString();
        }

        private static void Render(SpecNode node, StringBuilder builder)
        {
            switch (node)
            {
                case SpecObjectNode objectNode:
                    builder.Append('{');
                    for (int index = 0; index < objectNode.Properties.Count; index++)
                    {
                        if (index > 0) builder.Append(',');
                        builder.Append('"').Append(objectNode.Properties[index].Name).Append("\":");
                        Render(objectNode.Properties[index].Value, builder);
                    }

                    builder.Append('}');
                    break;
                case SpecArrayNode arrayNode:
                    builder.Append('[');
                    for (int index = 0; index < arrayNode.Items.Count; index++)
                    {
                        if (index > 0) builder.Append(',');
                        Render(arrayNode.Items[index], builder);
                    }

                    builder.Append(']');
                    break;
                case SpecStringNode stringNode:
                    builder.Append('"').Append(stringNode.Value.Replace("\n", "\\n")).Append('"');
                    break;
                case SpecNumberNode numberNode:
                    builder.Append(numberNode.Value);
                    break;
                case SpecBooleanNode booleanNode:
                    builder.Append(booleanNode.Value ? "true" : "false");
                    break;
                default:
                    builder.Append("null");
                    break;
            }
        }
    }
}
