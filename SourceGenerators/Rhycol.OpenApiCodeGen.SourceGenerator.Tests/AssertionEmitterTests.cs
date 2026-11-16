using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// The assertion IR (OpenApiSemanticAssertion), its mapping from a schema subtree
    /// (OpenApiSemanticParser.MapAssertion) and the emitted C# (AssertionEmitter): a table of schema fragments
    /// is mapped, emitted, compiled by Roslyn and run on JSON instances, with Newtonsoft.Json and with
    /// System.Text.Json. The composition fixtures of P0 are run through the same pipeline.
    /// </summary>
    public abstract class AssertionEmitterTests
    {
        private static readonly Lazy<IReadOnlyList<KeyValuePair<string, OpenApiSemanticAssertion>>> Roots =
            new Lazy<IReadOnlyList<KeyValuePair<string, OpenApiSemanticAssertion>>>(MapAllRoots);

        private readonly bool _systemTextJson;

        protected AssertionEmitterTests(bool systemTextJson)
        {
            _systemTextJson = systemTextJson;
        }

        public static IEnumerable<object[]> CaseNames() => AssertionCases.Names();

        public static IEnumerable<object[]> FixtureNames() => CompositionConformanceFixtures.Names();

        private static string FixtureRoot(string name) => "Fx_" + name;

        private static OpenApiSemanticAssertion Map(string dialectDocument, Func<SpecNode, SpecNode> select)
        {
            SpecNode root = TestBundleFactory.ReadRoot(dialectDocument);
            OpenApiSemanticParser mapper = OpenApiSemanticParser.CreateAssertionMapper(root);
            return mapper.MapAssertion(select(root));
        }

        private static SpecNode Member(SpecNode node, params string[] path)
        {
            foreach (string name in path)
            {
                Assert.True(node.TryGetProperty(name, out SpecNode next), "missing " + name);
                node = next;
            }

            return node;
        }

        private static IReadOnlyList<KeyValuePair<string, OpenApiSemanticAssertion>> MapAllRoots()
        {
            var roots = new List<KeyValuePair<string, OpenApiSemanticAssertion>>();
            foreach (AssertionCase item in AssertionCases.All)
            {
                string document = item.Document();
                // The fragment is read as a member of the document, so its $refs resolve like in a real document.
                string withFragment = document.Replace(
                    "\"paths\":{}",
                    "\"paths\":{},\"x-fragment\":" + item.Schema.Replace('\'', '"'));
                roots.Add(new KeyValuePair<string, OpenApiSemanticAssertion>(
                    item.Name,
                    Map(withFragment, static root => Member(root, "x-fragment"))));
            }

            foreach (ConformanceFixture fixture in CompositionConformanceFixtures.All)
            {
                roots.Add(new KeyValuePair<string, OpenApiSemanticAssertion>(
                    FixtureRoot(fixture.Name),
                    Map(fixture.Document(), static root => Member(root, "components", "schemas", "Body"))));
            }

            return roots;
        }

        private static readonly Lazy<AssertionProgram> Newtonsoft =
            new Lazy<AssertionProgram>(() => AssertionProgram.Build(Roots.Value, systemTextJson: false));

        private static readonly Lazy<AssertionProgram> SystemTextJson =
            new Lazy<AssertionProgram>(() => AssertionProgram.Build(Roots.Value, systemTextJson: true));

        private AssertionProgram Program => _systemTextJson ? SystemTextJson.Value : Newtonsoft.Value;

        private static IEnumerable<bool> Directions(AssertionDirection direction)
        {
            switch (direction)
            {
                case AssertionDirection.Request:
                    return new[] { true };
                case AssertionDirection.Response:
                    return new[] { false };
                default:
                    return new[] { false, true };
            }
        }

        private void Check(string root, IEnumerable<AssertionInstance> instances, string label)
        {
            var mismatches = new List<string>();
            foreach (AssertionInstance instance in instances)
            {
                foreach (bool request in Directions(instance.Direction))
                {
                    string? failure = Program.Evaluate(root, instance.Json, request);
                    if ((failure is null) != instance.Valid)
                    {
                        mismatches.Add(
                            (request ? "request " : "response ") + instance.Json + ": expected " +
                            (instance.Valid ? "valid" : "invalid") + ", got " + (failure ?? "valid"));
                    }
                }
            }

            Assert.True(mismatches.Count == 0, label + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void TheGeneratedAssertionsAcceptAndRejectLikeJsonSchema(string name)
        {
            AssertionCase item = AssertionCases.Get(name);

            Check(item.Name, item.Instances, item.Name + " (" + item.Schema + ")");
        }

        [Theory]
        [MemberData(nameof(FixtureNames))]
        public void TheBodySchemaOfEveryP0FixtureMeetsItsInstanceTable(string name)
        {
            ConformanceFixture fixture = CompositionConformanceFixtures.Get(name);

            Check(
                FixtureRoot(name),
                fixture.Instances.Select(static instance =>
                    new AssertionInstance(instance.Json, instance.Valid, AssertionDirection.Both)),
                name);
        }

        [Fact]
        public void TheGeneratedCodeCompilesWithoutWarnings()
        {
            Assert.Empty(Program.Warnings);
        }

        // ---- failure information -------------------------------------------------------------------------

        [Theory]
        [InlineData("Ref_RecursiveTree", "{'value':1,'children':[{'value':2,'children':[{}]}]}", "required", ".children[0].children[0]")]
        [InlineData("Ref_RecursiveTree", "{'value':'x'}", "type", ".value")]
        [InlineData("Properties_Nested", "{'a':{'b':[1,0]}}", "minimum", ".a.b[1]")]
        [InlineData("Additional_False", "{'b':1}", "additionalProperties", ".b")]
        [InlineData("Required_Several", "{'a':1}", "required", "")]
        [InlineData("Constraint_StringLength", "'a'", "minLength", "")]
        [InlineData("Enum_Strings", "'C'", "enum", "")]
        [InlineData("OneOf_ExactlyOne", "10", "oneOf", "")]
        [InlineData("AnyOf_AtLeastOne", "4", "anyOf", "")]
        [InlineData("Not_Type", "'a'", "not", "")]
        [InlineData("AllOf_AllParts", "{'a':'x','b':1}", "type", ".a")]
        [InlineData("Applicator_PropertyNames", "{'abcd':1}", "propertyNames", "")]
        [InlineData("Applicator_Contains", "[]", "contains", "")]
        [InlineData("BooleanSchema_False", "{'a':1}", "false", ".a")]
        [InlineData("Format_Date", "'2026-02-30'", "format", "")]
        [InlineData("Const_Number", "6", "const", "")]
        public void TheFirstViolationNamesTheKeywordAndThePath(string name, string json, string keyword, string path)
        {
            string? failure = Program.Evaluate(name, json.Replace('\'', '"'), request: false);

            Assert.Equal(keyword + "@" + path, failure);
        }

        [Fact]
        public void TheResponseEntryThrowsTheJsonExceptionOfTheSerializer()
        {
            string? result = Program.Enter("Properties_Nested", "{\"a\":{\"b\":[1,0]}}", "response");

            Assert.NotNull(result);
            string[] parts = result!.Split('|');
            Assert.Equal(
                _systemTextJson ? "System.Text.Json.JsonException" : "Newtonsoft.Json.JsonSerializationException",
                parts[0]);
            Assert.Equal("The response value at $.body.a.b[1] violates minimum: the value must be at least 1.", parts[1]);
        }

        [Fact]
        public void TheRequestEntryThrowsAnArgumentExceptionThatNamesTheParameter()
        {
            string? result = Program.Enter("Properties_Nested", "{\"a\":{\"b\":[1,0]}}", "request");

            Assert.NotNull(result);
            string[] parts = result!.Split('|');
            Assert.Equal("System.ArgumentException", parts[0]);
            Assert.Equal("argument", parts[2]);
            Assert.StartsWith("The request value at $.body.a.b[1] violates minimum", parts[1]);
        }

        [Fact]
        public void TheEntryReturnsForAValidValueAndUsesTheDirectionOfTheContract()
        {
            Assert.Null(Program.Enter("Properties_Nested", "{\"a\":{\"b\":[1]}}", "response"));
            // id is readOnly: a request may omit it, a response may not.
            Assert.Null(Program.Enter("Direction_ReadOnlyAndWriteOnly", "{\"name\":\"n\",\"secret\":\"s\"}", "request"));
            Assert.NotNull(Program.Enter("Direction_ReadOnlyAndWriteOnly", "{\"name\":\"n\",\"secret\":\"s\"}", "response"));
        }

        // ---- sharing --------------------------------------------------------------------------------------

        [Fact]
        public void EqualSchemasShareOneMethodAndARefTargetIsOneMethod()
        {
            GeneratedAssertionGraph graph = Program.Graph;

            // Type_String is the node of "type: string"; the Str components and inline strings are other nodes,
            // but two identical fragments are one node.
            int integerA = graph.Roots.Single(static root => root.Name == "Type_Integer").Node;
            Assert.True(integerA >= 0);
            int distinctNodes = graph.Nodes.Count;
            int mappedRoots = graph.Roots.Count;
            Assert.True(distinctNodes < mappedRoots * 6, "nodes: " + distinctNodes + ", roots: " + mappedRoots);
            Assert.Equal(distinctNodes, Program.Source.Split(new[] { "private static bool __OacgConstraint_AssertNode" }, StringSplitOptions.None).Length - 1);
        }

        [Fact]
        public void IdenticalSchemasOfTwoRootsAreTheSameNode()
        {
            SpecNode root = TestBundleFactory.ReadRoot(
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{}," +
                "\"x-a\":{\"type\":\"string\",\"minLength\":2},\"x-b\":{\"minLength\":2,\"type\":\"string\"}}");
            OpenApiSemanticParser mapper = OpenApiSemanticParser.CreateAssertionMapper(root);
            var builder = new GeneratedAssertionGraphBuilder();

            int first = builder.AddRoot("A", mapper.MapAssertion(Member(root, "x-a")));
            int second = builder.AddRoot("B", mapper.MapAssertion(Member(root, "x-b")));

            Assert.True(first >= 0);
            Assert.Equal(first, second);
            Assert.Single(builder.Build().Nodes);
        }

        [Fact]
        public void ASchemaThatAcceptsEverythingHasNoNode()
        {
            SpecNode root = TestBundleFactory.ReadRoot(
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{},\"x-a\":{\"description\":\"d\",\"x-extension\":1,\"example\":3}}");
            OpenApiSemanticParser mapper = OpenApiSemanticParser.CreateAssertionMapper(root);
            var builder = new GeneratedAssertionGraphBuilder();

            Assert.Equal(-1, builder.AddRoot("A", mapper.MapAssertion(Member(root, "x-a"))));
            Assert.Empty(builder.Build().Nodes);
        }

        // ---- the semantic model is unchanged -----------------------------------------------------------------

        [Fact]
        public void TheParserDoesNotFillTheAssertionsOfTheSemanticSchemasYet()
        {
            foreach (ConformanceFixture fixture in CompositionConformanceFixtures.All.Where(static item => item.CurrentId.Length == 0))
            {
                OpenApiSemanticDocument document = OpenApiSemanticParser.Parse(TestBundleFactory.ReadRoot(fixture.Document()));
                Assert.All(document.Schemas.Values, static schema => Assert.Empty(schema.Assertions));
            }
        }

        // ---- the mapping --------------------------------------------------------------------------------------

        [Fact]
        public void TheMappingKeepsTheDialectDecisionsOfTheParser()
        {
            OpenApiSemanticAssertion nullable = Map(
                Document("3.0.3", "{\"type\":\"string\",\"nullable\":true}"), static root => Member(root, "x-a"));
            OpenApiSemanticAssertion untyped = Map(
                Document("3.0.3", "{\"nullable\":true}"), static root => Member(root, "x-a"));
            OpenApiSemanticAssertion union = Map(
                Document("3.1.0", "{\"type\":[\"integer\",\"null\"]}"), static root => Member(root, "x-a"));
            OpenApiSemanticAssertion bounds30 = Map(
                Document("3.0.3", "{\"minimum\":1,\"exclusiveMinimum\":true}"), static root => Member(root, "x-a"));
            OpenApiSemanticAssertion bounds31 = Map(
                Document("3.1.0", "{\"exclusiveMinimum\":1}"), static root => Member(root, "x-a"));

            Assert.Equal(OpenApiSemanticAssertionTypes.String | OpenApiSemanticAssertionTypes.Null, nullable.Types);
            Assert.Null(untyped.Types);
            Assert.Equal(OpenApiSemanticAssertionTypes.Integer | OpenApiSemanticAssertionTypes.Null, union.Types);
            Assert.Null(bounds30.Constraints!.Minimum);
            Assert.Equal("1", bounds30.Constraints.ExclusiveMinimum);
            Assert.Equal("1", bounds31.Constraints!.ExclusiveMinimum);
        }

        [Fact]
        public void TheMappingKeepsLiteralsOfEveryKind()
        {
            OpenApiSemanticAssertion mapped = Map(
                Document("3.0.3", "{\"enum\":[null,true,\"s\",1,2.50,[1],{\"a\":null}]}"), static root => Member(root, "x-a"));

            Assert.Equal(
                new[]
                {
                    OpenApiSemanticAssertionValueKind.Null, OpenApiSemanticAssertionValueKind.Boolean,
                    OpenApiSemanticAssertionValueKind.String, OpenApiSemanticAssertionValueKind.Integer,
                    OpenApiSemanticAssertionValueKind.Number, OpenApiSemanticAssertionValueKind.Array,
                    OpenApiSemanticAssertionValueKind.Object
                },
                mapped.Enum!.Select(static value => value.Kind).ToArray());
            Assert.Equal("2.50", mapped.Enum![4].RawJson);
            Assert.Equal("{\"a\":null}", mapped.Enum[6].RawJson);
        }

        [Fact]
        public void ARefToTheSameComponentIsTheSameNodeAndIsNamed()
        {
            OpenApiSemanticAssertion mapped = Map(
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{}," +
                "\"x-a\":{\"properties\":{\"p\":{\"$ref\":\"#/components/schemas/T\"},\"q\":{\"$ref\":\"#/components/schemas/T\"}}}," +
                "\"components\":{\"schemas\":{\"T\":{\"type\":\"object\",\"properties\":{\"self\":{\"$ref\":\"#/components/schemas/T\"}}}}}}",
                static root => Member(root, "x-a"));

            OpenApiSemanticAssertion p = mapped.Properties[0].Schema;
            Assert.Same(p, mapped.Properties[1].Schema);
            Assert.Equal("T", p.Name);
            Assert.Same(p, p.Properties[0].Schema);
        }

        [Fact]
        public void AnUnresolvedRefIsReportedLikeTheParserDoes()
        {
            Assert.Throws<OpenApiSemanticException>(() => Map(
                Document("3.0.3", "{\"$ref\":\"#/components/schemas/Missing\"}"), static root => Member(root, "x-a")));
        }

        [Fact]
        public void AnInvalidKeywordIsReportedLikeTheParserDoes()
        {
            Assert.Throws<OpenApiSemanticException>(() => Map(
                Document("3.0.3", "{\"required\":\"a\"}"), static root => Member(root, "x-a")));
            Assert.Throws<OpenApiSemanticException>(() => Map(
                Document("3.0.3", "{\"minLength\":-1}"), static root => Member(root, "x-a")));
        }

        private static string Document(string version, string fragment)
        {
            return "{\"openapi\":\"" + version + "\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{},\"x-a\":" +
                   fragment + "}";
        }
    }

    public sealed class AssertionEmitterNewtonsoftTests : AssertionEmitterTests
    {
        public AssertionEmitterNewtonsoftTests()
            : base(systemTextJson: false)
        {
        }
    }

    public sealed class AssertionEmitterSystemTextJsonTests : AssertionEmitterTests
    {
        public AssertionEmitterSystemTextJsonTests()
            : base(systemTextJson: true)
        {
        }
    }
}
