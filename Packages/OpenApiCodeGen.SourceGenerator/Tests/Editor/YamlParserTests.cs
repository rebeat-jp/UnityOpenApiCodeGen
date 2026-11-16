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
