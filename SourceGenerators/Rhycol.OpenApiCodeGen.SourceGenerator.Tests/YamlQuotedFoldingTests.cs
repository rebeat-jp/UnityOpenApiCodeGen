using System;
using System.Collections.Generic;

using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    using SpecArrayNode = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecArrayNode;
    using SpecNode = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecNode;
    using SpecObjectNode = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecObjectNode;
    using SpecProperty = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecProperty;
    using SpecStringNode = Rhycol.OpenApiCodeGen.SourceGenerator.Editor.SpecStringNode;

    /// <summary>
    /// Quoted YAML scalars that continue over several lines (YAML 1.2 7.3): line folding, positions and the
    /// restrictions of the supported subset.
    /// </summary>
    public sealed class YamlQuotedFoldingTests
    {
        private const string SourcePath = "Assets/Specs/openapi.yaml";

        [Fact]
        public void IssueReproductionReadsAMultiLineSingleQuotedDescription()
        {
            SpecObjectNode root = ParseObject(
                "info:\n" +
                "  title: Example\n" +
                "  version: 1.0.0\n" +
                "  description: 'first line\n" +
                "    second line'\n");

            SpecObjectNode info = (SpecObjectNode)Property(root, "info").Value;
            Assert.Equal("first line second line", StringValue(info, "description"));
            Assert.Equal("1.0.0", StringValue(info, "version"));
        }

        [Fact]
        public void DoubleQuotedFoldingFollowsYamlExample75()
        {
            SpecObjectNode root = ParseObject(
                "v: \"folded \n" +
                "  to a space,\t\n" +
                " \n" +
                "  to a line feed, or \t\\\n" +
                "  \\ \tnon-content\"\n");

            Assert.Equal("folded to a space,\nto a line feed, or \t \tnon-content", StringValue(root, "v"));
        }

        [Fact]
        public void DoubleQuotedFoldingFollowsYamlExample75AsARootScalar()
        {
            SpecNode root = YamlParser.Parse(
                "\"folded \nto a space,\t\n \nto a line feed, or \t\\\n \\ \tnon-content\"\n",
                SourcePath);

            Assert.Equal("folded to a space,\nto a line feed, or \t \tnon-content", ((SpecStringNode)root).Value);
        }

        [Theory]
        [InlineData("v: ' 1st non-empty\n\n  2nd non-empty \n  \t3rd non-empty '\n")]
        [InlineData("v: ' 1st non-empty\r\n\r\n  2nd non-empty \r\n  \t3rd non-empty '\r\n")]
        [InlineData("v: ' 1st non-empty\r\r  2nd non-empty \r  \t3rd non-empty '\r")]
        public void SingleQuotedFoldingFollowsYamlExamples76And79(string source)
        {
            Assert.Equal(" 1st non-empty\n2nd non-empty 3rd non-empty ", StringValue(ParseObject(source), "v"));
        }

        [Fact]
        public void SingleQuotedFoldingFollowsYamlExample79AsARootScalar()
        {
            SpecNode root = YamlParser.Parse("' 1st non-empty\n\n 2nd non-empty \n 3rd non-empty '\n", SourcePath);

            Assert.Equal(" 1st non-empty\n2nd non-empty 3rd non-empty ", ((SpecStringNode)root).Value);
        }

        [Theory]
        [InlineData("v: 'it''s\n  ok'\n", "it's ok")]
        [InlineData("v: 'a''\n  b'\n", "a' b")]
        [InlineData("v: ''\n", "")]
        [InlineData("v: 'a\n\n\n  b'\n", "a\n\nb")]
        [InlineData("v: 'a  \n   b'\n", "a b")]
        [InlineData("v: 'a\n  '\n", "a ")]
        [InlineData("v: \"a\\\n  b\"\n", "ab")]
        [InlineData("v: \"a\\\\\n  b\"\n", "a\\ b")]
        [InlineData("v: \"a\\t\n  b\"\n", "a\t b")]
        [InlineData("v: \"a\\ \n  b\"\n", "a  b")]
        [InlineData("v: \"a\\\n\n  b\"\n", "a\nb")]
        [InlineData("v: \"a \\\n  b\"\n", "a b")]
        public void QuotedScalarsFoldLineBreaks(string source, string expected)
        {
            Assert.Equal(expected, StringValue(ParseObject(source), "v"));
        }

        [Fact]
        public void CrLfLineBreaksAreFoldedAndLaterNodesKeepTheirLines()
        {
            SpecObjectNode root = ParseObject("a: 'x\r\n  y'\r\nb: 1\r\nc: \"p\r\n  q\"\r\nd: 2\r\n");

            Assert.Equal("x y", StringValue(root, "a"));
            Assert.Equal("p q", StringValue(root, "c"));
            Assert.Equal(3, Property(root, "b").Line);
            Assert.Equal(4, Property(root, "c").Line);
            Assert.Equal(6, Property(root, "d").Line);
        }

        [Fact]
        public void NodeLocationsAfterAMultiLineScalarAreCorrect()
        {
            SpecObjectNode root = ParseObject("a: 'x\n  y'\nb: 1\n");

            Assert.Equal(1, Property(root, "a").Line);
            Assert.Equal(1, Property(root, "a").Value.Line);
            Assert.Equal(4, Property(root, "a").Value.Column);
            Assert.Equal(3, Property(root, "b").Line);
            Assert.Equal(1, Property(root, "b").Column);
        }

        [Fact]
        public void QuotedScalarsMayContinueInsideFlowCollectionsAndSequences()
        {
            SpecObjectNode root = ParseObject(
                "flow: [ 'a\nb', \"c\n  d\" ]\n" +
                "seq:\n" +
                "  - 'e\n    f'\n" +
                "  - g\n" +
                "map: { k: 'h\n  i' }\n");

            SpecArrayNode flow = (SpecArrayNode)Property(root, "flow").Value;
            Assert.Equal("a b", ((SpecStringNode)flow.Items[0]).Value);
            Assert.Equal("c d", ((SpecStringNode)flow.Items[1]).Value);
            SpecArrayNode seq = (SpecArrayNode)Property(root, "seq").Value;
            Assert.Equal("e f", ((SpecStringNode)seq.Items[0]).Value);
            Assert.Equal("g", ((SpecStringNode)seq.Items[1]).Value);
            Assert.Equal("h i", StringValue((SpecObjectNode)Property(root, "map").Value, "k"));
        }

        [Fact]
        public void AQuoteThatStartsTheLineMayContinueAtTheSameIndent()
        {
            SpecObjectNode root = ParseObject("v:\n  'a\n  b'\n");

            Assert.Equal("a b", StringValue(root, "v"));
        }

        [Fact]
        public void AnUnterminatedQuoteIsReportedAtItsStart()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(() => YamlParser.Parse("v: 'a\n  b\n", SourcePath));

            Assert.Equal("YAML006", exception.DiagnosticId);
            Assert.Equal(1, exception.Line);
            Assert.Equal(4, exception.Column);
            Assert.Equal(3, exception.Offset);

            exception = Assert.Throws<YamlParseException>(() => YamlParser.Parse("v: \"5\n", SourcePath));
            Assert.Equal("YAML006", exception.DiagnosticId);
            Assert.Equal(1, exception.Line);
        }

        [Fact]
        public void AMissingClosingQuoteBeforeTheNextKeyIsAnIndentationErrorOnThatLine()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("a:\n  size: \"5\n  next: 1\n", SourcePath));

            Assert.Equal("YAML002", exception.DiagnosticId);
            Assert.Equal(3, exception.Line);
            // The message names the line where the quote was opened, which is where the fix belongs.
            Assert.Contains("the quoted YAML scalar that starts on line 2", exception.Message);
        }

        [Fact]
        public void ADocumentMarkerInsideAQuotedScalarNamesTheLineTheQuoteStartedOn()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("a: 1\nv: 'x\n---\n  y'\n", SourcePath));

            Assert.Equal("YAML006", exception.DiagnosticId);
            Assert.Equal(3, exception.Line);
            Assert.Contains("the quoted YAML scalar that starts on line 2", exception.Message);
        }

        [Fact]
        public void APlainScalarWithAQuoteInTheMiddleStillParses()
        {
            SpecObjectNode root = ParseObject("size: 5\"\nnext: it's\n");

            Assert.Equal("5\"", StringValue(root, "size"));
            Assert.Equal("it's", StringValue(root, "next"));
        }

        [Theory]
        [InlineData("v: 'a\n---\n  b'\n")]
        [InlineData("v: 'a\n...\n  b'\n")]
        [InlineData("v: \"a\n--- \n  b\"\n")]
        public void ADocumentMarkerInsideAQuotedScalarIsRejected(string source)
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(() => YamlParser.Parse(source, SourcePath));

            Assert.Equal("YAML006", exception.DiagnosticId);
            Assert.Equal(2, exception.Line);
            Assert.Equal(1, exception.Column);
        }

        [Theory]
        [InlineData("v: 'a\nb'\n")]
        [InlineData("m:\n  k: 'a\n  b'\n")]
        [InlineData("- 'a\nb'\n")]
        public void AContinuationLineThatIsNotIndentedDeeperIsRejected(string source)
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(() => YamlParser.Parse(source, SourcePath));

            Assert.Equal("YAML002", exception.DiagnosticId);
        }

        [Fact]
        public void BlankContinuationLinesAreNotCheckedForIndentation()
        {
            Assert.Equal("a\nb", StringValue(ParseObject("v: 'a\n\n  b'\n"), "v"));
            Assert.Equal("a\nb", StringValue(ParseObject("v: 'a\n \n  b'\n"), "v"));
        }

        [Theory]
        [InlineData("v: 'a\n  b' title: X\n")]
        [InlineData("v: 'a\n  b' # comment\nw: 'c\n  d' x\n")]
        [InlineData("v: \"a\n  b\" \"c\"\n")]
        [InlineData("- 'a\n  b' title\n")]
        public void ContentAfterTheClosingQuoteOfAMultiLineScalarIsRejected(string source)
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(() => YamlParser.Parse(source, SourcePath));

            Assert.Equal("YAML006", exception.DiagnosticId);
            Assert.True(exception.Line >= 2, exception.Message);
        }

        [Fact]
        public void ACommentAfterTheClosingQuoteIsAllowed()
        {
            SpecObjectNode root = ParseObject("v: 'a\n  b' # comment\nw: 1\n");

            Assert.Equal("a b", StringValue(root, "v"));
            Assert.Equal(3, Property(root, "w").Line);
        }

        [Theory]
        [InlineData("'a\n  b': 1\n")]
        [InlineData("\"a\n  b\": 1\n")]
        [InlineData("- 'a\n  b': 1\n")]
        [InlineData("v: {'a\n  b': 1}\n")]
        public void AKeyThatSpansLinesIsRejected(string source)
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(() => YamlParser.Parse(source, SourcePath));

            Assert.Equal("YAML011", exception.DiagnosticId);
        }

        [Fact]
        public void ErrorsInsideAMultiLineTokenPointAtTheRightLine()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("v: \"a\n  \\xZZ\"\n", SourcePath));

            Assert.Equal("YAML006", exception.DiagnosticId);
            Assert.Equal(2, exception.Line);
            Assert.Equal(3, exception.Column);
            Assert.Equal(8, exception.Offset);
        }

        [Fact]
        public void TheMultiLineTokenSpansTheWholeQuotedTextAndTokensStayMonotonic()
        {
            const string source = "a: 'x\r\n  y'\r\nb: \"p\n  q\" # c\nc:\n  - 'r\n    s'\n";
            IReadOnlyList<YamlToken> tokens = YamlLexer.Tokenize(source, SourcePath);

            YamlToken first = tokens[2];
            Assert.Equal(YamlTokenKind.Scalar, first.Kind);
            Assert.Equal("'x\r\n  y'", first.Value);
            Assert.Equal(1, first.Line);
            Assert.Equal(4, first.Column);
            Assert.Equal(3, first.Offset);
            Assert.Equal(first.Value.Length, first.Length);

            int previousEnd = -1;
            foreach (YamlToken token in tokens)
            {
                Assert.True(token.Offset >= previousEnd, token.ToString());
                previousEnd = token.Offset + token.Length;
            }

            YamlToken newLine = tokens[3];
            Assert.Equal(YamlTokenKind.NewLine, newLine.Kind);
            Assert.Equal(2, newLine.Line);
            Assert.Equal(5, newLine.Column);
            Assert.Equal(source.Length, tokens[tokens.Count - 1].Offset);
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

        private static string StringValue(SpecObjectNode node, string name)
        {
            return ((SpecStringNode)Property(node, name).Value).Value;
        }
    }
}
