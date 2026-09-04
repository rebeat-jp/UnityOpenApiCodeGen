using System.Collections.Generic;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class YamlLexerTests
    {
        [Test]
        public void TokenizeTracksBomCrLfAndUtf16Offsets()
        {
            IReadOnlyList<YamlToken> tokens = YamlLexer.Tokenize(
                "\uFEFFa: 1\r\n# comment\nb: 😀\n",
                "Assets/Specs/openapi.yaml");

            Assert.That(tokens[0].Kind, Is.EqualTo(YamlTokenKind.Scalar));
            Assert.That(tokens[0].Value, Is.EqualTo("a"));
            Assert.That(tokens[0].Span.Start.Line, Is.EqualTo(1));
            Assert.That(tokens[0].Span.Start.Column, Is.EqualTo(1));
            Assert.That(tokens[0].Span.Start.Offset, Is.EqualTo(1));
            Assert.That(tokens[0].Span.Length, Is.EqualTo(1));
            Assert.That(tokens[1].Kind, Is.EqualTo(YamlTokenKind.Colon));
            Assert.That(tokens[1].Span.Start.Offset, Is.EqualTo(2));
            Assert.That(tokens[3].Kind, Is.EqualTo(YamlTokenKind.NewLine));
            Assert.That(tokens[3].Span.Start.Offset, Is.EqualTo(5));
            Assert.That(tokens[3].Span.Length, Is.EqualTo(2));

            YamlToken emoji = FindToken(tokens, YamlTokenKind.Scalar, "😀");
            Assert.That(emoji.Span.Start.Line, Is.EqualTo(3));
            Assert.That(emoji.Span.Start.Column, Is.EqualTo(4));
            Assert.That(emoji.Span.Start.Offset, Is.EqualTo(20));
            Assert.That(emoji.Span.Length, Is.EqualTo(2));
            Assert.That(emoji.SourcePath, Is.EqualTo("Assets/Specs/openapi.yaml"));
        }

        [Test]
        public void TokenizeSpansAreMonotonicAndNonOverlapping()
        {
            IReadOnlyList<YamlToken> tokens = YamlLexer.Tokenize(
                "items:\n  - value: -2\n    text: |\n      line\n",
                "Assets/Specs/openapi.yaml");

            int previousEnd = -1;
            for (int index = 0; index < tokens.Count; index++)
            {
                YamlToken token = tokens[index];
                Assert.That(token.Offset, Is.GreaterThanOrEqualTo(previousEnd), token.ToString());
                Assert.That(token.Length, Is.GreaterThanOrEqualTo(0), token.ToString());
                previousEnd = token.Offset + token.Length;
            }
        }

        [Test]
        public void TokenizeDistinguishesNegativeNumbersFromSequenceDashes()
        {
            IReadOnlyList<YamlToken> tokens = YamlLexer.Tokenize(
                "values:\n  - -2\n  - 3\n",
                "Assets/Specs/openapi.yaml");

            Assert.That(FindToken(tokens, YamlTokenKind.Scalar, "-2"), Is.Not.Null);
            Assert.That(FindFirstToken(tokens, YamlTokenKind.Dash), Is.Not.Null);
        }

        [Test]
        public void TokenizeRecognizesCommentsAnchorsAliasesAndFlowPunctuation()
        {
            IReadOnlyList<YamlToken> tokens = YamlLexer.Tokenize(
                "base: &base {x: 1} # comment\ncopy: *base\n",
                "Assets/Specs/openapi.yaml");

            Assert.That(FindToken(tokens, YamlTokenKind.Anchor, "&base"), Is.Not.Null);
            Assert.That(FindToken(tokens, YamlTokenKind.Alias, "*base"), Is.Not.Null);
            Assert.That(FindToken(tokens, YamlTokenKind.FlowMappingStart, "{"), Is.Not.Null);
            Assert.That(FindToken(tokens, YamlTokenKind.FlowMappingEnd, "}"), Is.Not.Null);
            YamlToken comment = FindFirstToken(tokens, YamlTokenKind.Comment);
            Assert.That(comment, Is.Not.Null);
            Assert.That(comment.Value, Is.EqualTo("# comment"));
            Assert.That(comment.Span.Start.Line, Is.EqualTo(1));
            Assert.That(comment.Span.Start.Column, Is.EqualTo(20));
            Assert.That(comment.Span.Length, Is.EqualTo(9));
        }

        [Test]
        public void TokenizeRejectsTabIndentationAndUnterminatedQuotes()
        {
            YamlParseException indentation = Assert.Throws<YamlParseException>(
                () => YamlLexer.Tokenize("\tkey: value\n", "Assets/Specs/openapi.yaml"));
            Assert.That(indentation.DiagnosticId, Is.EqualTo("YAML002"));

            YamlParseException quote = Assert.Throws<YamlParseException>(
                () => YamlLexer.Tokenize("key: \"unterminated\n", "Assets/Specs/openapi.yaml"));
            Assert.That(quote.DiagnosticId, Is.EqualTo("YAML006"));
        }

        private static YamlToken FindToken(
            IReadOnlyList<YamlToken> tokens,
            YamlTokenKind kind,
            string value)
        {
            for (int index = 0; index < tokens.Count; index++)
            {
                if (tokens[index].Kind == kind && tokens[index].Value == value)
                {
                    return tokens[index];
                }
            }

            return null;
        }

        private static YamlToken FindFirstToken(
            IReadOnlyList<YamlToken> tokens,
            YamlTokenKind kind)
        {
            for (int index = 0; index < tokens.Count; index++)
            {
                if (tokens[index].Kind == kind)
                {
                    return tokens[index];
                }
            }

            return null;
        }
    }
}
