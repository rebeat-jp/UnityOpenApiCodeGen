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
    /// A block sequence that is the value of a mapping key may be written at the indentation of the key
    /// (an "indentless" sequence, YAML 1.2 8.2.3). Readers and writers such as PyYAML use this form by default.
    /// </summary>
    public sealed class YamlIndentlessSequenceTests
    {
        private const string SourcePath = "Assets/Specs/openapi.yaml";

        [Fact]
        public void IssueReproductionReadsASequenceAtTheIndentationOfItsKey()
        {
            SpecNode root = YamlParser.Parse(
                "openapi: 3.0.4\n" +
                "info:\n" +
                "  title: Example\n" +
                "  version: 1.0.0\n" +
                "x-example:\n" +
                "  items:\n" +
                "  - a\n" +
                "  - b\n" +
                "paths: {}\n",
                SourcePath);

            Assert.Equal(
                "{\"openapi\":\"3.0.4\",\"info\":{\"title\":\"Example\",\"version\":\"1.0.0\"}," +
                "\"x-example\":{\"items\":[\"a\",\"b\"]},\"paths\":{}}",
                Render(root));
        }

        [Fact]
        public void ARootKeyReadsItsSequenceAtColumnOne()
        {
            SpecNode root = YamlParser.Parse("tags:\n- a\n- b\nnext: 1\n", SourcePath);

            Assert.Equal("{\"tags\":[\"a\",\"b\"],\"next\":1}", Render(root));
        }

        [Fact]
        public void ARootKeyReadsMappingItemsAndTheKeyThatFollows()
        {
            SpecObjectNode root = ParseObject(
                "tags:\n" +
                "- name: x\n" +
                "  description: y\n" +
                "- name: z\n" +
                "paths: {}\n");

            Assert.Equal(
                "{\"tags\":[{\"name\":\"x\",\"description\":\"y\"},{\"name\":\"z\"}],\"paths\":{}}",
                Render(root));
            SpecArrayNode tags = (SpecArrayNode)Property(root, "tags").Value;
            Assert.Equal(2, tags.Line);
            Assert.Equal(1, tags.Column);
            Assert.Equal(2, tags.Items[0].Line);
            Assert.Equal(3, tags.Items[0].Column);
            Assert.Equal(3, Property((SpecObjectNode)tags.Items[0], "description").Line);
        }

        [Fact]
        public void ANestedSequenceIsFollowedByItsSiblingKeysAndByTheParentSiblings()
        {
            SpecNode root = YamlParser.Parse(
                "a:\n" +
                "  b:\n" +
                "  - 1\n" +
                "  - 2\n" +
                "  c: 3\n" +
                "  d:\n" +
                "  - x\n" +
                "e: 4\n",
                SourcePath);

            Assert.Equal("{\"a\":{\"b\":[1,2],\"c\":3,\"d\":[\"x\"]},\"e\":4}", Render(root));
        }

        [Fact]
        public void ACompactMappingItemMayHoldAnIndentlessSequence()
        {
            SpecNode root = YamlParser.Parse(
                "list:\n" +
                "- name: x\n" +
                "  tags:\n" +
                "  - a\n" +
                "  - b\n" +
                "  description: y\n" +
                "- name: z\n" +
                "  tags:\n" +
                "  - c\n" +
                "other: 1\n",
                SourcePath);

            Assert.Equal(
                "{\"list\":[{\"name\":\"x\",\"tags\":[\"a\",\"b\"],\"description\":\"y\"}," +
                "{\"name\":\"z\",\"tags\":[\"c\"]}],\"other\":1}",
                Render(root));
        }

        [Fact]
        public void ARootSequenceOfCompactMappingsMayHoldAnIndentlessSequence()
        {
            SpecNode root = YamlParser.Parse(
                "- name: x\n" +
                "  tags:\n" +
                "  - a\n" +
                "- name: z\n",
                SourcePath);

            Assert.Equal("[{\"name\":\"x\",\"tags\":[\"a\"]},{\"name\":\"z\"}]", Render(root));
        }

        [Fact]
        public void AnIndentlessSequenceMayHoldIndentlessSequences()
        {
            SpecNode root = YamlParser.Parse(
                "a:\n" +
                "- b:\n" +
                "  - 1\n" +
                "  - 2\n" +
                "  c:\n" +
                "  - 3\n" +
                "- d: 4\n" +
                "z: 5\n",
                SourcePath);

            Assert.Equal("{\"a\":[{\"b\":[1,2],\"c\":[3]},{\"d\":4}],\"z\":5}", Render(root));
        }

        [Fact]
        public void CommentsBetweenTheKeyAndTheItemsAreSkipped()
        {
            SpecNode root = YamlParser.Parse(
                "a:\n" +
                "# before the items\n" +
                "- x # trailing\n" +
                "# between the items\n" +
                "\n" +
                "- y\n" +
                "b: 1\n",
                SourcePath);

            Assert.Equal("{\"a\":[\"x\",\"y\"],\"b\":1}", Render(root));
        }

        [Fact]
        public void ABlockScalarItemKeepsItsContentIndentation()
        {
            SpecNode root = YamlParser.Parse(
                "a:\n" +
                "- |\n" +
                "  text\n" +
                "  more\n" +
                "- b\n" +
                "c: 1\n",
                SourcePath);

            Assert.Equal("{\"a\":[\"text\\nmore\\n\",\"b\"],\"c\":1}", Render(root));
        }

        [Fact]
        public void AnAnchorOnAKeyMayBeFollowedByAnIndentlessSequence()
        {
            SpecNode root = YamlParser.Parse(
                "a: &list\n" +
                "- 1\n" +
                "- 2\n" +
                "b: *list\n",
                SourcePath);

            Assert.Equal("{\"a\":[1,2],\"b\":[1,2]}", Render(root));
        }

        [Fact]
        public void AnAnchorOnAKeyInsideACompactMappingMayBeFollowedByAnIndentlessSequence()
        {
            SpecNode root = YamlParser.Parse(
                "- name: &list\n" +
                "  - 1\n" +
                "- other: 2\n",
                SourcePath);

            Assert.Equal("[{\"name\":[1]},{\"other\":2}]", Render(root));
        }

        [Fact]
        public void TheIndentedAndTheIndentlessFormsReadTheSameTree()
        {
            const string indentless =
                "paths:\n" +
                "  /pets:\n" +
                "    get:\n" +
                "      tags:\n" +
                "      - pets\n" +
                "      parameters:\n" +
                "      - name: limit\n" +
                "        in: query\n" +
                "        schema:\n" +
                "          enum:\n" +
                "          - 1\n" +
                "          - 2\n" +
                "      security:\n" +
                "      - bearer: []\n";
            const string indented =
                "paths:\n" +
                "  /pets:\n" +
                "    get:\n" +
                "      tags:\n" +
                "        - pets\n" +
                "      parameters:\n" +
                "        - name: limit\n" +
                "          in: query\n" +
                "          schema:\n" +
                "            enum:\n" +
                "              - 1\n" +
                "              - 2\n" +
                "      security:\n" +
                "        - bearer: []\n";

            Assert.Equal(Render(YamlParser.Parse(indented, SourcePath)), Render(YamlParser.Parse(indentless, SourcePath)));
        }

        [Fact]
        public void AKeyWithoutAValueStillReadsNullWhenTheNextLineIsAKey()
        {
            SpecNode root = YamlParser.Parse("a:\nb: 1\n", SourcePath);

            Assert.Equal("{\"a\":null,\"b\":1}", Render(root));
        }

        [Theory]
        [InlineData("a: 1\n- x\n", "YAML003")]
        [InlineData("p:\n  a:\n- x\n", "YAML003")]
        [InlineData("a:\n- x\nb: 1\n- y\n", "YAML003")]
        [InlineData("p:\n  a: 1\n  - x\n", "YAML002")]
        [InlineData("p:\n  a:\n  - x\n  b: 1\n  - y\n", "YAML002")]
        [InlineData("p:\n  a:\n  - x\n    b: 1\n", "YAML011")]
        [InlineData("a:\n- &x\n- 1\n", "YAML004")]
        [InlineData("a:\n  - &x\n  - 1\n", "YAML004")]
        [InlineData("- &x\n- 1\n", "YAML004")]
        public void InvalidMixesOfKeysAndDashesAreStillRejected(string source, string expectedCode)
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(() => YamlParser.Parse(source, SourcePath));

            Assert.Equal(expectedCode, exception.DiagnosticId);
            Assert.True(exception.Line >= 1);
            Assert.True(exception.Column >= 1);
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
