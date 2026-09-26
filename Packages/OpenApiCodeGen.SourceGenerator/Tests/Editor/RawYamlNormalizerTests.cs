using System;
using System.Text;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class RawYamlNormalizerTests
    {
        private const string SpecId = "0123456789abcdef0123456789abcdef";
        private const string SourcePath = "Assets/Specs/openapi.yaml";

        [Test]
        public void NormalizeWritesCanonicalBundleForYaml12CompatibleBlockAndFlowValues()
        {
            const string RawYaml = "\uFEFFopenapi: 3.0.3\r\n" +
                                   "metadata:\r\n" +
                                   "  values: [null, true, false, 1, -2.5e+3, \"quoted\\nvalue\", 'it''s']\r\n" +
                                   "  yaml11String: ON\r\n" +
                                   "paths: {}\r\n";

            NormalizedSpecBundle result = Normalize(RawYaml);
            string canonical = Encoding.UTF8.GetString(result.Bytes);

            Assert.That(canonical, Does.Contain("\"formatVersion\": 1"));
            Assert.That(canonical, Does.Contain("\"sourcePath\": \"" + SourcePath + "\""));
            Assert.That(canonical, Does.Contain("\"name\": \"openapi\",\n            \"line\": 1,\n            \"column\": 1"));
            Assert.That(canonical, Does.Contain("\"kind\": \"null\""));
            Assert.That(canonical, Does.Contain("\"kind\": \"boolean\""));
            Assert.That(canonical, Does.Contain("\"numberKind\": \"integer\""));
            Assert.That(canonical, Does.Contain("\"numberKind\": \"real\""));
            Assert.That(canonical, Does.Contain("\"value\": \"quoted\\nvalue\""));
            Assert.That(canonical, Does.Contain("\"value\": \"it's\""));
            Assert.That(canonical, Does.Contain("\"value\": \"ON\""));
            Assert.That(canonical, Does.Not.Contain("\r"));
            Assert.That(canonical.Length, Is.GreaterThan(0));
            Assert.That(canonical[0], Is.EqualTo('{'));
        }

        [Test]
        public void NormalizeSupportsLiteralAndFoldedBlockScalarsWithChomping()
        {
            const string RawYaml = "description: |-\n" +
                                   "  first line\n" +
                                   "  second line\n" +
                                   "folded: >\n" +
                                   "  first line\n" +
                                   "  second line\n" +
                                   "next: value\n";

            string canonical = Encoding.UTF8.GetString(Normalize(RawYaml).Bytes);

            Assert.That(canonical, Does.Contain("\"value\": \"first line\\nsecond line\""));
            Assert.That(canonical, Does.Contain("\"value\": \"first line second line\\n\""));
        }

        [Test]
        public void NormalizeIncludesRawBomInHashButNotInCanonicalTree()
        {
            byte[] yaml = Encoding.UTF8.GetBytes("openapi: 3.0.3\npaths: {}\n");
            byte[] withBom = new byte[yaml.Length + 3];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Buffer.BlockCopy(yaml, 0, withBom, 3, yaml.Length);
            var normalizer = new RawYamlNormalizer();

            NormalizedSpecBundle plain = normalizer.Normalize(yaml, SpecId, SourcePath);
            NormalizedSpecBundle bom = normalizer.Normalize(withBom, SpecId, SourcePath);

            Assert.That(bom.RawSha256, Is.Not.EqualTo(plain.RawSha256));
            Assert.That(Encoding.UTF8.GetString(bom.Bytes), Does.Contain("\"name\": \"openapi\""));
            Assert.That(Encoding.UTF8.GetString(plain.Bytes), Does.Contain("\"name\": \"openapi\""));
        }

        [Test]
        public void NormalizeRejectsInvalidUtf8WithYamlDiagnostic()
        {
            var normalizer = new RawYamlNormalizer();

            NormalizedSpecException exception = Assert.Throws<NormalizedSpecException>(
                () => normalizer.Normalize(new byte[] { 0x80 }, SpecId, SourcePath));

            Assert.That(exception.DiagnosticCode, Is.EqualTo("YAML001"));
            Assert.That(exception.SourcePath, Is.EqualTo(SourcePath));
            Assert.That(exception.Line, Is.EqualTo(1));
            Assert.That(exception.Column, Is.EqualTo(1));
            Assert.That(exception.LogicalPath, Is.EqualTo(string.Empty));
            Assert.That(exception.Message, Does.Contain("not valid UTF-8"));
            string location = SourcePath + ":1:1";
            Assert.That(
                CountOccurrences(exception.Message, "YAML001"),
                Is.EqualTo(1),
                "Actual message: " + exception.Message);
            Assert.That(
                CountOccurrences(exception.Message, location),
                Is.EqualTo(1),
                "Actual message: " + exception.Message);
        }

        [Test]
        public void NormalizeIncludesYamlDiagnosticCodeAndLocationOnce()
        {
            var normalizer = new RawYamlNormalizer();

            NormalizedSpecException exception = Assert.Throws<NormalizedSpecException>(
                () => normalizer.Normalize(Encoding.UTF8.GetBytes("\tvalue: 1\n"), SpecId, SourcePath));

            Assert.That(exception.DiagnosticCode, Is.EqualTo("YAML002"));
            Assert.That(exception.SourcePath, Is.EqualTo(SourcePath));
            Assert.That(exception.Line, Is.EqualTo(1));
            Assert.That(exception.Column, Is.EqualTo(1));
            Assert.That(exception.LogicalPath, Is.EqualTo(string.Empty));

            string location = SourcePath + ":1:1";
            Assert.That(CountOccurrences(exception.Message, "YAML002"), Is.EqualTo(1));
            Assert.That(CountOccurrences(exception.Message, location), Is.EqualTo(1));
        }

        [Test]
        public void JsonAndYamlNormalizationProducesTheSameSemanticRoot()
        {
            const string Json = "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1.0.0\"},\"paths\":{\"/items\":{\"get\":{\"responses\":{\"200\":{\"description\":\"OK\"}}}}}}";
            const string Yaml = "openapi: 3.1.0\n" +
                               "info:\n" +
                               "  title: Sample\n" +
                               "  version: 1.0.0\n" +
                               "paths:\n" +
                               "  /items:\n" +
                               "    get:\n" +
                               "      responses:\n" +
                               "        \"200\":\n" +
                               "          description: OK\n";

            NormalizedSpecBundle jsonBundle = new RawJsonNormalizer().Normalize(
                Encoding.UTF8.GetBytes(Json),
                SpecId,
                "Assets/Specs/openapi.json");
            NormalizedSpecBundle yamlBundle = new RawYamlNormalizer().Normalize(
                Encoding.UTF8.GetBytes(Yaml),
                SpecId,
                SourcePath);

            Assert.That(jsonBundle.RawSha256, Is.Not.EqualTo(yamlBundle.RawSha256));
            Assert.That(jsonBundle.SourcePath, Is.Not.EqualTo(yamlBundle.SourcePath));

            AssertSemanticEquivalent(
                StrictJsonSpecParser.Parse(Json, "Assets/Specs/openapi.json"),
                YamlParser.Parse(Yaml, SourcePath));
        }

        private static void AssertSemanticEquivalent(SpecNode expected, SpecNode actual)
        {
            Assert.That(actual.GetType(), Is.EqualTo(expected.GetType()));
            SpecObjectNode expectedObject = expected as SpecObjectNode;
            SpecObjectNode actualObject = actual as SpecObjectNode;
            if (expectedObject != null)
            {
                Assert.That(actualObject.Properties.Count, Is.EqualTo(expectedObject.Properties.Count));
                for (int index = 0; index < expectedObject.Properties.Count; index++)
                {
                    Assert.That(actualObject.Properties[index].Name, Is.EqualTo(expectedObject.Properties[index].Name));
                    AssertSemanticEquivalent(
                        expectedObject.Properties[index].Value,
                        actualObject.Properties[index].Value);
                }

                return;
            }

            SpecArrayNode expectedArray = expected as SpecArrayNode;
            SpecArrayNode actualArray = actual as SpecArrayNode;
            if (expectedArray != null)
            {
                Assert.That(actualArray.Items.Count, Is.EqualTo(expectedArray.Items.Count));
                for (int index = 0; index < expectedArray.Items.Count; index++)
                {
                    AssertSemanticEquivalent(expectedArray.Items[index], actualArray.Items[index]);
                }

                return;
            }

            SpecStringNode expectedString = expected as SpecStringNode;
            SpecStringNode actualString = actual as SpecStringNode;
            if (expectedString != null)
            {
                Assert.That(actualString.Value, Is.EqualTo(expectedString.Value));
                return;
            }

            SpecNumberNode expectedNumber = expected as SpecNumberNode;
            SpecNumberNode actualNumber = actual as SpecNumberNode;
            if (expectedNumber != null)
            {
                Assert.That(actualNumber.IsInteger, Is.EqualTo(expectedNumber.IsInteger));
                Assert.That(actualNumber.Value, Is.EqualTo(expectedNumber.Value));
                return;
            }

            SpecBooleanNode expectedBoolean = expected as SpecBooleanNode;
            SpecBooleanNode actualBoolean = actual as SpecBooleanNode;
            if (expectedBoolean != null)
            {
                Assert.That(actualBoolean.Value, Is.EqualTo(expectedBoolean.Value));
            }
        }

        private static NormalizedSpecBundle Normalize(string rawYaml)
        {
            return new RawYamlNormalizer().Normalize(
                Encoding.UTF8.GetBytes(rawYaml),
                SpecId,
                SourcePath);
        }

        private static int CountOccurrences(string value, string substring)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(substring, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += substring.Length;
            }

            return count;
        }
    }
}
