using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class RawJsonNormalizerTests
    {
        private const string SpecId = "0123456789abcdef0123456789abcdef";
        private const string SourcePath = "Assets/Specs/openapi.json";

        [Test]
        public void NormalizeWritesCanonicalV1WithSourceLocationsAndEveryNodeKind()
        {
            const string RawJson = "{\r\n" +
                                   "  \"openapi\": \"3.0.3\",\r\n" +
                                   "  \"values\": [-0, 1e+02, true, null, \"line\\n雪😀\"]\r\n" +
                                   "}";

            NormalizedSpecBundle result = Normalize(RawJson);
            string canonical = Encoding.UTF8.GetString(result.Bytes);

            Assert.That(result.Bytes.Take(3).ToArray(), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(canonical, Does.Not.Contain("\r"));
            Assert.That(canonical, Does.EndWith("}\n"));
            Assert.That(canonical, Does.Contain("\"formatVersion\": 1"));
            Assert.That(canonical, Does.Contain("\"specId\": \"" + SpecId + "\""));
            Assert.That(canonical, Does.Contain("\"sourcePath\": \"" + SourcePath + "\""));
            Assert.That(canonical, Does.Contain("\"name\": \"openapi\",\n" +
                                                "            \"line\": 2,\n" +
                                                "            \"column\": 3"));
            Assert.That(canonical, Does.Contain("\"kind\": \"string\",\n" +
                                                "              \"line\": 2,\n" +
                                                "              \"column\": 14"));
            Assert.That(canonical, Does.Contain("\"numberKind\": \"integer\",\n" +
                                                "                  \"value\": \"-0\""));
            Assert.That(canonical, Does.Contain("\"numberKind\": \"real\",\n" +
                                                "                  \"value\": \"1e+02\""));
            Assert.That(canonical, Does.Contain("\"value\": true"));
            Assert.That(canonical, Does.Contain("\"kind\": \"null\""));
            Assert.That(canonical, Does.Contain("\"value\": \"line\\n雪😀\""));
        }

        [Test]
        public void NormalizePreservesVeryLargeRfc8259ExponentWithoutNumericConversion()
        {
            const string Number = "-1.234567890123456789e+999999";

            NormalizedSpecBundle result = Normalize("{\"number\":" + Number + "}");
            string canonical = Encoding.UTF8.GetString(result.Bytes);

            Assert.That(canonical, Does.Contain("\"numberKind\": \"real\""));
            Assert.That(canonical, Does.Contain("\"value\": \"" + Number + "\""));
        }

        [Test]
        public void NormalizeIsByteIdenticalForTheSameInputs()
        {
            byte[] rawBytes = Encoding.UTF8.GetBytes("{\"b\":2,\"a\":1}");
            var normalizer = new RawJsonNormalizer();

            byte[] first = normalizer.Normalize(rawBytes, SpecId, SourcePath).Bytes;
            byte[] second = normalizer.Normalize(rawBytes, SpecId, SourcePath).Bytes;

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void NormalizeUsesFixedStringEscapesWithoutUnicodeNormalization()
        {
            const string RawJson = "{\"é/~\":\"é\\\"\\\\\\b\\f\\n\\r\\t\\u0001\"}";

            string canonical = Encoding.UTF8.GetString(Normalize(RawJson).Bytes);

            Assert.That(canonical, Does.Contain("\"name\": \"é/~\""));
            Assert.That(canonical, Does.Contain("\"value\": \"é\\\"\\\\\\b\\f\\n\\r\\t\\u0001\""));
            Assert.That(canonical.IndexOf("\"value\": \"é", StringComparison.Ordinal), Is.EqualTo(-1));
        }

        [Test]
        public void NormalizeIncludesUtf8BomInRawHashButNotInTree()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"value\":1}");
            byte[] withBom = new byte[json.Length + 3];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Buffer.BlockCopy(json, 0, withBom, 3, json.Length);
            var normalizer = new RawJsonNormalizer();

            NormalizedSpecBundle plain = normalizer.Normalize(json, SpecId, SourcePath);
            NormalizedSpecBundle bom = normalizer.Normalize(withBom, SpecId, SourcePath);

            Assert.That(bom.RawSha256, Is.Not.EqualTo(plain.RawSha256));
            Assert.That(Encoding.UTF8.GetString(bom.Bytes), Does.Contain("\"name\": \"value\""));
        }

        [TestCase("{\"value\":1,}", "Trailing commas")]
        [TestCase("{/* comment */\"value\":1}", "comments are not allowed")]
        [TestCase("{\"value\":01}", "Leading zeroes")]
        [TestCase("{}{}", "Only one root")]
        public void NormalizeRejectsNonStrictJson(string rawJson, string expectedMessage)
        {
            NormalizedSpecException exception = Assert.Throws<NormalizedSpecException>(() => Normalize(rawJson));

            Assert.That(exception.Message, Does.Contain(expectedMessage));
            Assert.That(exception.SourcePath, Is.EqualTo(SourcePath));
        }

        [Test]
        public void NormalizeRejectsDuplicatePropertyWithEscapedJsonPointerAndLocation()
        {
            const string RawJson = "{\n  \"a/b~c\": 1,\n  \"a/b~c\": 2\n}";

            NormalizedSpecException exception = Assert.Throws<NormalizedSpecException>(() => Normalize(RawJson));

            Assert.That(exception.LogicalPath, Is.EqualTo("/a~1b~0c"));
            Assert.That(exception.Line, Is.EqualTo(3));
            Assert.That(exception.Column, Is.EqualTo(3));
        }

        [Test]
        public void NormalizeRejectsInvalidUtf8()
        {
            var normalizer = new RawJsonNormalizer();

            NormalizedSpecException exception = Assert.Throws<NormalizedSpecException>(
                () => normalizer.Normalize(new byte[] { 0x7B, 0x80, 0x7D }, SpecId, SourcePath));

            Assert.That(exception.Message, Does.Contain("not valid UTF-8"));
        }

        [Test]
        public void NormalizeRejectsNestingBeyond256Containers()
        {
            string rawJson = new string('[', 257) + "0" + new string(']', 257);

            NormalizedSpecException exception = Assert.Throws<NormalizedSpecException>(() => Normalize(rawJson));

            Assert.That(exception.Message, Does.Contain("maximum of 256"));
        }

        [Test]
        public void NormalizeAcceptsExactly256NestedContainers()
        {
            string rawJson = new string('[', 256) + "0" + new string(']', 256);

            Assert.DoesNotThrow(() => Normalize(rawJson));
        }

        [Test]
        public void NormalizeRequiresLowerCaseGuidNFormatSpecId()
        {
            var normalizer = new RawJsonNormalizer();

            Assert.Throws<ArgumentException>(
                () => normalizer.Normalize(Encoding.UTF8.GetBytes("{}"), SpecId.ToUpperInvariant(), SourcePath));
        }

        private static NormalizedSpecBundle Normalize(string rawJson)
        {
            return new RawJsonNormalizer().Normalize(Encoding.UTF8.GetBytes(rawJson), SpecId, SourcePath);
        }
    }
}
