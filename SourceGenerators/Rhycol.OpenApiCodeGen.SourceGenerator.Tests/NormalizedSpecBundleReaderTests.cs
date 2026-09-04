using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class NormalizedSpecBundleReaderTests
    {
        [Fact]
        public void Read_PreservesNodeKindsNumberLexemesAndLogicalPaths()
        {
            const string Json = "{\"a/b~c\":[-0,1e+02,-1.234567890123456789e+999999,true,false,null,\"text\"]}";

            NormalizedSpecBundle bundle = NormalizedSpecBundleReader.Read(TestBundleFactory.Create(Json));
            SpecNode array = GetProperty(bundle.Root, "a/b~c");
            SpecNode[] items = array.EnumerateArray().ToArray();

            Assert.Equal("/a~1b~0c", array.LogicalPath);
            Assert.Equal("/a~1b~0c/0", items[0].LogicalPath);
            Assert.Equal("-0", items[0].GetRawJson());
            Assert.True(items[0].IsInteger);
            Assert.Equal("1e+02", items[1].GetRawJson());
            Assert.False(items[1].IsInteger);
            Assert.Equal("-1.234567890123456789e+999999", items[2].GetRawJson());
            Assert.Equal(SpecValueKind.True, items[3].ValueKind);
            Assert.Equal(SpecValueKind.False, items[4].ValueKind);
            Assert.Equal(SpecValueKind.Null, items[5].ValueKind);
            Assert.Equal("text", items[6].GetString());
        }

        [Fact]
        public void Read_RejectsUnknownOrOutOfOrderFields()
        {
            string bundle = TestBundleFactory.Create("{}");
            bundle = bundle.Replace(
                "\"specId\":",
                "\"unexpected\":0,\"specId\":");

            NormalizedSpecBundleFormatException exception =
                Assert.Throws<NormalizedSpecBundleFormatException>(() => NormalizedSpecBundleReader.Read(bundle));

            Assert.Contains("Expected field 'specId'", exception.Message);
        }

        [Fact]
        public void Read_RejectsNumberKindThatDoesNotMatchLexeme()
        {
            string bundle = TestBundleFactory.Create("1").Replace(
                "\"numberKind\":\"integer\"",
                "\"numberKind\":\"real\"");

            NormalizedSpecBundleFormatException exception =
                Assert.Throws<NormalizedSpecBundleFormatException>(() => NormalizedSpecBundleReader.Read(bundle));

            Assert.Contains("does not match", exception.Message);
        }

        [Fact]
        public void Read_RejectsEmptySourcePath()
        {
            string bundle = TestBundleFactory.Create("{}", sourcePath: string.Empty);

            NormalizedSpecBundleFormatException exception =
                Assert.Throws<NormalizedSpecBundleFormatException>(() => NormalizedSpecBundleReader.Read(bundle));

            Assert.Contains("'sourcePath' must not be empty", exception.Message);
        }

        [Theory]
        [InlineData(@"Assets\Specs\openapi.json")]
        [InlineData("../openapi.json")]
        [InlineData("./openapi.json")]
        [InlineData("Assets//Specs/openapi.json")]
        [InlineData("C:Specs/openapi.json")]
        public void Read_RejectsNonNormalizedSourcePath(string sourcePath)
        {
            string bundle = TestBundleFactory.Create("{}", sourcePath: sourcePath);

            NormalizedSpecBundleFormatException exception =
                Assert.Throws<NormalizedSpecBundleFormatException>(() => NormalizedSpecBundleReader.Read(bundle));

            Assert.Contains("normalized project-relative or absolute path", exception.Message);
        }

        [Theory]
        [InlineData("Assets/Specs/openapi.json")]
        [InlineData("/Users/example/openapi.json")]
        [InlineData("C:/Specs/openapi.json")]
        [InlineData("//server/share/openapi.json")]
        public void Read_AcceptsNormalizedSourcePath(string sourcePath)
        {
            NormalizedSpecBundle bundle = NormalizedSpecBundleReader.Read(
                TestBundleFactory.Create("{}", sourcePath: sourcePath));

            Assert.Equal(sourcePath, bundle.SourcePath);
        }

        [Fact]
        public void Read_Allows256NestedContainers()
        {
            string root = CreateNestedArrayNode(256);

            NormalizedSpecBundle bundle = NormalizedSpecBundleReader.Read(
                TestBundleFactory.CreateWithEncodedRoot(root));

            Assert.Equal(SpecValueKind.Array, bundle.Root.ValueKind);
        }

        [Fact]
        public void Read_Rejects257NestedContainers()
        {
            string root = CreateNestedArrayNode(257);

            NormalizedSpecBundleFormatException exception = Assert.Throws<NormalizedSpecBundleFormatException>(
                () => NormalizedSpecBundleReader.Read(TestBundleFactory.CreateWithEncodedRoot(root)));

            Assert.Contains("maximum of 256", exception.Message);
        }

        [Fact]
        public void Read_ReportsMalformedV2TokenAfterWhitespace()
        {
            const string Bundle = "{\r\n  \"formatVersion\":\r\n    2";

            NormalizedSpecBundleFormatException exception =
                Assert.Throws<NormalizedSpecBundleFormatException>(
                    () => NormalizedSpecBundleReader.Read(Bundle));

            Assert.Equal(3, exception.Line);
            Assert.Equal(6, exception.Column);
        }

        [Fact]
        public void Read_CountsCrLfAsOneLineForMalformedDiagnostics()
        {
            const string Bundle = "{\r\n  \"formatVersion\": 1,\r\n  \"wrong\": 0";

            NormalizedSpecBundleFormatException exception =
                Assert.Throws<NormalizedSpecBundleFormatException>(
                    () => NormalizedSpecBundleReader.Read(Bundle));

            Assert.Equal(3, exception.Line);
        }

        [Fact]
        public void Read_AcceptsStrictV2BundleWithExternalDocumentAndReferenceEdge()
        {
            const string ExternalDocumentId = "Assets/Specs/pet.json";
            const string ReferencePointer =
                "/paths/~1pets/get/responses/200/content/application~1json/schema/$ref";
            string root = CreateExternalReferenceRoot("pet.json#/components/schemas/Pet");
            string external = "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Pet\",\"version\":\"1\"},\"paths\":{},\"components\":{\"schemas\":{\"Pet\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}}}}";
            string content = TestBundleFactory.CreateV2(
                root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        ExternalDocumentId,
                        "Assets/Specs/pet.json",
                        "json",
                        external),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        ReferencePointer,
                        ExternalDocumentId,
                        "/components/schemas/Pet"),
                });

            NormalizedSpecBundle bundle = NormalizedSpecBundleReader.Read(content);

            Assert.Equal(2, bundle.FormatVersion);
            Assert.Equal(2, bundle.Documents.Count);
            Assert.Equal("root", bundle.Documents[0].DocumentId);
            Assert.Equal(ExternalDocumentId, bundle.Documents[1].DocumentId);
            Assert.Equal("json", bundle.Documents[1].Format);
            Assert.Single(bundle.ReferenceEdges);
            Assert.Equal(ReferencePointer, bundle.ReferenceEdges[0].SourcePointer);
            Assert.Equal("/components/schemas/Pet", bundle.ReferenceEdges[0].TargetPointer);
        }

        [Fact]
        public void Read_RejectsV2WhenTopRawSha256DoesNotMatchRootDocument()
        {
            string content = TestBundleFactory.CreateV2(
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1\"},\"paths\":{}}",
                Array.Empty<TestBundleFactory.V2DocumentSpec>(),
                Array.Empty<TestBundleFactory.V2ReferenceSpec>(),
                topRawSha256: "0000000000000000000000000000000000000000000000000000000000000000");

            NormalizedSpecBundleFormatException exception = Assert.Throws<NormalizedSpecBundleFormatException>(
                () => NormalizedSpecBundleReader.Read(content));

            Assert.Contains("top-level 'rawSha256' must match", exception.Message);
        }

        [Fact]
        public void Read_RejectsV2BundleWithUnknownTopLevelField()
        {
            string content = TestBundleFactory.CreateV2(
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1\"},\"paths\":{}}",
                Array.Empty<TestBundleFactory.V2DocumentSpec>(),
                Array.Empty<TestBundleFactory.V2ReferenceSpec>());
            content = content.Replace(
                "\"specId\":",
                "\"unexpected\":0,\"specId\":");

            NormalizedSpecBundleFormatException exception = Assert.Throws<NormalizedSpecBundleFormatException>(
                () => NormalizedSpecBundleReader.Read(content));

            Assert.Contains("Expected field 'specId'", exception.Message);
        }

        [Fact]
        public void Read_RejectsV2ExternalDocumentsThatAreNotSortedByDocumentId()
        {
            const string FirstDocumentId = "Assets/Specs/b.json";
            const string SecondDocumentId = "Assets/Specs/a.json";
            string content = TestBundleFactory.CreateV2(
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1\"},\"paths\":{}}",
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        FirstDocumentId,
                        "Assets/Specs/b.json",
                        "json",
                        "{\"type\":\"string\"}"),
                    new TestBundleFactory.V2DocumentSpec(
                        SecondDocumentId,
                        "Assets/Specs/a.json",
                        "json",
                        "{\"type\":\"string\"}"),
                },
                Array.Empty<TestBundleFactory.V2ReferenceSpec>());

            NormalizedSpecBundleFormatException exception = Assert.Throws<NormalizedSpecBundleFormatException>(
                () => NormalizedSpecBundleReader.Read(content));

            Assert.Contains("sorted by documentId", exception.Message);
        }

        [Fact]
        public void Read_RejectsV2ReferenceWithoutAnEdge()
        {
            string content = TestBundleFactory.CreateV2(
                CreateExternalReferenceRoot("pet.json#/components/schemas/Pet"),
                Array.Empty<TestBundleFactory.V2DocumentSpec>(),
                Array.Empty<TestBundleFactory.V2ReferenceSpec>());

            NormalizedSpecBundleFormatException exception = Assert.Throws<NormalizedSpecBundleFormatException>(
                () => NormalizedSpecBundleReader.Read(content));

            Assert.Contains("has no reference edge", exception.Message);
        }

        [Fact]
        public void Read_RejectsRemoteDocumentWithLocalPathDocumentId()
        {
            string content = TestBundleFactory.CreateV2(
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1\"},\"paths\":{}}",
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        "Assets/Specs/pet.json",
                        "https://example.com/pet.json",
                        "json",
                        "{\"type\":\"string\"}"),
                },
                Array.Empty<TestBundleFactory.V2ReferenceSpec>());

            NormalizedSpecBundleFormatException exception = Assert.Throws<NormalizedSpecBundleFormatException>(
                () => NormalizedSpecBundleReader.Read(content));

            Assert.Contains("remote external document", exception.Message);
        }

        [Theory]
        [InlineData("/Users/example/pet.json")]
        [InlineData("//server/share/pet.json")]
        [InlineData("C:/Specs/pet.json")]
        public void Read_RejectsAbsoluteLocalExternalSourcePath(string sourcePath)
        {
            string content = TestBundleFactory.CreateV2(
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1\"},\"paths\":{}}",
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        sourcePath,
                        sourcePath,
                        "json",
                        "{\"type\":\"string\"}"),
                },
                Array.Empty<TestBundleFactory.V2ReferenceSpec>());

            NormalizedSpecBundleFormatException exception = Assert.Throws<NormalizedSpecBundleFormatException>(
                () => NormalizedSpecBundleReader.Read(content));

            Assert.Contains("project-relative", exception.Message);
        }

        [Fact]
        public void Read_UsesDocumentAndPointerTupleForPrefixCollidingEdges()
        {
            string content = TestBundleFactory.CreateV2(
                "{\"type\":\"object\"}",
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        "Assets/a",
                        "Assets/a",
                        "json",
                        "{\"b\":{\"$ref\":\"Assets/a/b\"}}"),
                    new TestBundleFactory.V2DocumentSpec(
                        "Assets/a/b",
                        "Assets/a/b",
                        "json",
                        "{\"$ref\":\"Assets/a\"}"),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "Assets/a",
                        "/b/$ref",
                        "Assets/a/b",
                        string.Empty),
                    new TestBundleFactory.V2ReferenceSpec(
                        "Assets/a/b",
                        "/$ref",
                        "Assets/a",
                        string.Empty),
                });

            NormalizedSpecBundle bundle = NormalizedSpecBundleReader.Read(content);

            Assert.Equal(2, bundle.ReferenceEdges.Count);
        }

        private static string CreateExternalReferenceRoot(string reference)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1\"},\"paths\":{\"/pets\":{\"get\":{\"operationId\":\"getPet\",\"responses\":{\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":" +
                   JsonSerializer.Serialize(reference) +
                   "}}}}}}}},\"components\":{}}";
        }

        private static SpecNode GetProperty(SpecNode node, string name)
        {
            Assert.True(node.TryGetProperty(name, out SpecNode value));
            return value;
        }

        private static string CreateNestedArrayNode(int depth)
        {
            var builder = new StringBuilder("{\"kind\":\"null\",\"line\":1,\"column\":1}");
            for (int index = 0; index < depth; index++)
            {
                builder.Insert(0, "{\"kind\":\"array\",\"line\":1,\"column\":1,\"items\":[");
                builder.Append("]}");
            }

            return builder.ToString();
        }
    }
}
