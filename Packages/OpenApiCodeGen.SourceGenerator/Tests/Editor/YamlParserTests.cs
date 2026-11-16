using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class YamlParserTests
    {
        private const string SourcePath = "Assets/Specs/openapi.yaml";

        [Test]
        public void ParseBuildsBlockAndFlowMappingsAndSequencesWithLocations()
        {
            SpecObjectNode root = ParseObject(
                "root:\n" +
                "  child: 😀\n" +
                "  values: [null, true, 1, -2.5e+3]\n" +
                "  nested: {name: sample, enabled: false}\n");

            SpecProperty rootProperty = Property(root, "root");
            SpecObjectNode nested = (SpecObjectNode)rootProperty.Value;
            SpecProperty child = Property(nested, "child");
            SpecStringNode childValue = (SpecStringNode)child.Value;
            SpecArrayNode values = (SpecArrayNode)Property(nested, "values").Value;
            SpecObjectNode flowObject = (SpecObjectNode)Property(nested, "nested").Value;

            Assert.That(root.Line, Is.EqualTo(1));
            Assert.That(root.Column, Is.EqualTo(1));
            Assert.That(rootProperty.Line, Is.EqualTo(1));
            Assert.That(rootProperty.Column, Is.EqualTo(1));
            Assert.That(child.Line, Is.EqualTo(2));
            Assert.That(child.Column, Is.EqualTo(3));
            Assert.That(childValue.Line, Is.EqualTo(2));
            Assert.That(childValue.Column, Is.EqualTo(10));
            Assert.That(values.Items, Has.Count.EqualTo(4));
            Assert.That(values.Items[0], Is.TypeOf<SpecNullNode>());
            Assert.That(((SpecBooleanNode)values.Items[1]).Value, Is.True);
            Assert.That(((SpecNumberNode)values.Items[2]).Value, Is.EqualTo("1"));
            Assert.That(((SpecNumberNode)values.Items[3]).Value, Is.EqualTo("-2.5e+3"));
            Assert.That(((SpecBooleanNode)Property(flowObject, "enabled").Value).Value, Is.False);
        }

        [Test]
        public void ParsePreservesAdditionalBlockScalarIndentation()
        {
            SpecObjectNode root = ParseObject(
                "description: |\n" +
                "    first\n" +
                "      extra\n" +
                "next: value\n");

            string value = ((SpecStringNode)Property(root, "description").Value).Value;

            Assert.That(value, Is.EqualTo("first\n  extra\n"));
        }

        [TestCase(">", 1, "first\nsecond\n")]
        [TestCase(">", 2, "first\n\nsecond\n")]
        [TestCase(">-", 1, "first\nsecond")]
        [TestCase(">-", 2, "first\n\nsecond")]
        [TestCase(">+", 1, "first\nsecond\n")]
        [TestCase(">+", 2, "first\n\nsecond\n")]
        public void ParseFoldsInteriorEmptyLinesOnce(string header, int emptyLineCount, string expected)
        {
            SpecObjectNode root = ParseObject(
                "value: " + header + "\n" +
                "  first\n" +
                new string('\n', emptyLineCount) +
                "  second\n" +
                "next: value\n");

            Assert.That(((SpecStringNode)Property(root, "value").Value).Value, Is.EqualTo(expected));
            Assert.That(((SpecStringNode)Property(root, "next").Value).Value, Is.EqualTo("value"));
        }

        [Test]
        public void ParsePreservesOrdinaryFoldAndMoreIndentedSeparation()
        {
            SpecObjectNode root = ParseObject(
                "value: >\n" +
                "  first\n" +
                "  second\n" +
                "\n" +
                "    indented\n" +
                "\n" +
                "  last\n");

            Assert.That(
                ((SpecStringNode)Property(root, "value").Value).Value,
                Is.EqualTo("first second\n\n  indented\n\nlast\n"));
        }

        [Test]
        public void ParsePreservesTrailingEmptyLinesForKeepChomping()
        {
            SpecObjectNode root = ParseObject(
                "value: >+\n" +
                "  first\n" +
                "  second\n" +
                "\n" +
                "\n" +
                "next: value\n");

            Assert.That(((SpecStringNode)Property(root, "value").Value).Value, Is.EqualTo("first second\n\n\n"));
        }

        [Test]
        public void ParseFoldsExplicitIndentAndStopsAtDedentedKey()
        {
            SpecObjectNode root = ParseObject(
                "value: >2-\n" +
                "  first\n" +
                "\n" +
                "  second\n" +
                "next: value\n");

            Assert.That(((SpecStringNode)Property(root, "value").Value).Value, Is.EqualTo("first\nsecond"));
            Assert.That(((SpecStringNode)Property(root, "next").Value).Value, Is.EqualTo("value"));
        }

        [Test]
        public void ParseSupportsRootAndSequenceItemBlockScalars()
        {
            SpecArrayNode root = (SpecArrayNode)YamlParser.Parse(
                "- |\n" +
                "  first\n" +
                "  second\n" +
                "- |\n" +
                "  third\n",
                SourcePath);

            Assert.That(root.Items, Has.Count.EqualTo(2));
            Assert.That(((SpecStringNode)root.Items[0]).Value, Is.EqualTo("first\nsecond\n"));
            Assert.That(((SpecStringNode)root.Items[1]).Value, Is.EqualTo("third\n"));
        }

        [Test]
        public void ParsePreservesCompactSequenceMappingBlockScalarAndFollowingKey()
        {
            SpecObjectNode root = ParseObject(
                "items:\n" +
                "  - description: |\n" +
                "      first\n" +
                "      second\n" +
                "    content:\n" +
                "      application/json: {}\n");
            SpecArrayNode items = (SpecArrayNode)Property(root, "items").Value;
            SpecObjectNode item = (SpecObjectNode)items.Items[0];

            Assert.That(
                ((SpecStringNode)Property(item, "description").Value).Value,
                Is.EqualTo("first\nsecond\n"));
            SpecObjectNode content = (SpecObjectNode)Property(item, "content").Value;
            SpecObjectNode media = (SpecObjectNode)Property(content, "application/json").Value;
            Assert.That(media.Properties, Is.Empty);
        }

        [Test]
        public void ParsePreservesExplicitCompactBlockScalarIndentAndFollowingKey()
        {
            SpecObjectNode root = ParseObject(
                "items:\n" +
                "  - description: |2\n" +
                "        first\n" +
                "      second\n" +
                "    content:\n" +
                "      application/json: {}\n");
            SpecArrayNode items = (SpecArrayNode)Property(root, "items").Value;
            SpecObjectNode item = (SpecObjectNode)items.Items[0];

            Assert.That(
                ((SpecStringNode)Property(item, "description").Value).Value,
                Is.EqualTo("  first\nsecond\n"));
            SpecObjectNode content = (SpecObjectNode)Property(item, "content").Value;
            Assert.That(((SpecObjectNode)Property(content, "application/json").Value).Properties, Is.Empty);
        }

        [Test]
        public void ParsePreservesEmptyCompactBlockScalarAndFollowingKey()
        {
            SpecObjectNode root = ParseObject(
                "items:\n" +
                "  - description: |\n" +
                "    content:\n" +
                "      application/json: {}\n");
            SpecArrayNode items = (SpecArrayNode)Property(root, "items").Value;
            SpecObjectNode item = (SpecObjectNode)items.Items[0];

            Assert.That(((SpecStringNode)Property(item, "description").Value).Value, Is.Empty);
            SpecObjectNode content = (SpecObjectNode)Property(item, "content").Value;
            Assert.That(((SpecObjectNode)Property(content, "application/json").Value).Properties, Is.Empty);
        }

        [Test]
        public void ParsePreservesNestedCompactSequenceBlockScalarAndFollowingKey()
        {
            SpecArrayNode root = (SpecArrayNode)YamlParser.Parse(
                "- - description: |2\n" +
                "      first\n" +
                "      second\n" +
                "    content:\n" +
                "      application/json: {}\n",
                SourcePath);
            SpecArrayNode nested = (SpecArrayNode)root.Items[0];
            SpecObjectNode item = (SpecObjectNode)nested.Items[0];

            Assert.That(
                ((SpecStringNode)Property(item, "description").Value).Value,
                Is.EqualTo("first\nsecond\n"));
            SpecObjectNode content = (SpecObjectNode)Property(item, "content").Value;
            Assert.That(((SpecObjectNode)Property(content, "application/json").Value).Properties, Is.Empty);
        }

        [TestCase("|1", 1, "line\n")]
        [TestCase("|+", 2, "line\n")]
        [TestCase("|1+", 1, "line\n")]
        [TestCase("|+1", 1, "line\n")]
        [TestCase("|-", 2, "line")]
        [TestCase(">-", 2, "line")]
        [TestCase(">2-", 2, "line")]
        [TestCase(">+", 2, "line\n")]
        public void ParseAcceptsLegalBlockScalarHeaders(
            string header,
            int contentIndent,
            string expectedValue)
        {
            SpecObjectNode root = ParseObject(
                "value: " + header + "\n" +
                new string(' ', contentIndent) + "line\n");

            Assert.That(((SpecStringNode)Property(root, "value").Value).Value, Is.EqualTo(expectedValue));
        }

        [TestCase("|0")]
        [TestCase("|22")]
        [TestCase("|++")]
        [TestCase("|1+2")]
        [TestCase("| foo")]
        public void ParseRejectsInvalidBlockScalarHeadersWithPositionedDiagnostic(string header)
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("value: " + header + "\n  line\n", SourcePath));

            Assert.That(exception.DiagnosticId, Is.EqualTo("YAML006"), exception.Message);
            Assert.That(exception.SourcePath, Is.EqualTo(SourcePath));
            Assert.That(exception.Line, Is.EqualTo(1));
            Assert.That(exception.Column, Is.EqualTo(8));
            Assert.That(exception.Offset, Is.EqualTo(7));
        }

        [Test]
        public void ParseRejectsBlockScalarInsideFlowMappingBeforeCollectingFollowingLine()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("{a: |}\n  leaked\n", SourcePath));

            Assert.That(exception.DiagnosticId, Is.EqualTo("YAML006"), exception.Message);
            Assert.That(exception.SourcePath, Is.EqualTo(SourcePath));
            Assert.That(exception.Line, Is.EqualTo(1));
            Assert.That(exception.Column, Is.EqualTo(5));
            Assert.That(exception.Offset, Is.EqualTo(4));
        }

        [Test]
        public void ParseRejectsBlockScalarInsideFlowSequenceBeforeCollectingFollowingLine()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("[|]\n  leaked\n", SourcePath));

            Assert.That(exception.DiagnosticId, Is.EqualTo("YAML006"), exception.Message);
            Assert.That(exception.SourcePath, Is.EqualTo(SourcePath));
            Assert.That(exception.Line, Is.EqualTo(1));
            Assert.That(exception.Column, Is.EqualTo(2));
            Assert.That(exception.Offset, Is.EqualTo(1));
        }

        [Test]
        public void ParseSupportsNestedBlockSequences()
        {
            SpecArrayNode root = (SpecArrayNode)YamlParser.Parse(
                "- - a\n" +
                "  - b\n",
                SourcePath);
            SpecArrayNode nested = (SpecArrayNode)root.Items[0];

            Assert.That(nested.Items, Has.Count.EqualTo(2));
            Assert.That(((SpecStringNode)nested.Items[0]).Value, Is.EqualTo("a"));
            Assert.That(((SpecStringNode)nested.Items[1]).Value, Is.EqualTo("b"));
        }

        [Test]
        public void ParseExpandsAnchorsAndAliasesWithAliasLocation()
        {
            SpecObjectNode root = ParseObject(
                "defaults: &defaults\n" +
                "  title: Sample\n" +
                "copy: *defaults\n");
            SpecProperty copy = Property(root, "copy");
            SpecObjectNode copyValue = (SpecObjectNode)copy.Value;

            Assert.That(copy.Line, Is.EqualTo(3));
            Assert.That(copy.Column, Is.EqualTo(1));
            Assert.That(copyValue.Line, Is.EqualTo(3));
            Assert.That(copyValue.Column, Is.EqualTo(7));
            Assert.That(((SpecStringNode)Property(copyValue, "title").Value).Value, Is.EqualTo("Sample"));
        }

        [Test]
        public void ParseExpandsFlowAnchorsAndAliasesBeforeFlowComma()
        {
            SpecObjectNode root = ParseObject("{base: &x {v: 1}, copy: *x}\n");
            SpecObjectNode copy = (SpecObjectNode)Property(root, "copy").Value;

            Assert.That(((SpecNumberNode)Property(copy, "v").Value).Value, Is.EqualTo("1"));
        }

        [Test]
        public void ParseRejectsYaml11TypingAndDuplicateOrNonStringKeys()
        {
            SpecObjectNode root = ParseObject("on: ON\nyes: yes\noff: off\n");
            Assert.That(((SpecStringNode)Property(root, "on").Value).Value, Is.EqualTo("ON"));
            Assert.That(((SpecStringNode)Property(root, "yes").Value).Value, Is.EqualTo("yes"));
            Assert.That(((SpecStringNode)Property(root, "off").Value).Value, Is.EqualTo("off"));

            AssertDiagnostic("a: 1\n\"a\": 2\n", "YAML007");
            AssertDiagnostic("1: value\n", "YAML011");
            AssertDiagnostic("<<: {a: 1}\n", "YAML004");
            AssertDiagnostic("[]: value\n", "YAML011");
            AssertDiagnostic("{}: value\n", "YAML011");
        }

        [Test]
        public void ParseRejectsUnsupportedDocumentFeaturesAndAliasErrors()
        {
            AssertDiagnostic("%YAML 1.2\n---\na: b\n", "YAML009");
            AssertDiagnostic("---\na: b\n---\nc: d\n", "YAML010");
            AssertDiagnostic("a: !custom value\n", "YAML008");
            AssertDiagnostic("a: *missing\n", "YAML012");
            AssertDiagnostic("a: &a [*a]\n", "YAML013");
            AssertDiagnostic("a: &a one\nb: &a two\n", "YAML014");
        }

        [Test]
        public void ParseRejectsAliasAndNestingBudgetOverflow()
        {
            var aliases = new System.Text.StringBuilder("base: &base value\n");
            for (int index = 0; index < 4097; index++)
            {
                aliases.Append("copy").Append(index).Append(": *base\n");
            }

            AssertDiagnostic(aliases.ToString(), "YAML015");

            string deepFlow = new string('[', 257) + "0" + new string(']', 257);
            AssertDiagnostic("value: " + deepFlow + "\n", "YAML015");
        }

        [Test]
        public void ParseAcceptsMaximumFlowDepthButRejectsOneBeyondIt()
        {
            string depth256 = new string('[', 256) + "0" + new string(']', 256);
            Assert.DoesNotThrow(() => YamlParser.Parse(depth256 + "\n", SourcePath));

            string depth257 = new string('[', 257) + "0" + new string(']', 257);
            AssertDiagnostic(depth257 + "\n", "YAML015");
        }

        [Test]
        public void ParseReportsInvalidDedentAsYamlIndentationDiagnostic()
        {
            AssertDiagnostic(
                "a:\n" +
                "   b: c\n" +
                "  d: e\n",
                "YAML002");
        }

        [Test]
        public void ParseSupportsCommentsAroundFlowCollectionsAndMultilineFlow()
        {
            SpecObjectNode root = ParseObject(
                "values: [a, # first\n" +
                "  b]\n");
            SpecArrayNode values = (SpecArrayNode)Property(root, "values").Value;

            Assert.That(values.Items, Has.Count.EqualTo(2));
            Assert.That(((SpecStringNode)values.Items[0]).Value, Is.EqualTo("a"));
            Assert.That(((SpecStringNode)values.Items[1]).Value, Is.EqualTo("b"));
        }

        [Test]
        public void ParsePreservesColonInsideFlowUrlMapping()
        {
            SpecObjectNode root = ParseObject("servers: [{url: https://example.com}]\n");
            SpecArrayNode servers = (SpecArrayNode)Property(root, "servers").Value;
            SpecObjectNode server = (SpecObjectNode)servers.Items[0];

            Assert.That(((SpecStringNode)Property(server, "url").Value).Value, Is.EqualTo("https://example.com"));
        }

        [Test]
        public void ParsePreservesColonInsideFlowUrlSequence()
        {
            SpecObjectNode root = ParseObject("urls: [https://example.com]\n");
            SpecArrayNode urls = (SpecArrayNode)Property(root, "urls").Value;

            Assert.That(((SpecStringNode)urls.Items[0]).Value, Is.EqualTo("https://example.com"));
        }

        [Test]
        public void ParsePreservesDashInsidePlainScalar()
        {
            SpecObjectNode root = ParseObject("description: foo - bar\n");

            Assert.That(((SpecStringNode)Property(root, "description").Value).Value, Is.EqualTo("foo - bar"));
        }

        [Test]
        public void ParsePreservesFlowPunctuationInsideBlockPlainScalar()
        {
            SpecObjectNode root = ParseObject("description: A, B\n");

            Assert.That(((SpecStringNode)Property(root, "description").Value).Value, Is.EqualTo("A, B"));
        }

        [Test]
        public void ParsePreservesBracesInsideBlockPlainScalar()
        {
            SpecObjectNode root = ParseObject("description: Use {id}\n");

            Assert.That(((SpecStringNode)Property(root, "description").Value).Value, Is.EqualTo("Use {id}"));
        }

        [Test]
        public void ParseSupportsOpenApiPathTemplateAsBlockMappingKey()
        {
            SpecObjectNode root = ParseObject(
                "/pets/{petId}:\n" +
                "  get:\n" +
                "    summary: Fetch\n");
            SpecObjectNode path = (SpecObjectNode)Property(root, "/pets/{petId}").Value;

            Assert.That(((SpecStringNode)Property((SpecObjectNode)Property(path, "get").Value, "summary").Value).Value, Is.EqualTo("Fetch"));
        }

        [Test]
        public void ParseTreatsIndentedDocumentMarkersAsPlainValues()
        {
            SpecObjectNode root = ParseObject(
                "values:\n" +
                "  start: ---\n" +
                "  end: ...\n");

            Assert.That(((SpecStringNode)Property((SpecObjectNode)Property(root, "values").Value, "start").Value).Value, Is.EqualTo("---"));
            Assert.That(((SpecStringNode)Property((SpecObjectNode)Property(root, "values").Value, "end").Value).Value, Is.EqualTo("..."));
        }

        [Test]
        public void ParseSupportsMultilineFlowMappingWithoutWhitespaceAfterColon()
        {
            SpecObjectNode root = ParseObject(
                "value: {a: 1,\n" +
                "  b:2}\n");
            SpecObjectNode value = (SpecObjectNode)Property(root, "value").Value;

            Assert.That(((SpecNumberNode)Property(value, "a").Value).Value, Is.EqualTo("1"));
            Assert.That(((SpecNumberNode)Property(value, "b").Value).Value, Is.EqualTo("2"));
        }

        [Test]
        public void ParseReadsAMultiLineSingleQuotedDescription()
        {
            SpecObjectNode root = ParseObject(
                "info:\n" +
                "  title: Example\n" +
                "  version: 1.0.0\n" +
                "  description: 'first line\n" +
                "    second line'\n");

            SpecObjectNode info = (SpecObjectNode)Property(root, "info").Value;
            Assert.That(StringValue(info, "description"), Is.EqualTo("first line second line"));
            Assert.That(StringValue(info, "version"), Is.EqualTo("1.0.0"));
        }

        [Test]
        public void ParseFoldsDoubleQuotedLinesLikeYamlExample75()
        {
            SpecObjectNode root = ParseObject(
                "v: \"folded \n" +
                "  to a space,\t\n" +
                " \n" +
                "  to a line feed, or \t\\\n" +
                "  \\ \tnon-content\"\n");

            Assert.That(
                StringValue(root, "v"),
                Is.EqualTo("folded to a space,\nto a line feed, or \t \tnon-content"));
        }

        [Test]
        public void ParseFoldsSingleQuotedLinesLikeYamlExample79AsARootScalar()
        {
            SpecNode root = YamlParser.Parse("' 1st non-empty\n\n 2nd non-empty \n 3rd non-empty '\n", SourcePath);

            Assert.That(((SpecStringNode)root).Value, Is.EqualTo(" 1st non-empty\n2nd non-empty 3rd non-empty "));
        }

        [TestCase("v: ' 1st non-empty\n\n  2nd non-empty \n  \t3rd non-empty '\n")]
        [TestCase("v: ' 1st non-empty\r\n\r\n  2nd non-empty \r\n  \t3rd non-empty '\r\n")]
        [TestCase("v: ' 1st non-empty\r\r  2nd non-empty \r  \t3rd non-empty '\r")]
        public void ParseFoldsSingleQuotedLinesForEveryLineBreakStyle(string source)
        {
            Assert.That(
                StringValue(ParseObject(source), "v"),
                Is.EqualTo(" 1st non-empty\n2nd non-empty 3rd non-empty "));
        }

        [TestCase("v: 'it''s\n  ok'\n", "it's ok")]
        [TestCase("v: 'a''\n  b'\n", "a' b")]
        [TestCase("v: 'a\n\n\n  b'\n", "a\n\nb")]
        [TestCase("v: 'a  \n   b'\n", "a b")]
        [TestCase("v: 'a\n  '\n", "a ")]
        [TestCase("v: \"a\\\n  b\"\n", "ab")]
        [TestCase("v: \"a\\\\\n  b\"\n", "a\\ b")]
        [TestCase("v: \"a\\t\n  b\"\n", "a\t b")]
        [TestCase("v: \"a\\ \n  b\"\n", "a  b")]
        [TestCase("v: \"a\\\n\n  b\"\n", "a\nb")]
        public void ParseFoldsQuotedLineBreaks(string source, string expected)
        {
            Assert.That(StringValue(ParseObject(source), "v"), Is.EqualTo(expected));
        }

        [Test]
        public void ParseKeepsLinesOfNodesAfterAMultiLineScalar()
        {
            SpecObjectNode root = ParseObject("a: 'x\r\n  y'\r\nb: 1\r\nc: \"p\r\n  q\"\r\nd: 2\r\n");

            Assert.That(StringValue(root, "a"), Is.EqualTo("x y"));
            Assert.That(StringValue(root, "c"), Is.EqualTo("p q"));
            Assert.That(Property(root, "a").Value.Column, Is.EqualTo(4));
            Assert.That(Property(root, "b").Line, Is.EqualTo(3));
            Assert.That(Property(root, "c").Line, Is.EqualTo(4));
            Assert.That(Property(root, "d").Line, Is.EqualTo(6));
        }

        [Test]
        public void ParseReadsMultiLineQuotedScalarsInFlowCollectionsAndSequences()
        {
            SpecObjectNode root = ParseObject(
                "flow: [ 'a\nb', \"c\n  d\" ]\n" +
                "seq:\n" +
                "  - 'e\n    f'\n" +
                "  - g\n" +
                "map: { k: 'h\n  i' }\n" +
                "same:\n  'j\n  k'\n");

            SpecArrayNode flow = (SpecArrayNode)Property(root, "flow").Value;
            Assert.That(((SpecStringNode)flow.Items[0]).Value, Is.EqualTo("a b"));
            Assert.That(((SpecStringNode)flow.Items[1]).Value, Is.EqualTo("c d"));
            SpecArrayNode seq = (SpecArrayNode)Property(root, "seq").Value;
            Assert.That(((SpecStringNode)seq.Items[0]).Value, Is.EqualTo("e f"));
            Assert.That(((SpecStringNode)seq.Items[1]).Value, Is.EqualTo("g"));
            Assert.That(StringValue((SpecObjectNode)Property(root, "map").Value, "k"), Is.EqualTo("h i"));
            Assert.That(StringValue(root, "same"), Is.EqualTo("j k"));
        }

        [Test]
        public void ParseReportsAnUnterminatedQuoteAtItsStart()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("v: 'a\n  b\n", SourcePath));

            Assert.That(exception.DiagnosticId, Is.EqualTo("YAML006"));
            Assert.That(exception.Line, Is.EqualTo(1));
            Assert.That(exception.Column, Is.EqualTo(4));
            Assert.That(exception.Offset, Is.EqualTo(3));
        }

        [Test]
        public void ParseReportsAMissingClosingQuoteBeforeTheNextKeyOnThatLine()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("a:\n  size: \"5\n  next: 1\n", SourcePath));

            Assert.That(exception.DiagnosticId, Is.EqualTo("YAML002"));
            Assert.That(exception.Line, Is.EqualTo(3));
        }

        [Test]
        public void ParseStillReadsAPlainScalarWithAQuoteInTheMiddle()
        {
            SpecObjectNode root = ParseObject("size: 5\"\nnext: it's\n");

            Assert.That(StringValue(root, "size"), Is.EqualTo("5\""));
            Assert.That(StringValue(root, "next"), Is.EqualTo("it's"));
        }

        [TestCase("v: 'a\n---\n  b'\n")]
        [TestCase("v: 'a\n...\n  b'\n")]
        [TestCase("v: \"a\n--- \n  b\"\n")]
        public void ParseRejectsADocumentMarkerInsideAQuotedScalar(string source)
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse(source, SourcePath));

            Assert.That(exception.DiagnosticId, Is.EqualTo("YAML006"));
            Assert.That(exception.Line, Is.EqualTo(2));
            Assert.That(exception.Column, Is.EqualTo(1));
        }

        [TestCase("v: 'a\nb'\n")]
        [TestCase("m:\n  k: 'a\n  b'\n")]
        [TestCase("- 'a\nb'\n")]
        public void ParseRejectsAContinuationLineThatIsNotIndentedDeeper(string source)
        {
            AssertDiagnostic(source, "YAML002");
        }

        [Test]
        public void ParseDoesNotCheckTheIndentOfBlankContinuationLines()
        {
            Assert.That(StringValue(ParseObject("v: 'a\n\n  b'\n"), "v"), Is.EqualTo("a\nb"));
            Assert.That(StringValue(ParseObject("v: 'a\n \n  b'\n"), "v"), Is.EqualTo("a\nb"));
        }

        [TestCase("v: 'a\n  b' title: X\n")]
        [TestCase("v: 'a\n  b' # comment\nw: 'c\n  d' x\n")]
        [TestCase("v: \"a\n  b\" \"c\"\n")]
        [TestCase("- 'a\n  b' title\n")]
        public void ParseRejectsContentAfterTheClosingQuoteOfAMultiLineScalar(string source)
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse(source, SourcePath));

            Assert.That(exception.DiagnosticId, Is.EqualTo("YAML006"));
            Assert.That(exception.Line, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void ParseAllowsACommentAfterTheClosingQuoteOfAMultiLineScalar()
        {
            SpecObjectNode root = ParseObject("v: 'a\n  b' # comment\nw: 1\n");

            Assert.That(StringValue(root, "v"), Is.EqualTo("a b"));
            Assert.That(Property(root, "w").Line, Is.EqualTo(3));
        }

        [TestCase("'a\n  b': 1\n")]
        [TestCase("\"a\n  b\": 1\n")]
        [TestCase("- 'a\n  b': 1\n")]
        [TestCase("v: {'a\n  b': 1}\n")]
        public void ParseRejectsAKeyThatSpansLines(string source)
        {
            AssertDiagnostic(source, "YAML011");
        }

        [Test]
        public void ParsePointsAtTheRightLineForAnErrorInsideAMultiLineToken()
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("v: \"a\n  \\xZZ\"\n", SourcePath));

            Assert.That(exception.DiagnosticId, Is.EqualTo("YAML006"));
            Assert.That(exception.Line, Is.EqualTo(2));
            Assert.That(exception.Column, Is.EqualTo(3));
            Assert.That(exception.Offset, Is.EqualTo(8));
        }

        [Test]
        public void ParseReadsAnIndentlessSequenceFromTheIssueReproduction()
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

            Assert.That(
                Render(root),
                Is.EqualTo(
                    "{\"openapi\":\"3.0.4\",\"info\":{\"title\":\"Example\",\"version\":\"1.0.0\"}," +
                    "\"x-example\":{\"items\":[\"a\",\"b\"]},\"paths\":{}}"));
        }

        [Test]
        public void ParseReadsAnIndentlessSequenceUnderARootKey()
        {
            SpecNode root = YamlParser.Parse("tags:\n- a\n- b\nnext: 1\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"tags\":[\"a\",\"b\"],\"next\":1}"));
        }

        [Test]
        public void ParseReadsIndentlessMappingItemsAndTheKeyThatFollows()
        {
            SpecObjectNode root = ParseObject(
                "tags:\n" +
                "- name: x\n" +
                "  description: y\n" +
                "- name: z\n" +
                "paths: {}\n");

            Assert.That(
                Render(root),
                Is.EqualTo("{\"tags\":[{\"name\":\"x\",\"description\":\"y\"},{\"name\":\"z\"}],\"paths\":{}}"));
            SpecArrayNode tags = (SpecArrayNode)Property(root, "tags").Value;
            Assert.That(tags.Line, Is.EqualTo(2));
            Assert.That(tags.Column, Is.EqualTo(1));
            Assert.That(tags.Items[0].Line, Is.EqualTo(2));
            Assert.That(tags.Items[0].Column, Is.EqualTo(3));
            Assert.That(Property((SpecObjectNode)tags.Items[0], "description").Line, Is.EqualTo(3));
        }

        [Test]
        public void ParseReadsANestedIndentlessSequenceAndTheSiblingsThatFollow()
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

            Assert.That(Render(root), Is.EqualTo("{\"a\":{\"b\":[1,2],\"c\":3,\"d\":[\"x\"]},\"e\":4}"));
        }

        [Test]
        public void ParseReadsAnIndentlessSequenceInsideACompactMappingItem()
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

            Assert.That(
                Render(root),
                Is.EqualTo(
                    "{\"list\":[{\"name\":\"x\",\"tags\":[\"a\",\"b\"],\"description\":\"y\"}," +
                    "{\"name\":\"z\",\"tags\":[\"c\"]}],\"other\":1}"));
        }

        [Test]
        public void ParseReadsAnIndentlessSequenceInsideACompactMappingOfARootSequence()
        {
            SpecNode root = YamlParser.Parse(
                "- name: x\n" +
                "  tags:\n" +
                "  - a\n" +
                "- name: z\n",
                SourcePath);

            Assert.That(Render(root), Is.EqualTo("[{\"name\":\"x\",\"tags\":[\"a\"]},{\"name\":\"z\"}]"));
        }

        [Test]
        public void ParseReadsAnIndentlessSequenceInsideAnIndentlessSequence()
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

            Assert.That(Render(root), Is.EqualTo("{\"a\":[{\"b\":[1,2],\"c\":[3]},{\"d\":4}],\"z\":5}"));
        }

        [Test]
        public void ParseSkipsCommentsAroundTheItemsOfAnIndentlessSequence()
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

            Assert.That(Render(root), Is.EqualTo("{\"a\":[\"x\",\"y\"],\"b\":1}"));
        }

        [Test]
        public void ParseReadsABlockScalarItemOfAnIndentlessSequence()
        {
            SpecNode root = YamlParser.Parse(
                "a:\n" +
                "- |\n" +
                "  text\n" +
                "  more\n" +
                "- b\n" +
                "c: 1\n",
                SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":[\"text\\nmore\\n\",\"b\"],\"c\":1}"));
        }

        [Test]
        public void ParseReadsAnAnchoredIndentlessSequence()
        {
            SpecNode root = YamlParser.Parse(
                "a: &list\n" +
                "- 1\n" +
                "- 2\n" +
                "b: *list\n",
                SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":[1,2],\"b\":[1,2]}"));
        }

        [Test]
        public void ParseReadsAnAnchoredIndentlessSequenceInsideACompactMapping()
        {
            SpecNode root = YamlParser.Parse(
                "- name: &list\n" +
                "  - 1\n" +
                "- other: 2\n",
                SourcePath);

            Assert.That(Render(root), Is.EqualTo("[{\"name\":[1]},{\"other\":2}]"));
        }

        [Test]
        public void ParseReadsTheIndentedAndTheIndentlessFormsAsTheSameTree()
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

            Assert.That(Render(YamlParser.Parse(indentless, SourcePath)), Is.EqualTo(Render(YamlParser.Parse(indented, SourcePath))));
        }

        [Test]
        public void ParseStillReadsNullForAKeyWhoseNextLineIsAnotherKey()
        {
            SpecNode root = YamlParser.Parse("a:\nb: 1\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":null,\"b\":1}"));
        }

        [TestCase("a: 1\n- x\n", "YAML003")]
        [TestCase("p:\n  a:\n- x\n", "YAML003")]
        [TestCase("a:\n- x\nb: 1\n- y\n", "YAML003")]
        [TestCase("p:\n  a: 1\n  - x\n", "YAML002")]
        [TestCase("p:\n  a:\n  - x\n  b: 1\n  - y\n", "YAML002")]
        [TestCase("p:\n  a:\n  - x\n    b: 1\n", "YAML011")]
        [TestCase("a:\n- &x\n- 1\n", "YAML004")]
        [TestCase("a:\n  - &x\n  - 1\n", "YAML004")]
        [TestCase("- &x\n- 1\n", "YAML004")]
        public void ParseStillRejectsInvalidMixesOfKeysAndDashes(string source, string expectedCode)
        {
            AssertDiagnostic(source, expectedCode);
        }


        [Test]
        public void ParsePlainMultiLineReadsATwoLineMappingValue()
        {
            SpecNode root = YamlParser.Parse("a: one\n  two\nb: 1\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"one two\",\"b\":1}"));
        }

        [Test]
        public void ParsePlainMultiLineReadsAThreeLineMappingValue()
        {
            SpecNode root = YamlParser.Parse("a: one\n  two\n  three\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"one two three\"}"));
        }

        [Test]
        public void ParsePlainMultiLineReadsAJapaneseDescriptionWrappedByAWriter()
        {
            SpecNode root = YamlParser.Parse("paths:\n  /x:\n    get:\n      description: 一段目の説明は長いので折り返される。上限を超えると競合\n        (409)、不足は別のエラーになる。\n      summary: 取得\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"paths\":{\"/x\":{\"get\":{\"description\":\"一段目の説明は長いので折り返される。上限を超えると競合 (409)、不足は別のエラーになる。\",\"summary\":\"取得\"}}}}"));
        }

        [Test]
        public void ParsePlainMultiLineReadsAnEmptyLineAsALineFeed()
        {
            SpecNode root = YamlParser.Parse("a: one\n\n  two\nb: 1\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"one\\ntwo\",\"b\":1}"));
        }

        [Test]
        public void ParsePlainMultiLineReadsTwoEmptyLinesAsTwoLineFeeds()
        {
            SpecNode root = YamlParser.Parse("a: one\n\n\n  two\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"one\\n\\ntwo\"}"));
        }

        [Test]
        public void ParsePlainMultiLineReadsAWrappedSequenceItem()
        {
            SpecNode root = YamlParser.Parse("- one\n  two\n- three\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("[\"one two\",\"three\"]"));
        }

        [Test]
        public void ParsePlainMultiLineReadsAWrappedCompactMappingValue()
        {
            SpecNode root = YamlParser.Parse("- k: one\n    two\n  j: 1\n- k: x\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("[{\"k\":\"one two\",\"j\":1},{\"k\":\"x\"}]"));
        }

        [Test]
        public void ParsePlainMultiLineReadsAWrappedValueFollowedByItsSiblingKeyAtTheParentLevel()
        {
            SpecNode root = YamlParser.Parse("a:\n  b: one\n    two\nc: 1\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":{\"b\":\"one two\"},\"c\":1}"));
        }

        [Test]
        public void ParsePlainMultiLineStopsAtACommentLineAfterTheWrappedValue()
        {
            SpecNode root = YamlParser.Parse("a: one\n  two\n# note\nb: 1\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"one two\",\"b\":1}"));
        }

        [Test]
        public void ParsePlainMultiLineStopsAtATrailingCommentOfTheLastLine()
        {
            SpecNode root = YamlParser.Parse("a: one\n  two # note\nb: 1\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"one two\",\"b\":1}"));
        }

        [Test]
        public void ParsePlainMultiLineTrimsTheWhitespaceAroundEveryLine()
        {
            SpecNode root = YamlParser.Parse("a: one  \n    two	 \n  three  \nb: 1\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"one two three\",\"b\":1}"));
        }

        [Test]
        public void ParsePlainMultiLineReadsAWrappedValueWithCrLfLineBreaks()
        {
            SpecNode root = YamlParser.Parse("a: one\r\n  two\r\n\r\n  three\r\nb: 1\r\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"one two\\nthree\",\"b\":1}"));
        }

        [Test]
        public void ParsePlainMultiLineReadsAWrappedValueWithAColonThatIsNotAMappingIndicator()
        {
            SpecNode root = YamlParser.Parse("a: see\n  http://x.example/y\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"see http://x.example/y\"}"));
        }

        [Test]
        public void ParsePlainMultiLineReadsAWrappedValueAfterAnAnchor()
        {
            SpecNode root = YamlParser.Parse("a: &x one\n  two\nb: *x\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"one two\",\"b\":\"one two\"}"));
        }

        [Test]
        public void ParsePlainMultiLineReadsWrappedValuesInsideIndentlessSequences()
        {
            SpecNode root = YamlParser.Parse("list:\n- one\n  two\n- k: x\n    y\n  j: z\n    w\nnext: 1\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"list\":[\"one two\",{\"k\":\"x y\",\"j\":\"z w\"}],\"next\":1}"));
        }

        [Test]
        public void ParsePlainMultiLineReadsAWrappedValueThatLooksLikeAScalarOfAnotherType()
        {
            SpecNode root = YamlParser.Parse("a: true\n  false\nb: 1\n  2\nc: null\n  x\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"true false\",\"b\":\"1 2\",\"c\":\"null x\"}"));
        }

        [Test]
        public void ParsePlainMultiLineKeepsTheTypeResolutionOfSingleLinePlainScalars()
        {
            SpecNode root = YamlParser.Parse("a: true\nb: 1\nc: null\nd: 1.5\ne: text\nf: false\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":true,\"b\":1,\"c\":null,\"d\":1.5,\"e\":\"text\",\"f\":false}"));
        }

        [Test]
        public void ParsePlainMultiLineKeepsAFlowSequenceOverSeveralLines()
        {
            SpecNode root = YamlParser.Parse("a: [x,\n  y]\nb: 1\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":[\"x\",\"y\"],\"b\":1}"));
        }

        [Test]
        public void ParsePlainMultiLineKeepsALineWithTheIndentationOfTheKeyAsAnEntry()
        {
            SpecNode root = YamlParser.Parse("a: one\nb: two\n", SourcePath);

            Assert.That(Render(root), Is.EqualTo("{\"a\":\"one\",\"b\":\"two\"}"));
        }

        [Test]
        public void ParsePlainMultiLineARowAtTheKeyIndentationIsNotAContinuation()
        {
            AssertDiagnostic("a:\n  b: one\n  two\n", "YAML002");
        }

        [Test]
        public void ParsePlainMultiLineAMappingValueInAContinuationLineIsRejected()
        {
            AssertDiagnostic("a: one\n  b: two\n", "YAML004");
        }

        [TestCase("a: &x one\n  b: two\n", "YAML004")]
        [TestCase("- k: &x one\n    b: two\n", "YAML004")]
        [TestCase("- &x one\n  b: two\n", "YAML011")]
        public void ParsePlainMultiLineAMappingValueAfterAnAnchorIsRejectedLikeWithoutAnAnchor(string source, string expectedCode)
        {
            AssertDiagnostic(source, expectedCode);
        }

        [Test]
        public void ParsePlainMultiLineAMultiLinePlainScalarCannotBeAKey()
        {
            AssertDiagnostic("- one\n  b: two\n", "YAML011");
        }

        [Test]
        public void ParsePlainMultiLineACommentLineEndsThePlainScalar()
        {
            AssertDiagnostic("a: one\n  # note\n  two\n", "YAML002");
        }

        [Test]
        public void ParsePlainMultiLineACompactMappingContinuationAtTheKeyIndentationIsNotAContinuation()
        {
            AssertDiagnostic("- k: one\n  two\n", "YAML002");
        }

        [Test]
        public void ParsePlainMultiLineReportsTheLocationsOfTheValueAndTheFollowingKey()
        {
            SpecObjectNode root = ParseObject("a: one\n  two\nb: 1\n");
            Assert.That(Property(root, "a").Value.Line, Is.EqualTo(1));
            Assert.That(Property(root, "a").Value.Column, Is.EqualTo(4));
            Assert.That(Property(root, "b").Value.Line, Is.EqualTo(3));

            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse("a: one\n  two: x\n", SourcePath));
            Assert.That(exception.Line, Is.EqualTo(2));
            Assert.That(exception.Column, Is.EqualTo(6));
            Assert.That(exception.Offset, Is.EqualTo(12));
        }

        [Test]
        public void ParsePlainMultiLineDoesNotContinueARootScalar()
        {
            Assert.Throws<YamlParseException>(() => YamlParser.Parse("one\n  two\n", SourcePath));
        }

        // A compact JSON-like text of the tree (numbers and booleans are bare, strings are quoted).
        private static string Render(SpecNode node)
        {
            var builder = new System.Text.StringBuilder();
            Render(node, builder);
            return builder.ToString();
        }

        private static void Render(SpecNode node, System.Text.StringBuilder builder)
        {
            SpecObjectNode objectNode = node as SpecObjectNode;
            if (objectNode != null)
            {
                builder.Append('{');
                for (int index = 0; index < objectNode.Properties.Count; index++)
                {
                    if (index > 0) builder.Append(',');
                    builder.Append('"').Append(objectNode.Properties[index].Name).Append("\":");
                    Render(objectNode.Properties[index].Value, builder);
                }

                builder.Append('}');
                return;
            }

            SpecArrayNode arrayNode = node as SpecArrayNode;
            if (arrayNode != null)
            {
                builder.Append('[');
                for (int index = 0; index < arrayNode.Items.Count; index++)
                {
                    if (index > 0) builder.Append(',');
                    Render(arrayNode.Items[index], builder);
                }

                builder.Append(']');
                return;
            }

            SpecStringNode stringNode = node as SpecStringNode;
            if (stringNode != null)
            {
                builder.Append('"').Append(stringNode.Value.Replace("\n", "\\n")).Append('"');
                return;
            }

            SpecNumberNode numberNode = node as SpecNumberNode;
            if (numberNode != null)
            {
                builder.Append(numberNode.Value);
                return;
            }

            SpecBooleanNode booleanNode = node as SpecBooleanNode;
            builder.Append(booleanNode != null ? (booleanNode.Value ? "true" : "false") : "null");
        }

        private static string StringValue(SpecObjectNode node, string name)
        {
            return ((SpecStringNode)Property(node, name).Value).Value;
        }

        private static SpecObjectNode ParseObject(string source)
        {
            return (SpecObjectNode)YamlParser.Parse(source, SourcePath);
        }

        private static SpecProperty Property(SpecObjectNode node, string name)
        {
            for (int index = 0; index < node.Properties.Count; index++)
            {
                if (node.Properties[index].Name == name)
                {
                    return node.Properties[index];
                }
            }

            Assert.Fail("Missing YAML property: " + name);
            return null;
        }

        private static void AssertDiagnostic(string source, string expectedCode)
        {
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse(source, SourcePath));

            Assert.That(exception.DiagnosticId, Is.EqualTo(expectedCode), exception.Message);
            Assert.That(exception.SourcePath, Is.EqualTo(SourcePath));
            Assert.That(exception.Line, Is.GreaterThanOrEqualTo(1));
            Assert.That(exception.Column, Is.GreaterThanOrEqualTo(1));
            Assert.That(exception.Offset, Is.GreaterThanOrEqualTo(0));
        }
    }
}
