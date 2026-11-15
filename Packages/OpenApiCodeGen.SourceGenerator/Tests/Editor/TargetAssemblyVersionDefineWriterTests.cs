#nullable enable

using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class TargetAssemblyVersionDefineWriterTests
    {
        const string EntryName = "\"name\": \"jp.rhycol.openapicodegen.source-generator\"";
        const string EntryDefine = "\"define\": \"OPENAPI_CODEGEN_SOURCE_GENERATOR\"";

        string directory = string.Empty;
        string asmdefPath = string.Empty;
        CountingWriter fileWriter = null!;
        TargetAssemblyVersionDefineWriter writer = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(
                Path.GetTempPath(),
                "TargetAssemblyVersionDefineWriterTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            asmdefPath = Path.Combine(directory, "Example.Clients.asmdef");
            fileWriter = new CountingWriter();
            writer = new TargetAssemblyVersionDefineWriter(fileWriter);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void AddsTheEntryWhenVersionDefinesIsMissing()
        {
            Write("{\n  \"name\": \"Example.Clients\",\n  \"references\": [\n    \"Unity.OpenApiCodeGen.SourceGenerator\"\n  ]\n}\n");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            Assert.That(
                Read(),
                Is.EqualTo(
                    "{\n  \"name\": \"Example.Clients\",\n  \"references\": [\n    \"Unity.OpenApiCodeGen.SourceGenerator\"\n  ],\n" +
                    "  \"versionDefines\": [\n    {\n      " + EntryName + ",\n      \"expression\": \"0.0.0\",\n      " +
                    EntryDefine + "\n    }\n  ]\n}\n"));
            Assert.That(fileWriter.WriteCount, Is.EqualTo(1));
        }

        [Test]
        public void IsIdempotent()
        {
            Write("{\n  \"name\": \"Example.Clients\"\n}\n");
            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);
            string afterFirst = Read();

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.False);

            Assert.That(Read(), Is.EqualTo(afterFirst));
            Assert.That(fileWriter.WriteCount, Is.EqualTo(1));
        }

        [Test]
        public void FillsAnEmptyVersionDefinesArray()
        {
            Write("{\n  \"name\": \"Example.Clients\",\n  \"versionDefines\": [],\n  \"noEngineReferences\": true\n}\n");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            Assert.That(
                Read(),
                Is.EqualTo(
                    "{\n  \"name\": \"Example.Clients\",\n  \"versionDefines\": [\n    {\n      " + EntryName +
                    ",\n      \"expression\": \"0.0.0\",\n      " + EntryDefine +
                    "\n    }\n  ],\n  \"noEngineReferences\": true\n}\n"));
        }

        [Test]
        public void AppendsAfterExistingVersionDefinesEntries()
        {
            Write(
                "{\n  \"name\": \"Example.Clients\",\n  \"versionDefines\": [\n    {\n      \"name\": \"com.unity.test-framework\",\n" +
                "      \"expression\": \"1.0.0\",\n      \"define\": \"OPENAPI_CODEGEN_TEST_FRAMEWORK\"\n    }\n  ]\n}\n");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            Assert.That(
                Read(),
                Is.EqualTo(
                    "{\n  \"name\": \"Example.Clients\",\n  \"versionDefines\": [\n    {\n      \"name\": \"com.unity.test-framework\",\n" +
                    "      \"expression\": \"1.0.0\",\n      \"define\": \"OPENAPI_CODEGEN_TEST_FRAMEWORK\"\n    },\n    {\n      " +
                    EntryName + ",\n      \"expression\": \"0.0.0\",\n      " + EntryDefine + "\n    }\n  ]\n}\n"));
        }

        [Test]
        public void TreatsAnEntryWithAnotherExpressionAsExisting()
        {
            string original =
                "{\n  \"name\": \"Example.Clients\",\n  \"versionDefines\": [\n    {\n      " + EntryName +
                ",\n      \"expression\": \"[1.0.0,2.0.0)\",\n      " + EntryDefine + "\n    }\n  ]\n}\n";
            Write(original);

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.False);

            Assert.That(Read(), Is.EqualTo(original));
            Assert.That(fileWriter.WriteCount, Is.EqualTo(0));
        }

        [Test]
        public void KeepsUnknownFieldsCrLfAndBom()
        {
            string original =
                "{\r\n    \"name\": \"Example.Clients\",\r\n    \"x-custom\": { \"nested\": [1, 2, { \"a\": \"}]\" }] },\r\n" +
                "    \"references\": [\"Unity.OpenApiCodeGen.SourceGenerator\"]\r\n}\r\n";
            WriteBytes(new UTF8Encoding(true).GetPreamble(), original);

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            byte[] bytes = File.ReadAllBytes(asmdefPath);
            Assert.That(bytes[0], Is.EqualTo(0xEF));
            Assert.That(bytes[1], Is.EqualTo(0xBB));
            Assert.That(bytes[2], Is.EqualTo(0xBF));
            string text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            string withoutClosingBrace = original.Substring(
                0,
                original.LastIndexOf("\r\n}", StringComparison.Ordinal));
            Assert.That(text, Does.StartWith(withoutClosingBrace + ",\r\n    \"versionDefines\": ["));
            Assert.That(text, Does.Contain("\"x-custom\": { \"nested\": [1, 2, { \"a\": \"}]\" }] }"));
            Assert.That(text.Replace("\r\n", string.Empty), Does.Not.Contain("\n"));
            Assert.That(text, Does.EndWith("\r\n}\r\n"));
        }

        [Test]
        public void KeepsLfWhenTheFileUsesLfAndHasNoBom()
        {
            Write("{\n  \"name\": \"Example.Clients\"\n}\n");

            writer.EnsureVersionDefine(asmdefPath);

            byte[] bytes = File.ReadAllBytes(asmdefPath);
            Assert.That(bytes[0], Is.EqualTo((byte)'{'));
            Assert.That(Read(), Does.Not.Contain("\r"));
        }

        [Test]
        public void HandlesAnEmptyRootObject()
        {
            Write("{}");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            Assert.That(
                Read(),
                Is.EqualTo(
                    "{\n  \"versionDefines\": [\n    {\n      " + EntryName + ",\n      \"expression\": \"0.0.0\",\n      " +
                    EntryDefine + "\n    }\n  ]\n}"));
        }

        [TestCase("{ \"name\": \"Example.Clients\",")]
        [TestCase("[1, 2]")]
        [TestCase("{ \"name\": \"Example.Clients\", \"versionDefines\": {} }")]
        [TestCase("{ \"name\": \"Example.Clients\", \"versionDefines\": [1] }")]
        [TestCase("{ // comment\n \"name\": \"Example.Clients\" }")]
        public void RefusesToRewriteAnAsmdefThatCannotBeParsedAndShowsTheManualEntry(string content)
        {
            Write(content);

            SafeGenerationException exception = Assert.Throws<SafeGenerationException>(
                () => writer.EnsureVersionDefine(asmdefPath))!;

            Assert.That(exception.Message, Does.Contain(TargetAssemblyVersionDefineWriter.ManualEntryJson));
            Assert.That(Read(), Is.EqualTo(content));
            Assert.That(fileWriter.WriteCount, Is.EqualTo(0));
        }

        [Test]
        public void IgnoresAStringValueThatEqualsTheKeyName()
        {
            Write("{\n  \"name\": \"versionDefines\",\n  \"references\": [\"versionDefines\"]\n}\n");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            SpecObjectNode root = ParseRoot();
            Assert.That(StringOf(root, "name"), Is.EqualTo("versionDefines"));
            Assert.That(ArrayOf(root, "versionDefines").Items, Has.Count.EqualTo(1));
            Assert.That(ArrayOf(root, "references").Items, Has.Count.EqualTo(1));
        }

        [Test]
        public void IgnoresVersionDefinesInsideANestedObjectWhenTheRootHasNone()
        {
            Write("{\n  \"name\": \"x\",\n  \"x-nested\": { \"versionDefines\": [ { \"name\": \"a\" } ] }\n}\n");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            SpecObjectNode root = ParseRoot();
            Assert.That(ArrayOf(root, "versionDefines").Items, Has.Count.EqualTo(1));
            var nested = (SpecObjectNode)root.Properties[1].Value;
            Assert.That(ArrayOf(nested, "versionDefines").Items, Has.Count.EqualTo(1));
        }

        [Test]
        public void ChangesOnlyTheRootVersionDefinesWhenANestedOneComesFirst()
        {
            Write(
                "{\n  \"x-nested\": { \"versionDefines\": [] },\n  \"name\": \"x\",\n" +
                "  \"versionDefines\": [\n    { \"name\": \"a\", \"expression\": \"1.0.0\", \"define\": \"A\" }\n  ]\n}\n");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            SpecObjectNode root = ParseRoot();
            Assert.That(ArrayOf(root, "versionDefines").Items, Has.Count.EqualTo(2));
            Assert.That(ArrayOf((SpecObjectNode)root.Properties[0].Value, "versionDefines").Items, Is.Empty);
            Assert.That(StringOf(root, "name"), Is.EqualTo("x"));
        }

        [Test]
        public void AppendsToASingleLineVersionDefinesArray()
        {
            Write(
                "{\n  \"name\": \"x\",\n" +
                "  \"versionDefines\": [{ \"name\": \"a\", \"expression\": \"1.0.0\", \"define\": \"A\" }]\n}\n");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            SpecObjectNode root = ParseRoot();
            SpecArrayNode array = ArrayOf(root, "versionDefines");
            Assert.That(array.Items, Has.Count.EqualTo(2));
            Assert.That(StringOf((SpecObjectNode)array.Items[0], "define"), Is.EqualTo("A"));
            Assert.That(
                StringOf((SpecObjectNode)array.Items[1], "define"),
                Is.EqualTo(TargetAssemblyVersionDefineWriter.Define));
            Assert.That(StringOf(root, "name"), Is.EqualTo("x"));
        }

        [Test]
        public void FollowsTabIndentation()
        {
            Write("{\n\t\"name\": \"x\"\n}\n");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            Assert.That(
                Read(),
                Is.EqualTo(
                    "{\n\t\"name\": \"x\",\n\t\"versionDefines\": [\n\t\t{\n\t\t\t" + EntryName +
                    ",\n\t\t\t\"expression\": \"0.0.0\",\n\t\t\t" + EntryDefine + "\n\t\t}\n\t]\n}\n"));
            Assert.That(ArrayOf(ParseRoot(), "versionDefines").Items, Has.Count.EqualTo(1));
        }

        [Test]
        public void KeepsStringsThatContainEscapedQuotesAndBrackets()
        {
            Write(
                "{\n  \"name\": \"a\\\"]}b\",\n  \"x-s\": \"\\\\\",\n  \"references\": [\"]}\\\"\", \"[{\"]\n}\n");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            SpecObjectNode root = ParseRoot();
            Assert.That(StringOf(root, "name"), Is.EqualTo("a\"]}b"));
            Assert.That(StringOf(root, "x-s"), Is.EqualTo("\\"));
            Assert.That(ArrayOf(root, "references").Items, Has.Count.EqualTo(2));
            Assert.That(ArrayOf(root, "versionDefines").Items, Has.Count.EqualTo(1));
        }

        [Test]
        public void AppendsWhenVersionDefinesIsNotTheLastRootKey()
        {
            Write(
                "{\n  \"versionDefines\": [\n    { \"name\": \"a\", \"expression\": \"1.0.0\", \"define\": \"A\" }\n  ],\n" +
                "  \"name\": \"x\"\n}\n");

            Assert.That(writer.EnsureVersionDefine(asmdefPath), Is.True);

            SpecObjectNode root = ParseRoot();
            Assert.That(ArrayOf(root, "versionDefines").Items, Has.Count.EqualTo(2));
            Assert.That(StringOf(root, "name"), Is.EqualTo("x"));
            Assert.That(root.Properties, Has.Count.EqualTo(2));
        }

        SpecObjectNode ParseRoot()
        {
            return (SpecObjectNode)StrictJsonSpecParser.Parse(Read(), asmdefPath);
        }

        static SpecArrayNode ArrayOf(SpecObjectNode node, string name)
        {
            foreach (SpecProperty property in node.Properties)
            {
                if (property.Name == name)
                {
                    return (SpecArrayNode)property.Value;
                }
            }

            throw new AssertionException("Missing property " + name);
        }

        static string StringOf(SpecObjectNode node, string name)
        {
            foreach (SpecProperty property in node.Properties)
            {
                if (property.Name == name)
                {
                    return ((SpecStringNode)property.Value).Value;
                }
            }

            throw new AssertionException("Missing property " + name);
        }

        void Write(string content)
        {
            WriteBytes(new byte[0], content);
        }

        void WriteBytes(byte[] preamble, string content)
        {
            byte[] body = new UTF8Encoding(false).GetBytes(content);
            byte[] all = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, all, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, all, preamble.Length, body.Length);
            File.WriteAllBytes(asmdefPath, all);
        }

        string Read()
        {
            return new UTF8Encoding(false).GetString(File.ReadAllBytes(asmdefPath));
        }

        sealed class CountingWriter : IAtomicFileWriter
        {
            readonly AtomicFileWriter inner = new AtomicFileWriter();

            internal int WriteCount { get; private set; }

            public void WriteAllBytesAtomically(string path, byte[] bytes)
            {
                WriteCount++;
                inner.WriteAllBytesAtomically(path, bytes);
            }
        }
    }
}
