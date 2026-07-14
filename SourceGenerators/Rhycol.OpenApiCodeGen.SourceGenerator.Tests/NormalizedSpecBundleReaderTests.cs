using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        public void Read_ReportsUnsupportedVersionTokenLocationAfterWhitespace()
        {
            const string Bundle = "{\r\n  \"formatVersion\":\r\n    2";

            UnsupportedNormalizedSpecVersionException exception =
                Assert.Throws<UnsupportedNormalizedSpecVersionException>(
                    () => NormalizedSpecBundleReader.Read(Bundle));

            Assert.Equal(3, exception.Line);
            Assert.Equal(5, exception.Column);
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
