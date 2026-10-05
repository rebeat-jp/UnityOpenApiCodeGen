using System;
using System.Text;

using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class YamlSurrogateEscapeTests
    {
        private const string SourcePath = "Assets/Specs/unicode.yaml";

        [Theory]
        [InlineData("\\uD83D\\uDE00")]
        [InlineData("\\U0001F600")]
        public void ValidNonBmpEscapeNormalizesAndRoundTrips(string escapedValue)
        {
            string source = "value: \"" + escapedValue + "\"\n";
            Rhycol.OpenApiCodeGen.SourceGenerator.Editor.NormalizedSpecBundle bundle =
                new RawYamlNormalizer().Normalize(
                Encoding.UTF8.GetBytes(source),
                TestBundleFactory.SpecId,
                SourcePath);

            Rhycol.OpenApiCodeGen.SourceGenerator.NormalizedSpecBundle parsedBundle =
                NormalizedSpecBundleReader.Read(Encoding.UTF8.GetString(bundle.Bytes));
            Assert.True(parsedBundle.Root.TryGetProperty("value", out SpecNode value));
            Assert.Equal("😀", value.GetString());
        }

        [Theory]
        [InlineData("\\uD83D", 9)]
        [InlineData("\\uDE00", 9)]
        [InlineData("\\uDE00\\uD83D", 9)]
        [InlineData("\\uD83D\\u0041", 15)]
        [InlineData("\\uD83D\\uD83D", 15)]
        [InlineData("\\uD83Dx", 9)]
        [InlineData("\\U0000D800", 9)]
        public void InvalidSurrogateEscapeHasPositionedParserAndNormalizerDiagnostic(
            string escapedValue,
            int expectedColumn)
        {
            string source = "value: \"" + escapedValue + "\"\n";
            YamlParseException parserException = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse(source, SourcePath));
            Assert.Equal("YAML006", parserException.DiagnosticId);
            Assert.Equal(SourcePath, parserException.SourcePath);
            Assert.Equal(1, parserException.Line);
            Assert.Equal(expectedColumn, parserException.Column);
            Assert.Equal(expectedColumn - 1, parserException.Offset);

            NormalizedSpecException normalizerException = Assert.Throws<NormalizedSpecException>(
                () => new RawYamlNormalizer().Normalize(
                    Encoding.UTF8.GetBytes(source),
                    TestBundleFactory.SpecId,
                    SourcePath));
            Assert.Equal("YAML006", normalizerException.DiagnosticCode);
            Assert.Equal(SourcePath, normalizerException.SourcePath);
            Assert.Equal(1, normalizerException.Line);
            Assert.Equal(expectedColumn, normalizerException.Column);
        }

        [Theory]
        [InlineData("value: \"", "\"\n", 9)]
        [InlineData("value: '", "'\n", 9)]
        [InlineData("value: ", "\n", 8)]
        public void DirectParserRejectsUnpairedLiteralSurrogates(
            string prefix,
            string suffix,
            int expectedColumn)
        {
            string source = prefix + '\uD800' + suffix;
            YamlParseException exception = Assert.Throws<YamlParseException>(
                () => YamlParser.Parse(source, SourcePath));

            Assert.Equal("YAML006", exception.DiagnosticId);
            Assert.Equal(expectedColumn, exception.Column);
        }

        [Fact]
        public void DirectParserAcceptsPairedLiteralSurrogates()
        {
            SpecObjectNode root = (SpecObjectNode)YamlParser.Parse("value: \"😀\"\n", SourcePath);
            Assert.Equal("😀", ((SpecStringNode)root.Properties[0].Value).Value);
        }
    }
}
