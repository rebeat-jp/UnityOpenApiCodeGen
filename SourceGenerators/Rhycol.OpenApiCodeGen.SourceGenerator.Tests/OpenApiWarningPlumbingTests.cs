using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class OpenApiWarningPlumbingTests
    {
        private const string MinimalSpec = @"{
  ""openapi"": ""3.0.3"",
  ""info"": { ""title"": ""t"", ""version"": ""1"" },
  ""paths"": {
    ""/ping"": { ""get"": { ""operationId"": ""ping"", ""responses"": { ""204"": { ""description"": ""ok"" } } } }
  }
}";

        [Fact]
        public void CollectorKeepsInsertionOrderAndDropsDuplicates()
        {
            var collector = new OpenApiWarningCollector();
            var first = new OpenApiSourceLocation(3, 5, "/paths/~1a");
            var second = new OpenApiSourceLocation(9, 1, "/paths/~1b");

            collector.Add(first, "x");
            collector.Add(second, "y");
            collector.Add(first, "x");

            IReadOnlyList<OpenApiSemanticWarning> warnings = collector.ToList();
            Assert.Equal(2, warnings.Count);
            Assert.Equal("/paths/~1a", warnings[0].Location.LogicalPath);
            Assert.Equal("y", warnings[1].Message);
        }

        [Fact]
        public void ParsingValidSpecProducesNoWarningsAndSemanticDefaults()
        {
            OpenApiSemanticDocument document = OpenApiSemanticParser.Parse(
                TestBundleFactory.ReadRoot(MinimalSpec));

            Assert.Empty(document.Warnings);
            OpenApiGenerationModel model = OpenApiGenerationModelBuilder.Build(
                document,
                new GeneratorOptions("Api", "Generated.Warn"));
            Assert.Empty(model.Warnings);
        }

        [Fact]
        public void WarningsFlowFromSemanticDocumentToOacg108Diagnostic()
        {
            OpenApiSemanticDocument parsed = OpenApiSemanticParser.Parse(
                TestBundleFactory.ReadRoot(MinimalSpec));
            var location = new OpenApiSourceLocation(
                "Assets/Specs/other.json",
                "Assets/Specs/other.json",
                12,
                7,
                "/paths/~1ping/get/parameters/0");
            var document = new OpenApiSemanticDocument(
                parsed.MinorVersion,
                parsed.BaseUrl,
                parsed.Schemas,
                parsed.Operations,
                parsed.Location,
                new[] { new OpenApiSemanticWarning(location, "header 'Accept' is ignored") });

            OpenApiGenerationModel model = OpenApiGenerationModelBuilder.Build(
                document,
                new GeneratorOptions("Api", "Generated.Warn"));
            Assert.Single(model.Warnings);

            Diagnostic diagnostic = Assert.Single(
                ApiClientCodeGenerator.CreateIgnoredElementDiagnostics(
                    model.Warnings,
                    "Assets/Specs/openapi.json"));
            Assert.Equal("OACG108", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Contains("/paths/~1ping/get/parameters/0", diagnostic.GetMessage());
            Assert.Contains("header 'Accept' is ignored", diagnostic.GetMessage());
            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            Assert.Equal("Assets/Specs/other.json", span.Path);
            Assert.Equal(11, span.StartLinePosition.Line);
            Assert.Equal(6, span.StartLinePosition.Character);
        }


        private static ApiClientCodeGenerator CreateInjectingGenerator(int repeat = 1)
        {
            return new ApiClientCodeGenerator((parser, root) =>
            {
                Assert.True(root.TryGetProperty("paths", out SpecNode paths));
                for (int index = 0; index < repeat; index++)
                {
                    parser.AddIgnoredWarning(paths, "injected ignored element");
                }
            });
        }

        [Fact]
        public void InjectedWarningIsReportedAsOacg108AndDoesNotChangeGeneratedSource()
        {
            Phase4GeneratorExecution plain = Phase4GeneratorTestHarness.GenerateAndCompile(MinimalSpec);
            Phase4GeneratorExecution injected = Phase4GeneratorTestHarness.GenerateAndCompile(
                MinimalSpec,
                generator: CreateInjectingGenerator());

            Assert.Empty(plain.RunResult.Diagnostics);
            Diagnostic warning = Assert.Single(injected.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("/paths", warning.GetMessage());
            Assert.Contains("injected ignored element", warning.GetMessage());
            Assert.Equal(plain.GeneratedSource, injected.GeneratedSource);
            Assert.Empty(injected.CompilationErrors);
        }

        [Fact]
        public void DuplicateWarningsWithSameLocationAndMessageAreReportedOnce()
        {
            Phase4GeneratorExecution injected = Phase4GeneratorTestHarness.GenerateAndCompile(
                MinimalSpec,
                generator: CreateInjectingGenerator(repeat: 3));

            Assert.Single(injected.RunResult.Diagnostics);
        }

        [Fact]
        public void FatalErrorReportsOnlyTheErrorAndDropsCollectedWarnings()
        {
            // An unsupported components section is rejected (OACG101) after the injector already
            // collected a warning.
            string spec = MinimalSpec.Replace(
                "\"paths\"",
                "\"components\": { \"foo\": {} }, \"paths\"");

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec,
                generator: CreateInjectingGenerator());

            Diagnostic error = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG101", error.Id);
            Assert.DoesNotContain(execution.RunResult.Diagnostics, static d => d.Id == "OACG108");
        }

        [Fact]
        public void ConflictingEnumValuesAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new OpenApiSemanticSchema(
                OpenApiSemanticSchemaKind.Enum,
                "E",
                string.Empty,
                false,
                string.Empty,
                Array.Empty<OpenApiSemanticProperty>(),
                null,
                new[] { "a" },
                new OpenApiSourceLocation(1, 1, "/x"),
                typedEnumValues: new[]
                {
                    new OpenApiSemanticLiteral(OpenApiSemanticLiteralKind.Integer, "1")
                }));
            Assert.Throws<ArgumentException>(() => new GeneratedEnumMemberModel(
                "One",
                "a",
                new OpenApiSemanticLiteral(OpenApiSemanticLiteralKind.Integer, "1")));
        }

        [Fact]
        public void GeneratedTypeModelComparesMapAndUnionPartsAndKeepsThemOnWithNullable()
        {
            var inner = new GeneratedTypeModel(GeneratedTypeKind.Int32);
            var map = new GeneratedTypeModel(GeneratedTypeKind.Map, valueType: inner);
            var sameMap = new GeneratedTypeModel(GeneratedTypeKind.Map, valueType: inner);
            var otherMap = new GeneratedTypeModel(
                GeneratedTypeKind.Map,
                valueType: new GeneratedTypeModel(GeneratedTypeKind.String));
            var union = new GeneratedTypeModel(
                GeneratedTypeKind.Union,
                name: "U",
                unionMembers: new[] { inner, new GeneratedTypeModel(GeneratedTypeKind.String) });

            Assert.Equal(map, sameMap);
            Assert.Equal(map.GetHashCode(), sameMap.GetHashCode());
            Assert.NotEqual(map, otherMap);
            Assert.Equal(2, union.WithNullable(true).UnionMembers.Count);
            Assert.Same(inner, map.WithNullable(true).ValueType);
            Assert.All(
                new[] { GeneratedTypeKind.Any, GeneratedTypeKind.Map, GeneratedTypeKind.Union, GeneratedTypeKind.Binary },
                static kind => Assert.False(new GeneratedTypeModel(kind).IsValueType));
        }

        [Fact]
        public void AnnotationsCompareByValueAndDistinguishUnspecifiedFromExplicitNull()
        {
            var a = new OpenApiSemanticAnnotations("d", true, false, false, null, null);
            var b = new OpenApiSemanticAnnotations("d", true, false, false, null, null);
            var explicitNull = new OpenApiSemanticAnnotations(
                "d", true, false, false, OpenApiSemanticLiteral.Null, null);

            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a, explicitNull);
            Assert.Equal(
                "1.0",
                new OpenApiSemanticLiteral(OpenApiSemanticLiteralKind.Number, "1.0").RawText);
        }
        [Fact]
        public void LegacyStringEnumBecomesTypedStringLiterals()
        {
            var schema = new OpenApiSemanticSchema(
                OpenApiSemanticSchemaKind.Enum,
                "E",
                string.Empty,
                false,
                string.Empty,
                Array.Empty<OpenApiSemanticProperty>(),
                null,
                new[] { "a", "b" },
                new OpenApiSourceLocation(1, 1, "/x"));

            Assert.Equal(new[] { "a", "b" }, schema.EnumValues);
            Assert.All(schema.TypedEnumValues, static value =>
                Assert.Equal(OpenApiSemanticLiteralKind.String, value.Kind));
            Assert.Same(OpenApiSemanticAnnotations.Empty, schema.Annotations);

            var typed = new OpenApiSemanticSchema(
                OpenApiSemanticSchemaKind.Enum,
                "E",
                string.Empty,
                false,
                string.Empty,
                Array.Empty<OpenApiSemanticProperty>(),
                null,
                Array.Empty<string>(),
                new OpenApiSourceLocation(1, 1, "/x"),
                typedEnumValues: new[]
                {
                    new OpenApiSemanticLiteral(OpenApiSemanticLiteralKind.Integer, "1"),
                    new OpenApiSemanticLiteral(OpenApiSemanticLiteralKind.Integer, "2")
                });
            Assert.Equal(new[] { "1", "2" }, typed.EnumValues);
        }
    }
}
