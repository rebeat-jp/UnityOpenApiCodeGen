using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Several clients may share one namespace. Each client declares its own PreserveAttribute (in a namespace of its
    /// own), so the declarations must not collide, whatever transport and serializer the clients use.
    /// </summary>
    public sealed class PreserveMultiClientTests
    {
        private const string FirstId = "0123456789abcdef0123456789abcdef";
        private const string SecondId = "fedcba9876543210fedcba9876543210";
        private const string ThirdId = "00112233445566778899aabbccddeeff";

        private static string Document(string paths, string schemas) =>
            "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"servers\":[{\"url\":\"https://x.test\"}]," +
            "\"paths\":{" + paths + "},\"components\":{\"schemas\":{" + schemas + "}}}";

        private static string Get(string path, string operation, string schema) =>
            "\"" + path + "\":{\"get\":{\"operationId\":\"" + operation + "\",\"responses\":{\"200\":{\"description\":\"ok\"," +
            "\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/" + schema + "\"}}}}}}}";

        // Plain DTO and enum.
        private static readonly string Plain = Document(
            Get("/a", "getAlpha", "Alpha"),
            "\"Tint\":{\"type\":\"string\",\"enum\":[\"red\",\"blue\"]}," +
            "\"Alpha\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"tint\":{\"$ref\":\"#/components/schemas/Tint\"}}}");

        // A second plain spec with different type names.
        private static readonly string Other = Document(
            Get("/b", "getBeta", "Beta"),
            "\"Beta\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"n\"],\"properties\":{\"n\":{\"type\":\"integer\"}}}");

        // The conditions that already produced a PreserveAttribute before: union, open DTO, readOnly property.
        private static readonly string Special = Document(
            Get("/p", "getPet", "Pet") + "," + Get("/o", "getOpen", "Open") + "," + Get("/r", "getRo", "Ro"),
            "\"Cat\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"meow\":{\"type\":\"boolean\"}}}," +
            "\"Dog\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"bark\":{\"type\":\"string\"}}}," +
            "\"Pet\":{\"oneOf\":[{\"$ref\":\"#/components/schemas/Cat\"},{\"$ref\":\"#/components/schemas/Dog\"}]," +
            "\"discriminator\":{\"propertyName\":\"kind\"}}," +
            "\"Open\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}," +
            "\"Ro\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"id\":{\"type\":\"integer\",\"readOnly\":true}}}");

        private static string Definition(string specId, string name, string extra) =>
            "[global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute(\"" + specId + "\", \"" + name +
            "\", \"Shared\", global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat.Json" + extra + ")]\n" +
            "public partial class " + name + " { }\n";

        private const string Uwr = ", HttpTransport = global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiHttpTransport.UnityWebRequest";
        private const string Stj = ", JsonSerializer = global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiJsonSerializer.SystemTextJson";

        private static void AssertCompiles(string firstExtra, string secondExtra, string thirdExtra, string first, string second, string third)
        {
            string source = Phase4GeneratorTestHarness.AttributeContract + "namespace Shared\n{\n" +
                Definition(FirstId, "FirstApi", firstExtra) +
                Definition(SecondId, "SecondApi", secondExtra) +
                Definition(ThirdId, "ThirdApi", thirdExtra) + "}\n";
            CSharpCompilation compilation = CSharpCompilation.Create(
                "SharedNamespace",
                new[] { CSharpSyntaxTree.ParseText(source) },
                Phase4GeneratorTestHarness.CreateCompilation(
                    options: new Phase4CompilationOptions { UnityWebRequestStub = true }).References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = Phase4GeneratorTestHarness.CreateDriver(
                compilation,
                ImmutableArray.Create<AdditionalText>(
                    Phase4GeneratorTestHarness.CreateBundleText(FirstId, TestBundleFactory.Create(first, specId: FirstId)),
                    Phase4GeneratorTestHarness.CreateBundleText(SecondId, TestBundleFactory.Create(second, specId: SecondId)),
                    Phase4GeneratorTestHarness.CreateBundleText(ThirdId, TestBundleFactory.Create(third, specId: ThirdId))));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

            Assert.Empty(generatorDiagnostics.Where(static item => item.Severity == DiagnosticSeverity.Error));
            Diagnostic[] errors = output.GetDiagnostics().Where(static item => item.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.True(errors.Length == 0, string.Join("\n", errors.Select(static item => item.ToString())));
            Assert.Equal(3, output.GetSymbolsWithName("PreserveAttribute", SymbolFilter.Type).Count());
        }

        [Fact]
        public void PlainClientsMixingTransportsAndSerializersShareANamespace()
        {
            AssertCompiles(string.Empty, Stj, Uwr + Stj, Plain, Other, Special);
        }

        [Fact]
        public void UnionOpenAndReadOnlyClientsShareANamespace()
        {
            AssertCompiles(Uwr, string.Empty, Stj, Special, Other, Plain);
            AssertCompiles(string.Empty, Stj, Uwr, Plain, Special, Other);
        }
    }
}
