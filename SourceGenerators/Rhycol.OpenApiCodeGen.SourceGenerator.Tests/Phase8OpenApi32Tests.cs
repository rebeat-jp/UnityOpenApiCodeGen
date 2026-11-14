using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P8 (G33): OpenAPI 3.2. Every feature is generated from a 3.2 document (diagnostics with their
    /// logical path), compiled, and exercised against a fake HttpMessageHandler (wire format and
    /// deserialization). Features that do not exist before 3.2 stay rejected in 3.0 / 3.1.
    /// </summary>
    public class Phase8OpenApi32Tests
    {
        public Phase8OpenApi32Tests()
            : this(SerializerMatrixHarness.NewtonsoftJson)
        {
        }

        protected Phase8OpenApi32Tests(string serializer)
        {
            MatrixMode.Current = serializer;
        }

        private const string SpecId = "0123456789abcdef0123456789abcdef";

        [Fact]
        public void TheRunUsesTheSerializerItIsNamedFor()
        {
            Phase4GeneratorExecution execution = Generate(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", "openapi32.json")));

            MatrixMode.AssertGeneratedFor(execution.GeneratedSource, ExpectsSystemTextJson);
        }

        protected virtual bool ExpectsSystemTextJson => false;

        private const string PetSchemas =
            "\"Pet\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\",\"name\"]," +
            "\"properties\":{\"id\":{\"type\":\"integer\"},\"name\":{\"type\":\"string\"}}}," +
            "\"Cat\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"kind\",\"lives\"]," +
            "\"properties\":{\"kind\":{\"type\":\"string\"},\"lives\":{\"type\":\"integer\"}}}," +
            "\"Dog\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"kind\",\"bark\"]," +
            "\"properties\":{\"kind\":{\"type\":\"string\"},\"bark\":{\"type\":\"string\"}}}," +
            "\"Other\":{\"type\":\"object\",\"additionalProperties\":false," +
            "\"properties\":{\"kind\":{\"type\":\"string\"},\"note\":{\"type\":\"string\"}}}";

        private static string Doc(
            string version,
            string paths,
            string components = "",
            string extraRoot = "")
        {
            return "{\"openapi\":\"" + version + "\",\"info\":{\"title\":\"P8\",\"version\":\"1\"}," +
                   "\"servers\":[{\"url\":\"https://api.example.test/v1\"}]," + extraRoot +
                   "\"paths\":{" + paths + "}," +
                   "\"components\":{" + components + "\"schemas\":{" + PetSchemas + "}}}";
        }

        private static Phase4GeneratorExecution Generate(string spec)
        {
            return MatrixMode.GenerateAndCompile(spec);
        }

        private static Diagnostic SingleDiagnostic(string spec, string id)
        {
            Phase4GeneratorExecution execution = Generate(spec);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal(id, diagnostic.Id);
            return diagnostic;
        }

        private static void AssertClean(Phase4GeneratorExecution execution)
        {
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        private static void AssertOnlyIgnoredWarnings(Phase4GeneratorExecution execution, params string[] logicalPaths)
        {
            Assert.All(execution.RunResult.Diagnostics, static d =>
            {
                Assert.Equal("OACG108", d.Id);
                Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            });
            Assert.Equal(logicalPaths.Length, execution.RunResult.Diagnostics.Length);
            foreach (string path in logicalPaths)
            {
                Assert.Contains(
                    execution.RunResult.Diagnostics,
                    d => d.GetMessage().Contains("logical path '" + path + "'", StringComparison.Ordinal));
            }

            Assert.Empty(execution.CompilationErrors);
        }

        private static object NewDto(Assembly assembly, string typeName, params (string Property, object? Value)[] values)
        {
            Type type = assembly.GetType(Phase4WireHarness.Ns + typeName, throwOnError: true)!;
            object instance = Activator.CreateInstance(type)!;
            foreach ((string property, object? value) in values)
            {
                type.GetProperty(property)!.SetValue(instance, value);
            }

            return instance;
        }

        private static Task<(object? Result, Phase4BodyWireHarness.Exchange Request)> Call(
            Assembly assembly,
            string methodName,
            Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> respond,
            params object?[] arguments)
        {
            return Phase4BodyWireHarness.Call(assembly, methodName, respond, arguments);
        }

        private static string Wire(Phase4BodyWireHarness.Exchange exchange)
        {
            return exchange.Uri!.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
        }

        private static bool HasMethod(Assembly assembly, string name)
        {
            return Phase4WireHarness.ClientType(assembly).GetMethod(name) is not null;
        }

        // ---- version ---------------------------------------------------------------------------------

        [Theory]
        [InlineData("3.2.0")]
        [InlineData("3.2.1")]
        public void OpenApi32DocumentIsAccepted(string version)
        {
            Phase4GeneratorExecution execution = Generate(Doc(
                version,
                "\"/pets/{id}\":{\"get\":{\"operationId\":\"getPet\"," +
                "\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"integer\"}}]," +
                "\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}}}}"));

            AssertClean(execution);
            Assert.Contains("getPet(", execution.GeneratedSource);
        }

        [Fact]
        public void OpenApi32UsesTheOpenApi31SchemaRules()
        {
            // The 3.1 dialect: type arrays, const and prefixItems are schema keywords of 3.2 as well.
            Phase4GeneratorExecution execution = Generate(Doc(
                "3.2.0",
                "\"/n\":{\"get\":{\"operationId\":\"getN\",\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"type\":[\"string\",\"null\"]}}}}}}}"));
            AssertClean(execution);

            Diagnostic nullable30 = SingleDiagnostic(
                Doc(
                    "3.0.3",
                    "\"/n\":{\"get\":{\"operationId\":\"getN\",\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"type\":[\"string\",\"null\"]}}}}}}}"),
                "OACG100");
            Assert.Contains("logical path", nullable30.GetMessage());
        }

        [Theory]
        [InlineData("https://spec.openapis.org/oas/3.1/dialect/base", true)]
        [InlineData("https://spec.openapis.org/oas/3.2/dialect/2025-09-17", true)]
        [InlineData("https://json-schema.org/draft/2020-12/schema", false)]
        public void OpenApi32AcceptsOnlyTheOpenApiBaseDialects(string dialect, bool accepted)
        {
            string spec = Doc(
                "3.2.0",
                "\"/n\":{\"get\":{\"operationId\":\"getN\",\"responses\":{\"204\":{\"description\":\"ok\"}}}}",
                extraRoot: "\"jsonSchemaDialect\":\"" + dialect + "\",");
            if (accepted)
            {
                AssertClean(Generate(spec));
                return;
            }

            Diagnostic diagnostic = SingleDiagnostic(spec, "OACG101");
            Assert.Contains("logical path '/jsonSchemaDialect'", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("3.0.3")]
        [InlineData("3.1.0")]
        public void PathItemQueryAndAdditionalOperationsAreRejectedBeforeOpenApi32(string version)
        {
            Diagnostic query = SingleDiagnostic(
                Doc(version, "\"/s\":{\"query\":{\"operationId\":\"q\",\"responses\":{\"204\":{\"description\":\"ok\"}}}}"),
                "OACG101");
            Assert.Contains("logical path '/paths/~1s/query'", query.GetMessage());
            Assert.Contains("Unsupported Path Item field: 'query'", query.GetMessage());

            Diagnostic additional = SingleDiagnostic(
                Doc(version, "\"/s\":{\"additionalOperations\":{\"PURGE\":{\"operationId\":\"p\",\"responses\":{\"204\":{\"description\":\"ok\"}}}}}"),
                "OACG101");
            Assert.Contains("logical path '/paths/~1s/additionalOperations'", additional.GetMessage());
        }

        // ---- QUERY method ----------------------------------------------------------------------------

        private const string QueryPaths =
            "\"/pets/search\":{\"query\":{\"operationId\":\"searchPets\"," +
            "\"parameters\":[{\"name\":\"limit\",\"in\":\"query\",\"schema\":{\"type\":\"integer\"}}]," +
            "\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"schema\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"name\":{\"type\":\"string\"}}}}}}," +
            "\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/components/schemas/Pet\"}}}}}}}}";

        [Fact]
        public async Task QueryMethodSendsAQueryRequestWithABody()
        {
            Phase4GeneratorExecution execution = Generate(Doc("3.2.0", QueryPaths));
            AssertClean(execution);
            Assert.Contains("new global::System.Net.Http.HttpMethod(\"QUERY\")", execution.GeneratedSource);
            Assembly assembly = execution.EmitAssembly();
            Type bodyType = assembly.GetTypes().Single(static t => t.Name.StartsWith("SearchPets", StringComparison.Ordinal) && t.GetProperty("Name") is not null);
            object body = Activator.CreateInstance(bodyType)!;
            bodyType.GetProperty("Name")!.SetValue(body, "rex");

            (object? result, Phase4BodyWireHarness.Exchange request) = await Call(
                assembly,
                "searchPets",
                static _ => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "[{\"id\":1,\"name\":\"rex\"},{\"id\":2,\"name\":\"tom\"}]"),
                (int?)5,
                body);

            Assert.Equal("QUERY", request.Method);
            Assert.Equal("https://api.example.test/v1/pets/search?limit=5", Wire(request));
            Assert.Equal("application/json", request.ContentType!.Split(';')[0]);
            Assert.Equal("{\"name\":\"rex\"}", request.BodyText);
            IList pets = Assert.IsAssignableFrom<IList>(result);
            Assert.Equal(2, pets.Count);
            Assert.Equal("tom", pets[1]!.GetType().GetProperty("Name")!.GetValue(pets[1]));
        }

        [Fact]
        public void QueryMethodWithoutOperationIdIsNamedFromMethodAndPath()
        {
            Phase4GeneratorExecution execution = Generate(Doc(
                "3.2.0",
                "\"/pets/search\":{\"query\":{\"responses\":{\"204\":{\"description\":\"ok\"}}}}"));

            AssertClean(execution);
            Assert.True(HasMethod(execution.EmitAssembly(), "QueryPetsSearch"));
        }

        // ---- additionalOperations --------------------------------------------------------------------

        private const string AdditionalPaths =
            "\"/pets/{id}\":{\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"integer\"}}]," +
            "\"additionalOperations\":{" +
            "\"PURGE\":{\"operationId\":\"purgePet\",\"responses\":{\"204\":{\"description\":\"purged\"}}}," +
            "\"Copy\":{\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}}}," +
            "\"x-LOCK\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}},\"responses\":{\"204\":{\"description\":\"ok\"}}}}}";

        [Fact]
        public async Task AdditionalOperationsSendTheMethodTokenExactlyAsWritten()
        {
            Phase4GeneratorExecution execution = Generate(Doc("3.2.0", AdditionalPaths));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();
            Assert.True(HasMethod(assembly, "purgePet"));
            Assert.True(HasMethod(assembly, "CopyPetsById"));
            Assert.True(HasMethod(assembly, "XlockPetsById"));

            (_, Phase4BodyWireHarness.Exchange purge) = await Call(
                assembly, "purgePet", static _ => Phase4BodyWireHarness.Respond(HttpStatusCode.NoContent), 7);
            Assert.Equal("PURGE", purge.Method);
            Assert.Equal("https://api.example.test/v1/pets/7", Wire(purge));

            (object? copied, Phase4BodyWireHarness.Exchange copy) = await Call(
                assembly,
                "CopyPetsById",
                static _ => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "{\"id\":7,\"name\":\"rex\"}"),
                7);
            Assert.Equal("Copy", copy.Method);
            Assert.Equal("rex", copied!.GetType().GetProperty("Name")!.GetValue(copied));

            object pet = NewDto(assembly, "Pet", ("Id", 7), ("Name", "rex"));
            (_, Phase4BodyWireHarness.Exchange lockRequest) = await Call(
                assembly, "XlockPetsById", static _ => Phase4BodyWireHarness.Respond(HttpStatusCode.NoContent), 7, pet);
            Assert.Equal("x-LOCK", lockRequest.Method);
            Assert.Equal("{\"id\":7,\"name\":\"rex\"}", lockRequest.BodyText);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("get")]
        [InlineData("Query")]
        [InlineData("tRaCe")]
        [InlineData("PATCH")]
        public void AdditionalOperationDuplicatingAFixedMethodIsRejectedAtTheOperation(string token)
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Doc("3.2.0", "\"/s\":{\"additionalOperations\":{\"" + token + "\":{\"responses\":{\"204\":{\"description\":\"ok\"}}}}}"),
                "OACG100");

            Assert.Contains("logical path '/paths/~1s/additionalOperations/" + token + "'", diagnostic.GetMessage());
            Assert.Contains("repeats a method that has its own Path Item field", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("BAD METHOD")]
        [InlineData("A/B")]
        [InlineData("")]
        [InlineData("M\\u00e9")]
        public void AdditionalOperationWithANonTokenMethodIsRejected(string token)
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Doc("3.2.0", "\"/s\":{\"additionalOperations\":{\"" + token + "\":{\"responses\":{\"204\":{\"description\":\"ok\"}}}}}"),
                "OACG100");

            Assert.Contains("not a valid HTTP method token", diagnostic.GetMessage());
            Assert.Contains("logical path '/paths/~1s/additionalOperations/", diagnostic.GetMessage());
        }

        [Fact]
        public void AdditionalOperationsMustBeAnObject()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Doc("3.2.0", "\"/s\":{\"additionalOperations\":[]}"),
                "OACG100");

            Assert.Contains("logical path '/paths/~1s/additionalOperations'", diagnostic.GetMessage());
        }

        [Fact]
        public void AdditionalOperationNamesAreDeterministicAndCollisionsGetUniqueNames()
        {
            // PURGE and purge are different tokens (case-sensitive) that synthesize the same id; an
            // explicit operationId "PurgePets" owns that name, so both synthesized ids get a suffix,
            // in the ordinal order of the method tokens.
            const string Paths =
                "\"/pets\":{\"additionalOperations\":{" +
                "\"PURGE\":{\"responses\":{\"204\":{\"description\":\"a\"}}}," +
                "\"purge\":{\"responses\":{\"204\":{\"description\":\"b\"}}}," +
                "\"Zap\":{\"operationId\":\"PurgePets\",\"responses\":{\"204\":{\"description\":\"c\"}}}}}";
            Phase4GeneratorExecution first = Generate(Doc("3.2.0", Paths));
            Phase4GeneratorExecution second = Generate(Doc("3.2.0", Paths));

            AssertClean(first);
            Assert.Equal(first.GeneratedSource, second.GeneratedSource);
            Assembly assembly = first.EmitAssembly();
            Assert.True(HasMethod(assembly, "PurgePets"));
            Assert.True(HasMethod(assembly, "PurgePets2"));
            Assert.True(HasMethod(assembly, "PurgePets3"));
        }

        [Fact]
        public void AdditionalOperationExplicitOperationIdCollidingWithAnotherOperationIsRejected()
        {
            const string Paths =
                "\"/a\":{\"get\":{\"operationId\":\"dup\",\"responses\":{\"204\":{\"description\":\"a\"}}}}," +
                "\"/b\":{\"additionalOperations\":{\"PURGE\":{\"operationId\":\"dup\",\"responses\":{\"204\":{\"description\":\"b\"}}}}}";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", Paths), "OACG106");

            Assert.Contains("logical path '/paths/~1b/additionalOperations/PURGE'", diagnostic.GetMessage());
            Assert.Contains("'dup'", diagnostic.GetMessage());
        }

        [Fact]
        public void AdditionalOperationsKeepTheOperationRules()
        {
            // Path parameters, servers and the response contract rules apply as for any operation.
            Diagnostic diagnostic = SingleDiagnostic(
                Doc("3.2.0", "\"/s/{missing}\":{\"additionalOperations\":{\"PURGE\":{\"responses\":{\"204\":{\"description\":\"ok\"}}}}}"),
                "OACG100");

            Assert.Contains("logical path '/paths/~1s~1{missing}/additionalOperations/PURGE'", diagnostic.GetMessage());
        }

        // ---- in: querystring -------------------------------------------------------------------------

        private const string FormQuerystringParameter =
            "{\"name\":\"filter\",\"in\":\"querystring\",\"required\":true,\"content\":{\"application/x-www-form-urlencoded\":{" +
            "\"schema\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"q\"],\"properties\":{" +
            "\"q\":{\"type\":\"string\"},\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"limit\":{\"type\":\"integer\"}}}%ENCODING%}}}";

        private static string QuerystringPaths(string parameter, string extraParameters = "")
        {
            return "\"/pets\":{\"get\":{\"operationId\":\"listPets\",\"parameters\":[" + parameter + extraParameters + "]," +
                   "\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/components/schemas/Pet\"}}}}}}}}";
        }

        private static string Filter(string encoding = "")
        {
            return FormQuerystringParameter.Replace("%ENCODING%", encoding);
        }

        private static object NewFilter(Assembly assembly, params (string Property, object? Value)[] values)
        {
            Type type = assembly.GetTypes().Single(static t => t.Name.StartsWith("Filter", StringComparison.Ordinal) && t.GetProperty("Q") is not null);
            object instance = Activator.CreateInstance(type)!;
            foreach ((string property, object? value) in values)
            {
                type.GetProperty(property)!.SetValue(instance, value);
            }

            return instance;
        }

        private static readonly object?[] NoArguments = new object?[0];

        private static System.Net.Http.HttpResponseMessage EmptyList(System.Net.Http.HttpRequestMessage _)
        {
            return Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "[]");
        }

        [Fact]
        public async Task QuerystringFormUrlEncodedContentBecomesTheWholeQueryString()
        {
            Phase4GeneratorExecution execution = Generate(Doc("3.2.0", QuerystringPaths(Filter())));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();
            object filter = NewFilter(
                assembly,
                ("Q", "a b&c"),
                ("Tags", new System.Collections.Generic.List<string> { "x", "y z" }),
                ("Limit", 5));

            (object? result, Phase4BodyWireHarness.Exchange request) = await Call(
                assembly,
                "listPets",
                static _ => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "[{\"id\":1,\"name\":\"rex\"}]"),
                filter);

            Assert.Equal("GET", request.Method);
            Assert.Equal(
                "https://api.example.test/v1/pets?limit=5&q=a%20b%26c&tags=x&tags=y%20z",
                Wire(request));
            Assert.Single(Assert.IsAssignableFrom<IList>(result));
        }

        [Fact]
        public async Task QuerystringEncodingObjectControlsTheFieldStyle()
        {
            Phase4GeneratorExecution execution = Generate(Doc(
                "3.2.0",
                QuerystringPaths(Filter(",\"encoding\":{\"tags\":{\"style\":\"form\",\"explode\":false}}"))));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();
            object filter = NewFilter(
                assembly,
                ("Q", "k"),
                ("Tags", new System.Collections.Generic.List<string> { "x", "y" }));

            (_, Phase4BodyWireHarness.Exchange request) = await Call(assembly, "listPets", EmptyList, filter);

            Assert.Equal("https://api.example.test/v1/pets?q=k&tags=x,y", Wire(request));
        }

        [Fact]
        public async Task OptionalQuerystringIsOmittedAndRequiredOneRejectsNull()
        {
            string optional = Filter().Replace("\"required\":true,", string.Empty);
            Phase4GeneratorExecution execution = Generate(Doc("3.2.0", QuerystringPaths(optional)));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();
            (_, Phase4BodyWireHarness.Exchange request) = await Call(assembly, "listPets", EmptyList, new object?[] { null });
            Assert.Equal("https://api.example.test/v1/pets", Wire(request));

            Assembly required = Generate(Doc("3.2.0", QuerystringPaths(Filter()))).EmitAssembly();
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await Call(
                required, "listPets", EmptyList, new object?[] { null }));
        }

        [Fact]
        public async Task QuerystringJsonContentIsThePercentEncodedJsonText()
        {
            const string Parameter =
                "{\"name\":\"filter\",\"in\":\"querystring\",\"required\":true,\"content\":{\"application/json\":{" +
                "\"schema\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"numbers\":{\"type\":\"array\",\"items\":{\"type\":\"integer\"}},\"flag\":{\"type\":\"boolean\"}}}}}}";
            Phase4GeneratorExecution execution = Generate(Doc("3.2.0", QuerystringPaths(Parameter)));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();
            Type type = assembly.GetTypes().Single(static t => t.Name.StartsWith("Filter", StringComparison.Ordinal) && t.GetProperty("Numbers") is not null);
            object filter = Activator.CreateInstance(type)!;
            type.GetProperty("Numbers")!.SetValue(filter, new System.Collections.Generic.List<int> { 1, 2 });
            type.GetProperty("Flag")!.SetValue(filter, (bool?)true);

            (_, Phase4BodyWireHarness.Exchange request) = await Call(assembly, "listPets", EmptyList, filter);

            Assert.Equal(
                "https://api.example.test/v1/pets?" + Uri.EscapeDataString("{\"flag\":true,\"numbers\":[1,2]}"),
                Wire(request));
        }

        [Fact]
        public async Task QuerystringCombinesWithPathAndHeaderParameters()
        {
            const string Paths =
                "\"/pets/{id}\":{\"get\":{\"operationId\":\"getPet\",\"parameters\":[" +
                "{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"integer\"}}," +
                "{\"name\":\"X-Trace\",\"in\":\"header\",\"required\":true,\"schema\":{\"type\":\"string\"}}," +
                "%FILTER%],\"responses\":{\"204\":{\"description\":\"ok\"}}}}";
            Phase4GeneratorExecution execution = Generate(Doc("3.2.0", Paths.Replace("%FILTER%", Filter())));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();
            object filter = NewFilter(assembly, ("Q", "k"));
            System.Reflection.ParameterInfo[] signature = Phase4WireHarness.ClientType(assembly).GetMethod("getPet")!.GetParameters();
            // Parameters are ordered by their location and name; supply them by type.
            var arguments = new object?[signature.Length];
            for (int index = 0; index < signature.Length; index++)
            {
                arguments[index] = signature[index].ParameterType == typeof(int) ? 9
                    : signature[index].ParameterType == typeof(string) ? "t-1"
                    : signature[index].ParameterType == filter.GetType() ? filter
                    : signature[index].ParameterType == typeof(CancellationToken) ? CancellationToken.None
                    : null;
            }

            (_, Phase4BodyWireHarness.Exchange request) = await Call(
                assembly, "getPet", static _ => Phase4BodyWireHarness.Respond(HttpStatusCode.NoContent), arguments);

            Assert.Equal("https://api.example.test/v1/pets/9?q=k", Wire(request));
            Assert.Equal("t-1", request.Headers["X-Trace"]);
        }

        [Fact]
        public void TwoQuerystringParametersAreOACG105AtTheSecond()
        {
            string second = Filter().Replace("\"filter\"", "\"other\"");
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", QuerystringPaths(Filter(), "," + second)), "OACG105");

            Assert.Contains("at most one in: querystring", diagnostic.GetMessage());
            Assert.Contains("logical path '/paths/~1pets/get/parameters/1'", diagnostic.GetMessage());
        }

        [Fact]
        public void QuerystringWithQueryParameterIsOACG105AtTheQuerystring()
        {
            const string Query = ",{\"name\":\"limit\",\"in\":\"query\",\"schema\":{\"type\":\"integer\"}}";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", QuerystringPaths(Filter(), Query)), "OACG105");

            Assert.Contains("cannot be combined with the in: query parameter 'limit'", diagnostic.GetMessage());
            Assert.Contains("logical path '/paths/~1pets/get/parameters/0'", diagnostic.GetMessage());
        }

        [Fact]
        public void QuerystringAtPathLevelAndQueryAtOperationLevelAreOACG105()
        {
            string paths =
                "\"/pets\":{\"parameters\":[" + Filter() + "],\"get\":{\"operationId\":\"listPets\"," +
                "\"parameters\":[{\"name\":\"limit\",\"in\":\"query\",\"schema\":{\"type\":\"integer\"}}]," +
                "\"responses\":{\"204\":{\"description\":\"ok\"}}}}";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", paths), "OACG105");

            Assert.Contains("logical path '/paths/~1pets/parameters/0'", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("text/plain")]
        [InlineData("application/xml")]
        [InlineData("multipart/form-data")]
        public void QuerystringWithAnUnsupportedMediaTypeIsOACG101(string mediaType)
        {
            string parameter =
                "{\"name\":\"filter\",\"in\":\"querystring\",\"content\":{\"" + mediaType + "\":{\"schema\":{\"type\":\"string\"}}}}";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", QuerystringPaths(parameter)), "OACG101");

            Assert.Contains("logical path '/paths/~1pets/get/parameters/0/content/" + mediaType.Replace("/", "~1") + "'", diagnostic.GetMessage());
            Assert.Contains("application/x-www-form-urlencoded", diagnostic.GetMessage());
        }

        [Fact]
        public void QuerystringFormContentMustBeAnObjectSchema()
        {
            const string Parameter =
                "{\"name\":\"filter\",\"in\":\"querystring\",\"content\":{\"application/x-www-form-urlencoded\":{\"schema\":{\"type\":\"string\"}}}}";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", QuerystringPaths(Parameter)), "OACG101");

            Assert.Contains("must be an object", diagnostic.GetMessage());
        }

        [Fact]
        public void QuerystringMustUseContentNotSchema()
        {
            const string Parameter =
                "{\"name\":\"filter\",\"in\":\"querystring\",\"schema\":{\"type\":\"string\"}}";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", QuerystringPaths(Parameter)), "OACG100");

            Assert.Contains("logical path '/paths/~1pets/get/parameters/0'", diagnostic.GetMessage());
            Assert.Contains("must define 'content'", diagnostic.GetMessage());
        }

        [Fact]
        public void QuerystringContentMustHaveExactlyOneMediaType()
        {
            const string Parameter =
                "{\"name\":\"filter\",\"in\":\"querystring\",\"content\":{\"application/json\":{\"schema\":{\"type\":\"string\"}},\"text/plain\":{\"schema\":{\"type\":\"string\"}}}}";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", QuerystringPaths(Parameter)), "OACG100");

            Assert.Contains("exactly one media type", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("3.0.3")]
        [InlineData("3.1.0")]
        public void QuerystringIsRejectedBeforeOpenApi32(string version)
        {
            Diagnostic diagnostic = SingleDiagnostic(Doc(version, QuerystringPaths(Filter())), "OACG101");

            Assert.Contains("logical path '/paths/~1pets/get/parameters/0'", diagnostic.GetMessage());
            Assert.Contains("Only path, query, header, and cookie parameters are supported.", diagnostic.GetMessage());
        }

        // ---- tags, $self, Response summary, Server name ----------------------------------------------

        private const string MinimalPaths = "\"/n\":{\"get\":{\"operationId\":\"getN\",\"responses\":{\"204\":{\"description\":\"ok\"}}}}";

        [Fact]
        public void TagParentKindAndSummaryAreAcceptedSilently()
        {
            const string Tags =
                "\"tags\":[{\"name\":\"nav\",\"summary\":\"Navigation\",\"kind\":\"nav\"}," +
                "{\"name\":\"pets\",\"summary\":\"Pets\",\"description\":\"d\",\"parent\":\"nav\",\"kind\":\"audience\"," +
                "\"externalDocs\":{\"url\":\"https://example.com\"}}],";
            Phase4GeneratorExecution execution = Generate(Doc("3.2.0", MinimalPaths, extraRoot: Tags));

            AssertClean(execution);
        }

        [Fact]
        public void TagParentMustNameADeclaredTag()
        {
            const string Tags = "\"tags\":[{\"name\":\"a\"},{\"name\":\"b\",\"parent\":\"missing\"}],";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", MinimalPaths, extraRoot: Tags), "OACG100");

            Assert.Contains("logical path '/tags/1/parent'", diagnostic.GetMessage());
        }

        [Fact]
        public void DuplicateTagNamesAreRejectedInOpenApi32OnlyAndNotCheckedBefore()
        {
            const string Tags = "\"tags\":[{\"name\":\"a\"},{\"name\":\"a\"}],";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", MinimalPaths, extraRoot: Tags), "OACG100");

            Assert.Contains("declared more than once", diagnostic.GetMessage());
            AssertClean(Generate(Doc("3.1.0", MinimalPaths, extraRoot: Tags)));
            AssertClean(Generate(Doc("3.0.3", MinimalPaths, extraRoot: Tags)));
        }

        [Fact]
        public void TagParentCyclesAreRejected()
        {
            const string Tags = "\"tags\":[{\"name\":\"a\",\"parent\":\"b\"},{\"name\":\"b\",\"parent\":\"a\"}],";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", MinimalPaths, extraRoot: Tags), "OACG100");

            Assert.Contains("cyclic", diagnostic.GetMessage());
        }

        [Fact]
        public void TagFieldsAreNotValidatedBeforeOpenApi32()
        {
            const string Tags = "\"tags\":[{\"name\":\"b\",\"parent\":\"missing\"}],";

            AssertClean(Generate(Doc("3.1.0", MinimalPaths, extraRoot: Tags)));
        }

        [Fact]
        public void SelfIsAcceptedAndIgnoredWithoutAWarningWhenNoDocumentIsReferenced()
        {
            Phase4GeneratorExecution execution = Generate(Doc(
                "3.2.0", MinimalPaths, extraRoot: "\"$self\":\"https://example.com/api/openapi.json\","));

            AssertClean(execution);
        }

        [Theory]
        [InlineData("\"$self\":\"https://example.com/api#frag\",", "must not contain a fragment")]
        [InlineData("\"$self\":5,", "must be a string")]
        public void InvalidSelfIsRejected(string field, string message)
        {
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", MinimalPaths, extraRoot: field), "OACG100");

            Assert.Contains("logical path '/$self'", diagnostic.GetMessage());
            Assert.Contains(message, diagnostic.GetMessage());
        }

        [Fact]
        public void SelfWarnsWhenOtherDocumentsAreReferenced()
        {
            string root = "{\"openapi\":\"3.2.0\",\"$self\":\"https://example.com/api/openapi.json\"," +
                          "\"info\":{\"title\":\"P8\",\"version\":\"1\"}," +
                          "\"paths\":{\"/pets\":{\"get\":{\"operationId\":\"getPet\",\"responses\":{\"200\":{\"description\":\"ok\"," +
                          "\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"pet.json#/components/schemas/Pet\"}}}}}}}}}";
            string external = "{\"openapi\":\"3.2.0\",\"info\":{\"title\":\"E\",\"version\":\"1\"},\"paths\":{}," +
                              "\"components\":{\"schemas\":{\"Pet\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}}}}";
            string bundle = TestBundleFactory.CreateV2(
                root,
                new[] { new TestBundleFactory.V2DocumentSpec("Assets/Specs/pet.json", "Assets/Specs/pet.json", "json", external) },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        "/paths/~1pets/get/responses/200/content/application~1json/schema/$ref",
                        "Assets/Specs/pet.json",
                        "/components/schemas/Pet")
                });

            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(root, bundle: bundle);

            AssertOnlyIgnoredWarnings(execution, "/$self");
        }

        [Fact]
        public void ResponseSummaryAndOptionalDescriptionAndServerNameAreAcceptedInOpenApi32()
        {
            const string Paths =
                "\"/n\":{\"get\":{\"operationId\":\"getN\",\"responses\":{" +
                "\"200\":{\"summary\":\"The N\",\"content\":{\"application/json\":{\"schema\":{\"type\":\"integer\"}}}}," +
                "\"404\":{\"summary\":\"Missing\",\"description\":\"d\"}}}}";
            string spec = Doc("3.2.0", Paths)
                .Replace("{\"url\":\"https://api.example.test/v1\"}", "{\"url\":\"https://api.example.test/v1\",\"name\":\"Production\",\"description\":\"p\"}");

            AssertClean(Generate(spec));
        }

        [Theory]
        [InlineData("3.0.3")]
        [InlineData("3.1.0")]
        public void ResponseSummaryAndMissingDescriptionAreRejectedBeforeOpenApi32(string version)
        {
            Diagnostic summary = SingleDiagnostic(
                Doc(version, "\"/n\":{\"get\":{\"operationId\":\"getN\",\"responses\":{\"204\":{\"description\":\"ok\",\"summary\":\"s\"}}}}"),
                "OACG101");
            Assert.Contains("Unsupported Response field: 'summary'", summary.GetMessage());

            Diagnostic description = SingleDiagnostic(
                Doc(version, "\"/n\":{\"get\":{\"operationId\":\"getN\",\"responses\":{\"204\":{}}}}"),
                "OACG100");
            Assert.Contains("logical path '/paths/~1n/get/responses/204'", description.GetMessage());
        }

        [Fact]
        public void DocumentationOnlyFieldsOfOpenApi32AreSilent()
        {
            const string Components =
                "\"examples\":{\"E\":{\"summary\":\"s\",\"dataValue\":{\"a\":1},\"serializedValue\":\"{\\\"a\\\":1}\"}},";
            const string Paths =
                "\"/n\":{\"get\":{\"operationId\":\"getN\",\"responses\":{\"200\":{\"description\":\"ok\"," +
                "\"content\":{\"application/json\":{\"examples\":{\"e\":{\"dataValue\":1,\"serializedValue\":\"1\"}}," +
                "\"schema\":{\"type\":\"object\",\"xml\":{\"nodeType\":\"element\",\"name\":\"n\"},\"properties\":{\"a\":{\"type\":\"integer\"}}}}}}}}}";

            AssertClean(Generate(Doc("3.2.0", Paths, Components)));
        }

        // ---- itemSchema ------------------------------------------------------------------------------

        private static string SequentialPaths(string mediaType, string mediaBody = "\"itemSchema\":{\"$ref\":\"#/components/schemas/Pet\"}")
        {
            return "\"/events\":{\"get\":{\"operationId\":\"getEvents\",\"responses\":{\"200\":{\"description\":\"ok\"," +
                   "\"content\":{\"" + mediaType + "\":{" + mediaBody + "}}}}}}";
        }

        [Theory]
        [InlineData("application/jsonl")]
        [InlineData("application/x-ndjson")]
        [InlineData("application/json-seq")]
        public async Task ItemSchemaOfASequentialMediaTypeIsIgnoredAndTheBodyIsReturnedAsBytes(string mediaType)
        {
            Phase4GeneratorExecution execution = Generate(Doc("3.2.0", SequentialPaths(mediaType)));

            AssertOnlyIgnoredWarnings(
                execution,
                "/paths/~1events/get/responses/200/content/" + mediaType.Replace("/", "~1") + "/itemSchema");
            Assert.Contains("not interpreted", execution.RunResult.Diagnostics[0].GetMessage());
            Assembly assembly = execution.EmitAssembly();
            const string Lines = "{\"id\":1,\"name\":\"a\"}\n{\"id\":2,\"name\":\"b\"}\n";

            (object? result, Phase4BodyWireHarness.Exchange request) = await Call(
                assembly,
                "getEvents",
                _ => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, Lines, mediaType),
                NoArguments);

            Assert.Equal("GET", request.Method);
            Assert.Equal(Lines, Encoding.UTF8.GetString((byte[])result!));
        }

        [Fact]
        public void ItemSchemaOfTextEventStreamIsOACG101()
        {
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", SequentialPaths("text/event-stream")), "OACG101");

            Assert.Contains("logical path '/paths/~1events/get/responses/200/content/text~1event-stream/itemSchema'", diagnostic.GetMessage());
            Assert.Contains("streaming", diagnostic.GetMessage());
        }

        [Fact]
        public void ItemSchemaOfANonSequentialMediaTypeIsOACG101()
        {
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", SequentialPaths("application/json")), "OACG101");

            Assert.Contains("logical path '/paths/~1events/get/responses/200/content/application~1json/itemSchema'", diagnostic.GetMessage());
            Assert.Contains("sequential media types only", diagnostic.GetMessage());
        }

        [Fact]
        public void TextEventStreamWithoutItemSchemaStaysATextResponse()
        {
            Phase4GeneratorExecution execution = Generate(Doc(
                "3.2.0", SequentialPaths("text/event-stream", "\"schema\":{\"type\":\"string\"}")));

            AssertClean(execution);
        }

        [Fact]
        public void ItemSchemaIsNotInterpretedBeforeOpenApi32()
        {
            // 3.1 has no itemSchema: it is an unknown field of the Media Type Object and ignored as before.
            AssertClean(Generate(Doc("3.1.0", SequentialPaths("application/jsonl"))));
        }

        [Fact]
        public void PrefixEncodingAndItemEncodingAreIgnoredWithAWarning()
        {
            const string Paths =
                "\"/e\":{\"post\":{\"operationId\":\"postE\",\"requestBody\":{\"content\":{\"multipart/form-data\":{" +
                "\"schema\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}," +
                "\"encoding\":{\"a\":{\"contentType\":\"text/plain\",\"encoding\":{\"x\":{}},\"itemEncoding\":{}}}}}}," +
                "\"responses\":{\"204\":{\"description\":\"ok\"}}}}";
            Phase4GeneratorExecution execution = Generate(Doc("3.2.0", Paths));

            AssertOnlyIgnoredWarnings(
                execution,
                "/paths/~1e/post/requestBody/content/multipart~1form-data/encoding/a/encoding",
                "/paths/~1e/post/requestBody/content/multipart~1form-data/encoding/a/itemEncoding");
        }

        [Theory]
        [InlineData("\"prefixEncoding\":[{\"contentType\":\"text/plain\"}]")]
        [InlineData("\"itemEncoding\":{\"contentType\":\"text/plain\"}")]
        public void MediaTypePrefixOrItemEncodingAloneIsIgnoredWithAWarning(string field)
        {
            string paths =
                "\"/e\":{\"post\":{\"operationId\":\"postE\",\"requestBody\":{\"content\":{\"multipart/form-data\":{" +
                "\"schema\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}," + field + "}}}," +
                "\"responses\":{\"204\":{\"description\":\"ok\"}}}}";
            string name = field.Substring(1, field.IndexOf('"', 1) - 1);

            AssertOnlyIgnoredWarnings(
                Generate(Doc("3.2.0", paths)),
                "/paths/~1e/post/requestBody/content/multipart~1form-data/" + name);
        }

        [Theory]
        [InlineData("\"prefixEncoding\":[{\"contentType\":\"text/plain\"}]")]
        [InlineData("\"itemEncoding\":{\"contentType\":\"text/plain\"}")]
        public void MediaTypeEncodingTogetherWithPrefixOrItemEncodingIsOACG100(string field)
        {
            string paths =
                "\"/e\":{\"post\":{\"operationId\":\"postE\",\"requestBody\":{\"content\":{\"multipart/form-data\":{" +
                "\"schema\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}," +
                "\"encoding\":{\"a\":{\"contentType\":\"text/plain\"}}," + field + "}}}," +
                "\"responses\":{\"204\":{\"description\":\"ok\"}}}}";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", paths), "OACG100");

            Assert.Contains("logical path '/paths/~1e/post/requestBody/content/multipart~1form-data/encoding'", diagnostic.GetMessage());
            Assert.Contains("must not be present together", diagnostic.GetMessage());
        }

        // ---- components.mediaTypes -------------------------------------------------------------------

        [Fact]
        public async Task MediaTypeReferencesToComponentsMediaTypesAreResolved()
        {
            const string Components =
                "\"mediaTypes\":{\"PetJson\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}," +
                "\"Unused\":{\"schema\":{\"type\":\"string\"}}},";
            const string Paths =
                "\"/pets\":{\"post\":{\"operationId\":\"createPet\"," +
                "\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"$ref\":\"#/components/mediaTypes/PetJson\"}}}," +
                "\"responses\":{\"201\":{\"description\":\"created\",\"content\":{\"application/json\":{\"$ref\":\"#/components/mediaTypes/PetJson\"}}}," +
                "\"400\":{\"description\":\"bad\",\"content\":{\"application/json\":{\"$ref\":\"#/components/mediaTypes/PetJson\"}}}}}}";
            Phase4GeneratorExecution execution = Generate(Doc("3.2.0", Paths, Components));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();
            object pet = NewDto(assembly, "Pet", ("Id", 3), ("Name", "rex"));

            (object? result, Phase4BodyWireHarness.Exchange request) = await Call(
                assembly,
                "createPet",
                static _ => Phase4BodyWireHarness.Respond(HttpStatusCode.Created, "{\"id\":3,\"name\":\"rex\"}"),
                pet);

            Assert.Equal("POST", request.Method);
            Assert.Equal("{\"id\":3,\"name\":\"rex\"}", request.BodyText);
            Assert.Equal("rex", result!.GetType().GetProperty("Name")!.GetValue(result));
        }

        [Fact]
        public void QuerystringContentMayReferenceComponentsMediaTypes()
        {
            const string Components =
                "\"mediaTypes\":{\"FilterForm\":{\"schema\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"q\":{\"type\":\"string\"}}}}},";
            const string Parameter =
                "{\"name\":\"filter\",\"in\":\"querystring\",\"content\":{\"application/x-www-form-urlencoded\":{\"$ref\":\"#/components/mediaTypes/FilterForm\"}}}";

            AssertClean(Generate(Doc("3.2.0", QuerystringPaths(Parameter), Components)));
        }

        [Fact]
        public void MissingMediaTypeComponentIsAnUnresolvedReference()
        {
            const string Paths =
                "\"/pets\":{\"post\":{\"operationId\":\"createPet\"," +
                "\"requestBody\":{\"content\":{\"application/json\":{\"$ref\":\"#/components/mediaTypes/Nope\"}}}," +
                "\"responses\":{\"204\":{\"description\":\"ok\"}}}}";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", Paths, "\"mediaTypes\":{},"), "OACG102");

            Assert.Contains("/paths/~1pets/post/requestBody/content/application~1json/$ref", diagnostic.GetMessage());
        }

        [Fact]
        public void MediaTypeComponentsAreRejectedBeforeOpenApi32AndInvalidOnesAreReported()
        {
            Diagnostic old = SingleDiagnostic(
                Doc("3.1.0", MinimalPaths, "\"mediaTypes\":{\"A\":{\"schema\":{\"type\":\"string\"}}},"),
                "OACG101");
            Assert.Contains("logical path '/components/mediaTypes'", old.GetMessage());

            Diagnostic invalid = SingleDiagnostic(
                Doc("3.2.0", MinimalPaths, "\"mediaTypes\":{\"A\":5},"),
                "OACG100");
            Assert.Contains("logical path '/components/mediaTypes/A'", invalid.GetMessage());
        }

        [Fact]
        public void MediaTypeReferenceInParameterContentOtherThanQuerystringIsOACG101()
        {
            const string Components = "\"mediaTypes\":{\"J\":{\"schema\":{\"type\":\"string\"}}},";
            const string Parameter =
                "{\"name\":\"x\",\"in\":\"query\",\"content\":{\"application/json\":{\"$ref\":\"#/components/mediaTypes/J\"}}}";
            Diagnostic diagnostic = SingleDiagnostic(Doc("3.2.0", QuerystringPaths(Parameter), Components), "OACG101");

            Assert.Contains("only for in: querystring", diagnostic.GetMessage());
        }

        // ---- discriminator defaultMapping ------------------------------------------------------------

        private const string PetUnionPaths =
            "\"/pets/any\":{\"get\":{\"operationId\":\"getAnimal\",\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Animal\"}}}}}}}";

        private static string AnimalComponents(string discriminatorExtra, string keyword = "oneOf")
        {
            return "\"Animal\":{\"" + keyword + "\":[{\"$ref\":\"#/components/schemas/Cat\"},{\"$ref\":\"#/components/schemas/Dog\"},{\"$ref\":\"#/components/schemas/Other\"}]," +
                   "\"discriminator\":{\"propertyName\":\"kind\",\"mapping\":{\"cat\":\"#/components/schemas/Cat\",\"dog\":\"Dog\"}" + discriminatorExtra + "}}";
        }

        private static string WithAnimal(string version, string discriminatorExtra, string keyword = "oneOf")
        {
            string spec = Doc(version, PetUnionPaths);
            return spec.Replace("\"schemas\":{", "\"schemas\":{" + AnimalComponents(discriminatorExtra, keyword) + ",");
        }

        private static async Task<object> ReadAnimal(Assembly assembly, string json)
        {
            (object? result, _) = await Call(
                assembly, "getAnimal", _ => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, json), NoArguments);
            return result!.GetType().GetProperty("Value")!.GetValue(result)!;
        }

        [Theory]
        [InlineData("{\"kind\":\"gizmo\",\"note\":\"n\"}")]
        [InlineData("{\"kind\":\"CAT\",\"note\":\"n\"}")]
        [InlineData("{\"note\":\"n\"}")]
        public async Task UnknownDiscriminatorValueDeserializesIntoTheDefaultMapping(string json)
        {
            Phase4GeneratorExecution execution = Generate(
                WithAnimal("3.2.0", ",\"defaultMapping\":\"#/components/schemas/Other\""));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();

            object value = await ReadAnimal(assembly, json);

            Assert.Equal("Other", value.GetType().Name);
            Assert.Equal("n", value.GetType().GetProperty("Note")!.GetValue(value));
        }

        [Fact]
        public async Task MappedValuesStillSelectTheirOwnSchemaWithADefaultMapping()
        {
            Phase4GeneratorExecution execution = Generate(
                WithAnimal("3.2.0", ",\"defaultMapping\":\"Other\""));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();

            Assert.Equal("Cat", (await ReadAnimal(assembly, "{\"kind\":\"cat\",\"lives\":9}")).GetType().Name);
            Assert.Equal("Dog", (await ReadAnimal(assembly, "{\"kind\":\"dog\",\"bark\":\"woof\"}")).GetType().Name);
            // The implicit mapping (the schema name) also still applies.
            Assert.Equal("Other", (await ReadAnimal(assembly, "{\"kind\":\"Other\"}")).GetType().Name);
        }

        [Fact]
        public async Task DefaultMappingMemberIsStillValidatedAgainstItsContract()
        {
            Phase4GeneratorExecution execution = Generate(
                WithAnimal("3.2.0", ",\"defaultMapping\":\"Other\""));
            Assembly assembly = execution.EmitAssembly();

            // Other has additionalProperties:false, so an unknown member of an unmapped value is invalid.
            Exception exception = await MatrixMode.ThrowsSerializationAsync(
                async () => await ReadAnimal(assembly, "{\"kind\":\"gizmo\",\"unexpected\":1}"));
            Assert.Contains("unexpected", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task WithoutDefaultMappingAnUnknownDiscriminatorValueStillFails()
        {
            Phase4GeneratorExecution execution = Generate(WithAnimal("3.2.0", string.Empty));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();

            Exception exception = await MatrixMode.ThrowsSerializationAsync(
                async () => await ReadAnimal(assembly, "{\"kind\":\"gizmo\"}"));
            Assert.Contains("does not select a schema", exception.Message);
        }

        [Fact]
        public async Task DefaultMappingAlsoAppliesToAnyOf()
        {
            Phase4GeneratorExecution execution = Generate(
                WithAnimal("3.2.0", ",\"defaultMapping\":\"Other\"", "anyOf"));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();

            Assert.Equal("Other", (await ReadAnimal(assembly, "{\"kind\":\"x\"}")).GetType().Name);
        }

        [Fact]
        public void DefaultMappingThatIsNotAMemberIsOACG101()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                WithAnimal("3.2.0", ",\"defaultMapping\":\"Pet\""), "OACG101");

            Assert.Contains("defaultMapping value 'Pet' does not refer to a member of the oneOf", diagnostic.GetMessage());
            Assert.Contains("defaultMapping'", diagnostic.GetMessage());
        }

        [Fact]
        public void DefaultMappingIsNotInterpretedBeforeOpenApi32()
        {
            // An unknown field of the 3.1 Discriminator Object: ignored as before.
            AssertClean(Generate(WithAnimal("3.1.0", ",\"defaultMapping\":\"Pet\"")));
        }

        [Fact]
        public async Task DefaultMappingOnAnInheritanceBaseSelectsTheDefaultSubtype()
        {
            const string Schemas =
                "\"Base\":{\"type\":\"object\",\"required\":[\"kind\"],\"properties\":{\"kind\":{\"type\":\"string\"}}," +
                "\"discriminator\":{\"propertyName\":\"kind\",\"mapping\":{\"a\":\"#/components/schemas/A\"},\"defaultMapping\":\"#/components/schemas/Fallback\"}}," +
                "\"A\":{\"allOf\":[{\"$ref\":\"#/components/schemas/Base\"},{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"integer\"}}}]}," +
                "\"Fallback\":{\"allOf\":[{\"$ref\":\"#/components/schemas/Base\"},{\"type\":\"object\",\"properties\":{\"y\":{\"type\":\"integer\"}}}]},";
            string spec = Doc(
                "3.2.0",
                "\"/b\":{\"get\":{\"operationId\":\"getBase\",\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Base\"}}}}}}}")
                .Replace("\"schemas\":{", "\"schemas\":{" + Schemas);
            Phase4GeneratorExecution execution = Generate(spec);
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();

            (object? result, _) = await Call(
                assembly,
                "getBase",
                _ => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "{\"kind\":\"zzz\",\"y\":4}"),
                NoArguments);

            object value = result!.GetType().GetProperty("Value")!.GetValue(result)!;
            Assert.Equal("Fallback", value.GetType().Name);
            Assert.Equal(4, value.GetType().GetProperty("Y")!.GetValue(value));
        }

        // ---- OAuth2 flows ----------------------------------------------------------------------------

        private const string DeviceScheme =
            "\"securitySchemes\":{\"oauth\":{\"type\":\"oauth2\",\"oauth2MetadataUrl\":\"https://auth.example.test/.well-known/oauth-authorization-server\"," +
            "\"deprecated\":false,\"flows\":{\"deviceAuthorization\":{\"deviceAuthorizationUrl\":\"https://auth.example.test/device\"," +
            "\"tokenUrl\":\"https://auth.example.test/token\",\"scopes\":{\"read\":\"Read pets\"}}}}},";

        [Fact]
        public async Task DeviceAuthorizationFlowAndMetadataUrlAreAcceptedAndOnlyDocumented()
        {
            Phase4GeneratorExecution execution = Generate(Doc(
                "3.2.0",
                "\"/n\":{\"get\":{\"operationId\":\"getN\",\"security\":[{\"oauth\":[\"read\"]}],\"responses\":{\"204\":{\"description\":\"ok\"}}}}",
                DeviceScheme));

            AssertClean(execution);
            Assert.Contains("deviceAuthorizationUrl https://auth.example.test/device", execution.GeneratedSource);
            Assert.Contains("metadata URL: https://auth.example.test/.well-known/oauth-authorization-server", execution.GeneratedSource);
            Assembly assembly = execution.EmitAssembly();

            // The client never runs the flow: an externally supplied token is sent as a Bearer token.
            (_, Phase4BodyWireHarness.Exchange request) = await CallWithConfiguredClient(
                assembly,
                "getN",
                client =>
                {
                    PropertyInfo token = client.GetType().GetProperties().First(
                        static p => p.PropertyType == typeof(string) && p.Name.Contains("oauth", StringComparison.OrdinalIgnoreCase));
                    token.SetValue(client, "device-token");
                });

            Assert.Equal("Bearer device-token", request.Headers["Authorization"]);
        }

        private static async Task<(object? Result, Phase4BodyWireHarness.Exchange Request)> CallWithConfiguredClient(
            Assembly assembly,
            string methodName,
            Action<object> configure,
            params object?[] arguments)
        {
            Phase4BodyWireHarness.Exchange? last = null;
            var handler = new CapturingHandler(
                _ => methodName == "listPets"
                    ? Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "[]")
                    : Phase4BodyWireHarness.Respond(HttpStatusCode.NoContent),
                e => last = e);
            using var httpClient = new System.Net.Http.HttpClient(handler);
            object client = Phase4WireHarness.CreateClient(assembly, httpClient);
            configure(client);
            await ((Task)Phase4WireHarness.ClientType(assembly).GetMethod(methodName)!.Invoke(
                client, arguments.Concat(new object?[] { CancellationToken.None }).ToArray())!);
            return (null, last!);
        }

        private sealed class CapturingHandler : System.Net.Http.HttpMessageHandler
        {
            private readonly Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> _respond;
            private readonly Action<Phase4BodyWireHarness.Exchange> _record;

            internal CapturingHandler(
                Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> respond,
                Action<Phase4BodyWireHarness.Exchange> record)
            {
                _respond = respond;
                _record = record;
            }

            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
                System.Net.Http.HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var exchange = new Phase4BodyWireHarness.Exchange { Method = request.Method.Method, Uri = request.RequestUri };
                foreach (var header in request.Headers)
                {
                    exchange.Headers[header.Key] = string.Join(",", header.Value);
                }

                _record(exchange);
                return Task.FromResult(_respond(request));
            }
        }

        [Fact]
        public void DeviceAuthorizationRequiresItsUrlsAndIsUnknownBeforeOpenApi32()
        {
            Diagnostic missing = SingleDiagnostic(
                Doc("3.2.0", MinimalPaths, DeviceScheme.Replace("\"deviceAuthorizationUrl\":\"https://auth.example.test/device\",", string.Empty)),
                "OACG100");
            Assert.Contains("logical path '/components/securitySchemes/oauth/flows/deviceAuthorization'", missing.GetMessage());
            Assert.Contains("deviceAuthorizationUrl", missing.GetMessage());

            Diagnostic old = SingleDiagnostic(Doc("3.1.0", MinimalPaths, DeviceScheme), "OACG100");
            Assert.Contains("Unknown oauth2 flow 'deviceAuthorization'", old.GetMessage());
            Assert.Contains("logical path '/components/securitySchemes/oauth/flows/deviceAuthorization'", old.GetMessage());
        }

        // ---- Editor bundling and the normalized bundle reader ----------------------------------------

        private static async Task<(NormalizedSpecGraph Graph, NormalizedSpecBundle Bundle)> Bundle(
            string projectRoot,
            string rootPath)
        {
            var loader = new ExternalSpecGraphLoader(
                projectRoot,
                new RawJsonNormalizer(),
                new RawYamlNormalizer());
            NormalizedSpecGraph graph = await loader.LoadAsync(rootPath, SpecId, CancellationToken.None);
            byte[] bytes = CanonicalSpecBundleWriter.Write(
                graph.SpecId,
                graph.RawSha256,
                graph.Documents,
                graph.Edges);
            return (graph, NormalizedSpecBundleReader.Read(Encoding.UTF8.GetString(bytes)));
        }

        [Fact]
        public async Task EditorBundlingCollectsRefsUnderQueryAdditionalOperationsAndMediaTypeComponents()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(
                    projectRoot,
                    "Assets/Specs/root.json",
                    "{\"openapi\":\"3.2.0\",\"info\":{\"title\":\"R\",\"version\":\"1\"}," +
                    "\"paths\":{\"/s\":{" +
                    "\"query\":{\"operationId\":\"q\",\"parameters\":[{\"$ref\":\"param.json\"}]," +
                    "\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"query-body.json\"}}}}," +
                    "\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"$ref\":\"media.json\"}}}}}," +
                    "\"additionalOperations\":{\"PURGE\":{\"operationId\":\"p\",\"requestBody\":{\"$ref\":\"body.json\"}," +
                    "\"responses\":{\"200\":{\"$ref\":\"response.json\"}}}}}}," +
                    "\"components\":{\"mediaTypes\":{\"Direct\":{\"$ref\":\"media-direct.json\"}," +
                    "\"Inline\":{\"schema\":{\"$ref\":\"inline-schema.json\"},\"encoding\":{\"a\":{\"headers\":{\"X\":{\"$ref\":\"header.json\"}}}}}}," +
                    "\"schemas\":{\"Pet\":{\"type\":\"string\"}}}}");
                WriteText(projectRoot, "Assets/Specs/param.json", "{\"name\":\"qs\",\"in\":\"querystring\",\"content\":{\"application/x-www-form-urlencoded\":{\"schema\":{\"$ref\":\"qs-schema.json\"}}}}");
                WriteText(projectRoot, "Assets/Specs/qs-schema.json", "{\"type\":\"object\",\"properties\":{\"q\":{\"type\":\"string\"}}}");
                WriteText(projectRoot, "Assets/Specs/query-body.json", "{\"type\":\"string\"}");
                WriteText(projectRoot, "Assets/Specs/media.json", "{\"schema\":{\"$ref\":\"media-schema.json\"}}");
                WriteText(projectRoot, "Assets/Specs/media-schema.json", "{\"type\":\"integer\"}");
                WriteText(projectRoot, "Assets/Specs/body.json", "{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"purge-schema.json\"}}}}");
                WriteText(projectRoot, "Assets/Specs/purge-schema.json", "{\"type\":\"boolean\"}");
                WriteText(projectRoot, "Assets/Specs/response.json", "{\"description\":\"d\"}");
                WriteText(projectRoot, "Assets/Specs/media-direct.json", "{\"schema\":{\"type\":\"string\"}}");
                WriteText(projectRoot, "Assets/Specs/inline-schema.json", "{\"type\":\"number\"}");
                WriteText(projectRoot, "Assets/Specs/header.json", "{\"schema\":{\"type\":\"string\"}}");

                (NormalizedSpecGraph graph, NormalizedSpecBundle bundle) = await Bundle(projectRoot, "Assets/Specs/root.json");

                string[] expected =
                {
                    "/paths/~1s/query/parameters/0/$ref",
                    "/paths/~1s/query/requestBody/content/application~1json/schema/$ref",
                    "/paths/~1s/query/responses/200/content/application~1json/$ref",
                    "/paths/~1s/additionalOperations/PURGE/requestBody/$ref",
                    "/paths/~1s/additionalOperations/PURGE/responses/200/$ref",
                    "/components/mediaTypes/Direct/$ref",
                    "/components/mediaTypes/Inline/schema/$ref",
                    "/components/mediaTypes/Inline/encoding/a/headers/X/$ref"
                };
                Assert.Equal(expected.Length, bundle.ReferenceEdges.Count);
                foreach (string pointer in expected)
                {
                    Assert.True(
                        bundle.ReferenceEdges.Any(edge => edge.SourcePointer == pointer),
                        "Missing " + pointer + " in " + string.Join(", ", bundle.ReferenceEdges.Select(static edge => edge.SourcePointer)));
                }

                // A bare external file is bundled as a leaf: only the references of the entry document are edges.
                string documents = string.Join(", ", graph.Documents.Select(static document => document.SourcePath));
                Assert.True(graph.Documents.Count == 9, documents);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task EditorBundlingDoesNotCollectRefsInsideItemSchemaOrExtensionsOfOpenApi32()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(
                    projectRoot,
                    "Assets/Specs/root.json",
                    "{\"openapi\":\"3.2.0\",\"$self\":\"https://example.com/root\",\"info\":{\"title\":\"R\",\"version\":\"1\"}," +
                    "\"tags\":[{\"name\":\"a\",\"parent\":\"b\",\"externalDocs\":{\"url\":\"u\"}}]," +
                    "\"paths\":{\"/events\":{\"get\":{\"operationId\":\"e\",\"responses\":{\"200\":{\"description\":\"ok\"," +
                    "\"content\":{\"application/jsonl\":{\"itemSchema\":{\"$ref\":\"missing-item.json\"}," +
                    "\"examples\":{\"x\":{\"$ref\":\"missing-example.json\"}}}}}}}}}," +
                    "\"components\":{\"mediaTypes\":{\"X\":{\"itemSchema\":{\"$ref\":\"missing-item2.json\"}}}," +
                    "\"examples\":{\"E\":{\"$ref\":\"missing-example2.json\"}}," +
                    "\"schemas\":{\"Pet\":{\"$ref\":\"pet.json\"}}}}");
                WriteText(projectRoot, "Assets/Specs/pet.json", "{\"type\":\"string\"}");

                (NormalizedSpecGraph graph, NormalizedSpecBundle bundle) = await Bundle(projectRoot, "Assets/Specs/root.json");

                Assert.Equal(2, graph.Documents.Count);
                Assert.Single(bundle.ReferenceEdges);
                Assert.Equal("/components/schemas/Pet/$ref", bundle.ReferenceEdges[0].SourcePointer);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task BundledOpenApi32DocumentWithAnExternalMediaTypeGeneratesAClient()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(
                    projectRoot,
                    "Assets/Specs/root.json",
                    "{\"openapi\":\"3.2.0\",\"info\":{\"title\":\"R\",\"version\":\"1\"}," +
                    "\"servers\":[{\"url\":\"https://api.example.test\"}]," +
                    "\"paths\":{\"/s\":{\"query\":{\"operationId\":\"search\"," +
                    "\"requestBody\":{\"content\":{\"application/json\":{\"$ref\":\"media.json#/components/mediaTypes/M\"}}}," +
                    "\"responses\":{\"204\":{\"description\":\"ok\"}}}}}}");
                WriteText(
                    projectRoot,
                    "Assets/Specs/media.json",
                    "{\"openapi\":\"3.2.0\",\"info\":{\"title\":\"M\",\"version\":\"1\"},\"paths\":{},\"components\":{\"mediaTypes\":{\"M\":{\"schema\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"term\":{\"type\":\"string\"}}}}}}}");

                var loader = new ExternalSpecGraphLoader(projectRoot, new RawJsonNormalizer(), new RawYamlNormalizer());
                NormalizedSpecGraph graph = await loader.LoadAsync("Assets/Specs/root.json", SpecId, CancellationToken.None);
                string bundleText = Encoding.UTF8.GetString(CanonicalSpecBundleWriter.Write(
                    graph.SpecId, graph.RawSha256, graph.Documents, graph.Edges));

                Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(
                    File.ReadAllText(Path.Combine(projectRoot, "Assets", "Specs", "root.json")),
                    bundle: bundleText);

                AssertClean(execution);
                Assert.Contains("HttpMethod(\"QUERY\")", execution.GeneratedSource);
                Assert.Contains("class SearchRequest", execution.GeneratedSource);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        private static string CreateProjectRoot()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "OpenApiCodeGenPhase8",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(path, "Assets", "Specs"));
            return path;
        }

        private static void WriteText(string projectRoot, string relativePath, string content)
        {
            string path = Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        // ---- defaultMapping: write side and inheritance base ------------------------------------------

        private static string WithAnimalBody(string discriminatorExtra)
        {
            const string BodyPath =
                "\"/animals\":{\"put\":{\"operationId\":\"putAnimal\",\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Animal\"}}}}," +
                "\"responses\":{\"204\":{\"description\":\"ok\"}}}}";
            string spec = Doc("3.2.0", PetUnionPaths + "," + BodyPath);
            return spec.Replace("\"schemas\":{", "\"schemas\":{" + AnimalComponents(discriminatorExtra) + ",");
        }

        private static object NewAnimal(Assembly assembly, string memberName, params (string Property, object? Value)[] values)
        {
            object member = NewDto(assembly, memberName, values);
            Type animal = assembly.GetType(Phase4WireHarness.Ns + "Animal", throwOnError: true)!;
            return animal.GetMethod("From" + memberName)!.Invoke(null, new[] { member })!;
        }

        [Fact]
        public async Task DefaultMappingMemberIsWrittenWithItsFirstDiscriminatorValue()
        {
            Phase4GeneratorExecution execution = Generate(WithAnimalBody(",\"defaultMapping\":\"Other\""));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();
            object animal = NewAnimal(assembly, "Other", ("Note", "n"));

            (_, Phase4BodyWireHarness.Exchange request) = await Call(
                assembly, "putAnimal", static _ => Phase4BodyWireHarness.Respond(HttpStatusCode.NoContent), animal);

            Assert.Equal("PUT", request.Method);
            // Other has no mapping entry, so its implicit value (the schema name) is its first value.
            Assert.Equal("{\"kind\":\"Other\",\"note\":\"n\"}", request.BodyText);
        }

        [Fact]
        public async Task UnknownDiscriminatorValueIsReplacedByTheDefaultValueWhenWrittenBack()
        {
            Phase4GeneratorExecution execution = Generate(WithAnimalBody(",\"defaultMapping\":\"Other\""));
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();

            (object? read, _) = await Call(
                assembly,
                "getAnimal",
                _ => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "{\"kind\":\"gizmo\",\"note\":\"n\"}"),
                NoArguments);
            (_, Phase4BodyWireHarness.Exchange request) = await Call(
                assembly, "putAnimal", static _ => Phase4BodyWireHarness.Respond(HttpStatusCode.NoContent), read);

            // Lossy round trip (documented): the unknown value "gizmo" is not kept.
            Assert.DoesNotContain("gizmo", request.BodyText);
            Assert.Equal("{\"kind\":\"Other\",\"note\":\"n\"}", request.BodyText);
        }

        private const string BaseDefaultsToItself =
            "\"Base\":{\"type\":\"object\",\"required\":[\"kind\"],\"properties\":{\"kind\":{\"type\":\"string\"}}," +
            "\"discriminator\":{\"propertyName\":\"kind\",\"mapping\":{\"a\":\"#/components/schemas/A\"},\"defaultMapping\":\"#/components/schemas/Base\"}}," +
            "\"A\":{\"allOf\":[{\"$ref\":\"#/components/schemas/Base\"},{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"integer\"}}}]},";

        [Fact]
        public async Task DefaultMappingToTheInheritanceBaseItselfIsIgnoredWithAWarningAndDoesNotRecurse()
        {
            string spec = Doc(
                "3.2.0",
                "\"/b\":{\"get\":{\"operationId\":\"getBase\",\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Base\"}}}}}}}")
                .Replace("\"schemas\":{", "\"schemas\":{" + BaseDefaultsToItself);
            Phase4GeneratorExecution execution = Generate(spec);

            AssertOnlyIgnoredWarnings(execution, "/components/schemas/Base/discriminator/defaultMapping");
            Assembly assembly = execution.EmitAssembly();
            (object? known, _) = await Call(
                assembly, "getBase", _ => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "{\"kind\":\"a\",\"x\":1}"), NoArguments);
            Assert.Equal("A", known!.GetType().GetProperty("Value")!.GetValue(known)!.GetType().Name);

            // The ignored default changes nothing: an unknown value is rejected, not looped on.
            await MatrixMode.ThrowsSerializationAsync(async () => await Call(
                assembly, "getBase", _ => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "{\"kind\":\"zzz\"}"), NoArguments));
        }

        // ---- querystring with an existing query on the URL --------------------------------------------

        [Fact]
        public async Task QuerystringIsAppendedAfterAQueryOfTheServerUrlAndASecurityQueryKey()
        {
            const string Components =
                "\"securitySchemes\":{\"key\":{\"type\":\"apiKey\",\"in\":\"query\",\"name\":\"api_key\"}},";
            string spec = Doc("3.2.0", QuerystringPaths(Filter()), Components)
                .Replace("https://api.example.test/v1", "https://api.example.test/v1?tenant=t1")
                .Replace("\"paths\":{", "\"security\":[{\"key\":[]}],\"paths\":{");
            Phase4GeneratorExecution execution = Generate(spec);
            AssertClean(execution);
            Assembly assembly = execution.EmitAssembly();
            object filter = NewFilter(assembly, ("Q", "k"), ("Limit", 2));

            (_, Phase4BodyWireHarness.Exchange request) = await CallWithConfiguredClient(
                assembly,
                "listPets",
                client => client.GetType().GetProperties()
                    .First(static p => p.PropertyType == typeof(string) && p.Name.EndsWith("ApiKey", StringComparison.Ordinal))
                    .SetValue(client, "s3cret"),
                filter);

            Assert.Equal("https://api.example.test/v1/pets?tenant=t1&limit=2&q=k&api_key=s3cret", Wire(request));
        }
    }

    /// <summary>The System.Text.Json run of every test of <see cref="Phase8OpenApi32Tests"/>.</summary>
    public sealed class Phase8OpenApi32TestsSystemTextJson : Phase8OpenApi32Tests
    {
        public Phase8OpenApi32TestsSystemTextJson()
            : base(SerializerMatrixHarness.SystemTextJson)
        {
        }

        protected override bool ExpectsSystemTextJson => true;
    }
}
