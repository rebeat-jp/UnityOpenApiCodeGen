using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P6 (G29, G30): the validation keywords are checked at run time. A request that violates one is
    /// rejected before anything is sent (ArgumentException, the fake handler is never called); a response
    /// that violates one raises a JsonSerializationException. The clients are compiled from two inline
    /// specs (OpenAPI 3.1 and 3.0) and driven through reflection against a fake HttpMessageHandler.
    /// </summary>
    public class Phase6ConstraintTests
    {
        public Phase6ConstraintTests()
            : this(SerializerMatrixHarness.NewtonsoftJson)
        {
        }

        protected Phase6ConstraintTests(string serializer)
        {
            MatrixMode.Current = serializer;
        }

        private static readonly string[] BodySchemas =
        {
            "Name", "Code", "Partial", "Digits", "Letters", "Count", "Price", "Step", "Tags", "AnyList", "Bag",
            "KeyNames", "Pat", "Dep", "Containing", "Tuple", "Tuple2", "Rec", "Short", "TwoTags", "Level", "RW", "Slow"
        };

        private static readonly string[] ReadOnlySchemas = { "Pick", "NotShort" };

        [Fact]
        public void TheRunUsesTheSerializerItIsNamedFor()
        {
            MatrixMode.AssertGeneratedFor(Execution31.Value.GeneratedSource, ExpectsSystemTextJson);
        }

        protected virtual bool ExpectsSystemTextJson => false;

        // The clients are compiled once per serializer (the class runs once with each).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Phase4GeneratorExecution>> Executions31 =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Phase4GeneratorExecution>>();

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Assembly>> Assemblies31 =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Assembly>>();

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Assembly>> Assemblies30 =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Assembly>>();

        private static Lazy<Phase4GeneratorExecution> Execution31 =>
            Executions31.GetOrAdd(
                MatrixMode.Current,
                static _ => new Lazy<Phase4GeneratorExecution>(() => MatrixMode.GenerateAndCompile(Spec31())));

        private static Lazy<Assembly> Assembly31 =>
            Assemblies31.GetOrAdd(MatrixMode.Current, static _ => new Lazy<Assembly>(() => Execution31.Value.EmitAssembly()));

        private static Lazy<Assembly> Assembly30 =>
            Assemblies30.GetOrAdd(
                MatrixMode.Current,
                static _ => new Lazy<Assembly>(() =>
                {
                    Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(Spec30());
                    Assert.Empty(execution.RunResult.Diagnostics);
                    Assert.Empty(execution.CompilationErrors);
                    return execution.EmitAssembly();
                }));

        internal static string Spec31()
        {
            var paths = new List<string>();
            foreach (string name in BodySchemas)
            {
                string lower = name.ToLowerInvariant();
                paths.Add("\"/send/" + lower + "\":{\"post\":{\"operationId\":\"send" + name + "\"," +
                          "\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/" + name + "\"}}}}," +
                          "\"responses\":{\"204\":{\"description\":\"ok\"}}}}");
            }

            foreach (string name in BodySchemas.Concat(ReadOnlySchemas))
            {
                string lower = name.ToLowerInvariant();
                paths.Add("\"/read/" + lower + "\":{\"get\":{\"operationId\":\"read" + name + "\"," +
                          "\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/" + name + "\"}}}}}}}");
            }

            paths.Add("\"/q/name\":{\"get\":{\"operationId\":\"queryName\",\"parameters\":[{\"name\":\"value\",\"in\":\"query\",\"required\":true," +
                      "\"schema\":{\"$ref\":\"#/components/schemas/Name\"}}],\"responses\":{\"204\":{\"description\":\"ok\"}}}}");
            paths.Add("\"/form\":{\"post\":{\"operationId\":\"postForm\",\"requestBody\":{\"required\":true,\"content\":{\"application/x-www-form-urlencoded\":" +
                      "{\"schema\":{\"$ref\":\"#/components/schemas/FormBody\"}}}},\"responses\":{\"204\":{\"description\":\"ok\"}}}}");
            paths.Add("\"/multipart\":{\"post\":{\"operationId\":\"postMultipart\",\"requestBody\":{\"required\":true,\"content\":{\"multipart/form-data\":" +
                      "{\"schema\":{\"$ref\":\"#/components/schemas/FormBody\"}}}},\"responses\":{\"204\":{\"description\":\"ok\"}}}}");
            paths.Add("\"/text/send\":{\"post\":{\"operationId\":\"sendText\",\"requestBody\":{\"required\":true,\"content\":{\"text/plain\":" +
                      "{\"schema\":{\"$ref\":\"#/components/schemas/Name\"}}}},\"responses\":{\"204\":{\"description\":\"ok\"}}}}");
            paths.Add("\"/text/read\":{\"get\":{\"operationId\":\"readText\",\"responses\":{\"200\":{\"description\":\"ok\"," +
                      "\"content\":{\"text/plain\":{\"schema\":{\"$ref\":\"#/components/schemas/Name\"}}}}}}}");
            paths.Add("\"/hdr\":{\"get\":{\"operationId\":\"readWithHeader\",\"responses\":{\"200\":{\"description\":\"ok\"," +
                      "\"headers\":{\"X-Count\":{\"required\":true,\"schema\":{\"$ref\":\"#/components/schemas/Count\"}}}," +
                      "\"content\":{\"application/json\":{\"schema\":{\"type\":\"string\"}}}}}}}");

            const string Schemas =
                "\"Name\":{\"type\":\"string\",\"minLength\":3,\"maxLength\":5}," +
                "\"Code\":{\"type\":\"string\",\"pattern\":\"^[a-z]+$\"}," +
                "\"Partial\":{\"type\":\"string\",\"pattern\":\"ab\"}," +
                "\"Digits\":{\"type\":\"string\",\"pattern\":\"^\\\\d+$\"}," +
                "\"Letters\":{\"type\":\"string\",\"pattern\":\"^\\\\p{L}+$\"}," +
                "\"Count\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}," +
                "\"Price\":{\"type\":\"number\",\"exclusiveMinimum\":0,\"exclusiveMaximum\":100,\"multipleOf\":0.01}," +
                "\"Step\":{\"type\":\"number\",\"multipleOf\":0.1}," +
                "\"Tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1,\"maxItems\":3,\"uniqueItems\":true}," +
                "\"AnyList\":{\"type\":\"array\",\"items\":{},\"uniqueItems\":true}," +
                "\"Bag\":{\"type\":\"object\",\"minProperties\":1,\"maxProperties\":2}," +
                "\"KeyNames\":{\"type\":\"object\",\"propertyNames\":{\"pattern\":\"^[a-z]+$\"}}," +
                "\"Pat\":{\"type\":\"object\",\"patternProperties\":{\"^x-\":{\"type\":\"integer\"}}}," +
                "\"Dep\":{\"type\":\"object\",\"dependentRequired\":{\"a\":[\"b\"]}}," +
                "\"Containing\":{\"type\":\"array\",\"items\":{},\"contains\":{\"type\":\"integer\",\"minimum\":5},\"minContains\":2,\"maxContains\":3}," +
                "\"Tuple\":{\"type\":\"array\",\"prefixItems\":[{\"type\":\"string\"},{\"type\":\"integer\"}],\"items\":false}," +
                "\"Tuple2\":{\"type\":\"array\",\"prefixItems\":[{\"type\":\"string\"}],\"items\":{\"type\":\"integer\"}}," +
                "\"Rec\":{\"type\":\"object\",\"minProperties\":2,\"required\":[\"title\"],\"properties\":{" +
                "\"title\":{\"$ref\":\"#/components/schemas/Name\"}," +
                "\"alias\":{\"allOf\":[{\"$ref\":\"#/components/schemas/Name\"}],\"maxLength\":4}}}," +
                // 3.1: constraint siblings of a $ref are constraints of the reference site.
                "\"Short\":{\"$ref\":\"#/components/schemas/Name\",\"maxLength\":4}," +
                "\"TwoTags\":{\"$ref\":\"#/components/schemas/Tags\",\"minItems\":2}," +
                "\"Level\":{\"type\":\"string\",\"enum\":[\"low\",\"medium\",\"high\"],\"maxLength\":4}," +
                "\"RW\":{\"type\":\"object\",\"required\":[\"wo\"],\"properties\":{" +
                "\"ro\":{\"type\":\"string\",\"minLength\":3,\"readOnly\":true}," +
                "\"wo\":{\"type\":\"string\",\"minLength\":3,\"writeOnly\":true}," +
                "\"plain\":{\"type\":\"string\"}}}," +
                "\"Slow\":{\"type\":\"string\",\"pattern\":\"^(a|aa)+$\"}," +
                "\"Pick\":{\"oneOf\":[{\"type\":\"string\",\"minLength\":3},{\"type\":\"string\",\"maxLength\":1}]}," +
                "\"NotShort\":{\"type\":\"string\",\"not\":{\"type\":\"string\",\"maxLength\":2}}," +
                "\"FormBody\":{\"type\":\"object\",\"required\":[\"title\"],\"properties\":{" +
                "\"title\":{\"$ref\":\"#/components/schemas/Name\"},\"n\":{\"$ref\":\"#/components/schemas/Count\"}}}";
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"P6\",\"version\":\"1\"},\"servers\":[{\"url\":\"https://api.example.com\"}],\"paths\":{" +
                   string.Join(",", paths) + "},\"components\":{\"schemas\":{" + Schemas + "}}}";
        }

        // OpenAPI 3.0: exclusiveMinimum / exclusiveMaximum are booleans.
        private static string Spec30()
        {
            const string Schemas =
                "\"Range30\":{\"type\":\"number\",\"minimum\":1,\"exclusiveMinimum\":true,\"maximum\":5}," +
                "\"Open30\":{\"type\":\"integer\",\"minimum\":0,\"exclusiveMinimum\":false,\"maximum\":3,\"exclusiveMaximum\":true}";
            string path(string name) =>
                "\"/send/" + name + "\":{\"post\":{\"operationId\":\"send" + name + "\",\"requestBody\":{\"required\":true," +
                "\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/" + name + "\"}}}}," +
                "\"responses\":{\"204\":{\"description\":\"ok\"}}}}," +
                "\"/read/" + name + "\":{\"get\":{\"operationId\":\"read" + name + "\",\"responses\":{\"200\":{\"description\":\"ok\"," +
                "\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/" + name + "\"}}}}}}}";
            return "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"P6\",\"version\":\"1\"},\"servers\":[{\"url\":\"https://api.example.com\"}],\"paths\":{" +
                   path("Range30") + "," + path("Open30") + "},\"components\":{\"schemas\":{" + Schemas + "}}}";
        }

        // ---- harness -------------------------------------------------------------------------------

        private sealed class Outcome
        {
            internal Exception? Error { get; set; }

            internal int Calls { get; set; }

            internal object? Result { get; set; }
        }

        private static async Task<Outcome> Invoke(
            Assembly assembly,
            string method,
            HttpStatusCode status,
            string? body,
            params object?[] arguments)
        {
            arguments = MatrixMode.ConvertArguments(Phase4WireHarness.ClientType(assembly).GetMethod(method)!, arguments);
            var outcome = new Outcome();
            try
            {
                (object? result, _) = await Phase4BodyWireHarness.Call(
                    assembly,
                    method,
                    _ =>
                    {
                        outcome.Calls++;
                        return Phase4BodyWireHarness.Respond(status, body);
                    },
                    arguments).ConfigureAwait(false);
                outcome.Result = result;
            }
            catch (Exception exception)
            {
                outcome.Error = exception;
            }

            return outcome;
        }

        private static async Task AssertSent(string method, params object?[] arguments)
        {
            Outcome outcome = await Invoke(Assembly31.Value, method, HttpStatusCode.NoContent, null, arguments);
            Assert.Null(outcome.Error);
            Assert.Equal(1, outcome.Calls);
        }

        private static async Task<Exception> AssertRejected(string method, string keyword, params object?[] arguments)
        {
            return await AssertRejected(Assembly31.Value, method, keyword, arguments);
        }

        private static async Task<Exception> AssertRejected(
            Assembly assembly,
            string method,
            string keyword,
            params object?[] arguments)
        {
            Outcome outcome = await Invoke(assembly, method, HttpStatusCode.NoContent, null, arguments);

            // Nothing was sent, and the exception names the keyword; it is an argument error.
            Assert.Equal(0, outcome.Calls);
            ArgumentException error = Assert.IsAssignableFrom<ArgumentException>(outcome.Error);
            Assert.Contains(keyword, error.Message);
            Assert.Contains("request", error.Message);
            return error;
        }

        private static async Task AssertReadOk(string method, string json, Assembly? assembly = null)
        {
            Outcome outcome = await Invoke(assembly ?? Assembly31.Value, method, HttpStatusCode.OK, json);
            Assert.Null(outcome.Error);
            Assert.Equal(1, outcome.Calls);
        }

        private static async Task<Exception> AssertReadRejected(
            string method,
            string json,
            string keyword,
            Assembly? assembly = null)
        {
            Outcome outcome = await Invoke(assembly ?? Assembly31.Value, method, HttpStatusCode.OK, json);
            Exception error = MatrixMode.AssertSerialization(outcome.Error);
            Assert.Contains(keyword, error.Message);
            Assert.Contains("response", error.Message);
            return error;
        }

        private static object Dto(string typeName, params (string Property, object? Value)[] values)
        {
            Type type = Assembly31.Value.GetType("Generated.Phase4." + typeName, throwOnError: true)!;
            object instance = Activator.CreateInstance(type)!;
            foreach ((string property, object? value) in values)
            {
                type.GetProperty(property)!.SetValue(instance, value);
            }

            return instance;
        }

        // Rec needs two members (minProperties 2): the declared title plus one extra member.
        private static object Extra()
        {
            return MatrixMode.ExtensionDictionary(("k", "1"));
        }

        private static MatrixMode.JsonList Tokens(params object[] values)
        {
            return new MatrixMode.JsonList(values.Select(static value => JsonConvert.SerializeObject(value)).ToArray());
        }

        // ---- the generated code --------------------------------------------------------------------

        [Fact]
        public void TheSpecsGenerateAndCompileWithoutDiagnostics()
        {
            Phase4GeneratorExecution execution = Execution31.Value;

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.NotNull(Assembly30.Value);
        }

        [Fact]
        public void TheGeneratedCodeCompilesWithoutWarnings()
        {
            var warnings = Execution31.Value.OutputCompilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning &&
                                            // Only warnings in the generated files (the harness stubs and the
                                            // reference set raise unrelated ones).
                                            diagnostic.Location.SourceTree is not null &&
                                            diagnostic.Location.SourceTree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) &&
                                            // CS0108 on the response validator comparer predates P6.
                                            diagnostic.Id != "CS0108")
                .ToArray();

            Assert.Empty(warnings);
        }

        [Fact]
        public void ConstraintsAreDocumentedAndRegexesAreCachedWithoutCompiledOption()
        {
            string source = Execution31.Value.GeneratedSource;

            Assert.Contains("Must be 3..5 characters.", source);
            Assert.Contains("Must have at least 2 properties.", source);
            Assert.Contains("static readonly global::System.Text.RegularExpressions.Regex", source);
            Assert.DoesNotContain("RegexOptions.Compiled", source);
            // Every pattern of this spec is ECMAScript-compatible (the host accepts \p{L} there): no .NET-mode regex is emitted.
            Assert.Contains("RegexOptions.ECMAScript", source);
        }

        // ---- minLength / maxLength (Unicode code points) -----------------------------------------

        [Theory]
        [InlineData("abc", true)]
        [InlineData("abcde", true)]
        [InlineData("ab", false)]
        [InlineData("abcdef", false)]
        public async Task StringLengthBoundariesOnRequestAndResponse(string value, bool valid)
        {
            if (valid)
            {
                await AssertSent("sendName", value);
                await AssertReadOk("readName", JsonConvert.SerializeObject(value));
            }
            else
            {
                string keyword = value.Length < 3 ? "minLength" : "maxLength";
                await AssertRejected("sendName", keyword, value);
                await AssertReadRejected("readName", JsonConvert.SerializeObject(value), keyword);
            }
        }

        [Theory]
        // 3 and 5 code points are valid although they are 6 and 10 UTF-16 units; 2 and 6 are not.
        [InlineData(3, true)]
        [InlineData(5, true)]
        [InlineData(2, false)]
        [InlineData(6, false)]
        public async Task LengthCountsCodePointsNotUtf16Units(int codePoints, bool valid)
        {
            string value = string.Concat(Enumerable.Repeat("\U0001F600", codePoints));
            Assert.Equal(codePoints * 2, value.Length);
            if (valid)
            {
                await AssertSent("sendName", value);
                await AssertReadOk("readName", JsonConvert.SerializeObject(value));
            }
            else
            {
                string keyword = codePoints < 3 ? "minLength" : "maxLength";
                await AssertRejected("sendName", keyword, value);
                await AssertReadRejected("readName", JsonConvert.SerializeObject(value), keyword);
            }
        }

        [Fact]
        public async Task TheMessageNamesThePathAndKeywordButNotTheValue()
        {
            string longValue = new string('x', 500);

            Exception request = await AssertRejected("sendName", "maxLength", longValue);
            Exception response = await AssertReadRejected("readName", JsonConvert.SerializeObject(longValue), "maxLength");

            Assert.DoesNotContain("xxxxxxxxxx", request.Message);
            Assert.DoesNotContain("xxxxxxxxxx", response.Message);
            Assert.Contains("$", request.Message);
            Assert.Contains("$", response.Message);
        }

        [Fact]
        public async Task NestedViolationsReportThePropertyPath()
        {
            Exception request = await AssertRejected(
                "sendRec", "minLength", Dto("Rec", ("Title", "ab"), ("AdditionalProperties", Extra())));
            Assert.Contains("$.title", request.Message);

            Exception alias = await AssertRejected(
                "sendRec", "maxLength", Dto("Rec", ("Title", "abc"), ("Alias", "abcde"), ("AdditionalProperties", Extra())));
            Assert.Contains("$.alias", alias.Message);

            Exception response = await AssertReadRejected("readRec", "{\"title\":\"ab\",\"k\":1}", "minLength");
            Assert.Contains("$.title", response.Message);

            Exception arrayItem = await AssertReadRejected("readTags", "[\"a\",\"b\",\"a\"]", "uniqueItems");
            Assert.Contains("$", arrayItem.Message);
        }

        // ---- pattern ---------------------------------------------------------------------------

        [Theory]
        [InlineData("sendCode", "abc", true)]
        [InlineData("sendCode", "ABC", false)]
        [InlineData("sendCode", "ab1", false)]
        // Not anchored: the pattern may match anywhere (JSON Schema).
        [InlineData("sendPartial", "xxabxx", true)]
        [InlineData("sendPartial", "xyz", false)]
        // ECMAScript mode: \d is [0-9] only (an Arabic-Indic digit is not a digit here).
        [InlineData("sendDigits", "123", true)]
        [InlineData("sendDigits", "١٢٣", false)]
        // \p{L} is not available in ECMAScript mode, so this pattern uses the .NET semantics.
        [InlineData("sendLetters", "éa", true)]
        [InlineData("sendLetters", "1", false)]
        public async Task PatternOnRequests(string method, string value, bool valid)
        {
            if (valid)
            {
                await AssertSent(method, value);
            }
            else
            {
                await AssertRejected(method, "pattern", value);
            }
        }

        [Theory]
        [InlineData("readCode", "abc", true)]
        [InlineData("readCode", "ABC", false)]
        [InlineData("readPartial", "xxabxx", true)]
        [InlineData("readPartial", "xyz", false)]
        [InlineData("readDigits", "123", true)]
        [InlineData("readDigits", "١٢٣", false)]
        [InlineData("readLetters", "éa", true)]
        [InlineData("readLetters", "1", false)]
        public async Task PatternOnResponses(string method, string value, bool valid)
        {
            string json = JsonConvert.SerializeObject(value);
            if (valid)
            {
                await AssertReadOk(method, json);
            }
            else
            {
                await AssertReadRejected(method, json, "pattern");
            }
        }

        // ---- numbers ---------------------------------------------------------------------------

        [Theory]
        [InlineData(1, true)]
        [InlineData(10, true)]
        [InlineData(0, false)]
        [InlineData(11, false)]
        public async Task MinimumAndMaximumAreInclusive(int value, bool valid)
        {
            if (valid)
            {
                await AssertSent("sendCount", value);
                await AssertReadOk("readCount", value.ToString());
            }
            else
            {
                string keyword = value < 1 ? "minimum" : "maximum";
                await AssertRejected("sendCount", keyword, value);
                await AssertReadRejected("readCount", value.ToString(), keyword);
            }
        }

        [Theory]
        // OpenAPI 3.1: exclusiveMinimum 0, exclusiveMaximum 100 (numbers), multipleOf 0.01 (decimal arithmetic).
        [InlineData("0.01", true, "")]
        [InlineData("99.99", true, "")]
        [InlineData("19.99", true, "")]
        [InlineData("0", false, "exclusiveMinimum")]
        [InlineData("0.0", false, "exclusiveMinimum")]
        [InlineData("100", false, "exclusiveMaximum")]
        [InlineData("0.015", false, "multipleOf")]
        [InlineData("0.001", false, "multipleOf")]
        public async Task ExclusiveBoundsAndMultipleOfInOpenApi31(string text, bool valid, string keyword)
        {
            double value = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            if (valid)
            {
                await AssertSent("sendPrice", value);
                await AssertReadOk("readPrice", text);
            }
            else
            {
                await AssertRejected("sendPrice", keyword, value);
                await AssertReadRejected("readPrice", text, keyword);
            }
        }

        [Theory]
        [InlineData("0.3", true)]
        // 0.1 + 0.2 in double arithmetic: the double is rounded to 15 digits first.
        [InlineData("0.30000000000000004", true)]
        [InlineData("0.35", false)]
        [InlineData("1E+30", true)]
        public async Task MultipleOfToleratesBinaryFloatingPointNoise(string text, bool valid)
        {
            double value = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            if (valid)
            {
                await AssertSent("sendStep", value);
                await AssertReadOk("readStep", text);
            }
            else
            {
                await AssertRejected("sendStep", "multipleOf", value);
                await AssertReadRejected("readStep", text, "multipleOf");
            }
        }

        [Theory]
        // OpenAPI 3.0: exclusiveMinimum: true makes the minimum exclusive; false / absent keeps it inclusive.
        [InlineData("sendRange30", "readRange30", "1.0001", true, "")]
        [InlineData("sendRange30", "readRange30", "5", true, "")]
        [InlineData("sendRange30", "readRange30", "1", false, "exclusiveMinimum")]
        [InlineData("sendRange30", "readRange30", "5.1", false, "maximum")]
        [InlineData("sendOpen30", "readOpen30", "0", true, "")]
        [InlineData("sendOpen30", "readOpen30", "2", true, "")]
        [InlineData("sendOpen30", "readOpen30", "3", false, "exclusiveMaximum")]
        [InlineData("sendOpen30", "readOpen30", "-1", false, "minimum")]
        public async Task BooleanExclusiveFormInOpenApi30(
            string sendMethod,
            string readMethod,
            string text,
            bool valid,
            string keyword)
        {
            Assembly assembly = Assembly30.Value;
            object value = sendMethod.Contains("Range")
                ? (object)double.Parse(text, System.Globalization.CultureInfo.InvariantCulture)
                : int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            if (valid)
            {
                Outcome sent = await Invoke(assembly, sendMethod, HttpStatusCode.NoContent, null, value);
                Assert.Null(sent.Error);
                await AssertReadOk(readMethod, text, assembly);
            }
            else
            {
                await AssertRejected(assembly, sendMethod, keyword, value);
                await AssertReadRejected(readMethod, text, keyword, assembly);
            }
        }

        // ---- arrays and objects ----------------------------------------------------------------

        [Theory]
        [InlineData(1, true)]
        [InlineData(3, true)]
        [InlineData(0, false)]
        [InlineData(4, false)]
        public async Task ItemCountBoundaries(int count, bool valid)
        {
            List<string> items = Enumerable.Range(0, count).Select(static index => "t" + index).ToList();
            string json = JsonConvert.SerializeObject(items);
            if (valid)
            {
                await AssertSent("sendTags", items);
                await AssertReadOk("readTags", json);
            }
            else
            {
                string keyword = count < 1 ? "minItems" : "maxItems";
                await AssertRejected("sendTags", keyword, items);
                await AssertReadRejected("readTags", json, keyword);
            }
        }

        [Fact]
        public async Task UniqueItemsComparesJsonValues()
        {
            await AssertRejected("sendTags", "uniqueItems", new List<string> { "a", "a" });
            await AssertReadRejected("readTags", "[\"a\",\"a\"]", "uniqueItems");

            // 1 and 1.0 are the same JSON number.
            await AssertRejected("sendAnyList", "uniqueItems", Tokens(1, 1.0));
            await AssertReadRejected("readAnyList", "[1,1.0]", "uniqueItems");
            await AssertSent("sendAnyList", Tokens(1, 2, "1"));

            // Objects are equal regardless of the order of their members.
            var objects = new MatrixMode.JsonList("{\"a\":1,\"b\":2}", "{\"b\":2,\"a\":1}");
            await AssertRejected("sendAnyList", "uniqueItems", objects);
            await AssertReadRejected("readAnyList", "[{\"a\":1,\"b\":2},{\"b\":2,\"a\":1}]", "uniqueItems");

            var different = new MatrixMode.JsonList("{\"a\":1}", "{\"a\":2}");
            await AssertSent("sendAnyList", different);
            await AssertReadOk("readAnyList", "[{\"a\":1},{\"a\":2},[1],[1,2],null,true,false]");
            await AssertReadRejected("readAnyList", "[[1,2],[1,2]]", "uniqueItems");
        }

        [Theory]
        [InlineData("{}", false)]
        [InlineData("{\"a\":1}", true)]
        [InlineData("{\"a\":1,\"b\":2}", true)]
        [InlineData("{\"a\":1,\"b\":2,\"c\":3}", false)]
        public async Task PropertyCountOfAFreeFormObject(string json, bool valid)
        {
            if (valid)
            {
                await AssertSent("sendBag", MatrixMode.FreeObject(json));
                await AssertReadOk("readBag", json);
            }
            else
            {
                string keyword = json == "{}" ? "minProperties" : "maxProperties";
                await AssertRejected("sendBag", keyword, MatrixMode.FreeObject(json));
                await AssertReadRejected("readBag", json, keyword);
            }
        }

        [Fact]
        public async Task PropertyCountOfAnOpenDtoCountsDeclaredAndExtraMembers()
        {
            // Rec: minProperties 2, title is required.
            await AssertRejected("sendRec", "minProperties", Dto("Rec", ("Title", "abc")));
            await AssertReadRejected("readRec", "{\"title\":\"abc\"}", "minProperties");

            await AssertSent(
                "sendRec",
                Dto("Rec", ("Title", "abc"), ("AdditionalProperties", Extra())));
            await AssertReadOk("readRec", "{\"title\":\"abc\",\"k\":1}");
        }

        [Fact]
        public async Task PatternPropertiesValidateMatchingValues()
        {
            await AssertSent("sendPat", MatrixMode.FreeObject("{\"x-a\":1,\"other\":\"free\"}"));
            await AssertRejected("sendPat", "patternProperties", MatrixMode.FreeObject("{\"x-a\":\"text\"}"));
            await AssertReadOk("readPat", "{\"x-a\":1,\"other\":\"free\"}");
            await AssertReadRejected("readPat", "{\"x-a\":\"text\"}", "patternProperties");
        }

        [Fact]
        public async Task PropertyNamesValidateTheKeys()
        {
            await AssertSent("sendKeyNames", MatrixMode.FreeObject("{\"abc\":1}"));
            await AssertRejected("sendKeyNames", "propertyNames", MatrixMode.FreeObject("{\"ABC\":1}"));
            await AssertReadOk("readKeyNames", "{\"abc\":1,\"def\":2}");
            await AssertReadRejected("readKeyNames", "{\"abc\":1,\"Bad\":2}", "propertyNames");
        }

        [Fact]
        public async Task DependentRequiredNeedsTheListedPropertiesWhenTheTriggerIsPresent()
        {
            await AssertSent("sendDep", MatrixMode.FreeObject("{\"b\":1}"));
            await AssertSent("sendDep", MatrixMode.FreeObject("{\"a\":1,\"b\":2}"));
            await AssertRejected("sendDep", "dependentRequired", MatrixMode.FreeObject("{\"a\":1}"));
            await AssertReadOk("readDep", "{\"a\":1,\"b\":2}");
            await AssertReadRejected("readDep", "{\"a\":1}", "dependentRequired");
        }

        [Fact]
        public async Task ContainsWithMinAndMaxContains()
        {
            // contains: integer >= 5, minContains 2, maxContains 3
            await AssertSent("sendContaining", Tokens(1, "x", 6, 7));
            await AssertSent("sendContaining", Tokens(6, 7, 8));
            await AssertRejected("sendContaining", "minContains", Tokens(6, 1));
            await AssertRejected("sendContaining", "minContains", Tokens());
            await AssertRejected("sendContaining", "maxContains", Tokens(6, 7, 8, 9));
            await AssertReadOk("readContaining", "[1,\"x\",6,7]");
            await AssertReadRejected("readContaining", "[6,1]", "minContains");
            await AssertReadRejected("readContaining", "[6,7,8,9]", "maxContains");
        }

        [Fact]
        public async Task PrefixItemsValidateByPosition()
        {
            // prefixItems [string, integer], items: false closes the tuple.
            await AssertSent("sendTuple", Tokens("a", 1));
            await AssertSent("sendTuple", Tokens("a"));
            await AssertRejected("sendTuple", "prefixItems", Tokens(1, "a"));
            await AssertRejected("sendTuple", "maxItems", Tokens("a", 1, 2));
            await AssertReadOk("readTuple", "[\"a\",1]");
            await AssertReadRejected("readTuple", "[1,\"a\"]", "prefixItems");
            await AssertReadRejected("readTuple", "[\"a\",1,2]", "maxItems");

            // items applies to the elements after the prefix.
            await AssertSent("sendTuple2", Tokens("a", 1, 2));
            await AssertRejected("sendTuple2", "items", Tokens("a", "b"));
            await AssertReadOk("readTuple2", "[\"a\",1,2]");
            await AssertReadRejected("readTuple2", "[\"a\",\"b\"]", "items");
        }

        // ---- oneOf / not ------------------------------------------------------------------------

        [Theory]
        // Both members are strings; only their constraints tell them apart.
        [InlineData("\"abc\"", true)]
        [InlineData("\"abcd\"", true)]
        [InlineData("\"a\"", true)]
        [InlineData("\"ab\"", false)]
        public async Task OneOfCandidatesAreDecidedByTheirConstraints(string json, bool valid)
        {
            if (valid)
            {
                await AssertReadOk("readPick", json);
            }
            else
            {
                Exception error = MatrixMode.AssertSerialization(
                    (await Invoke(Assembly31.Value, "readPick", HttpStatusCode.OK, json)).Error);
                Assert.Contains("oneOf", error.Message);
            }
        }

        [Theory]
        [InlineData("\"abc\"", true)]
        [InlineData("\"ab\"", false)]
        [InlineData("\"\"", false)]
        public async Task NotSchemaIsDecidedByItsConstraints(string json, bool valid)
        {
            if (valid)
            {
                await AssertReadOk("readNotShort", json);
            }
            else
            {
                await AssertReadRejected("readNotShort", json, "(not)");
            }
        }

        // ---- other request locations ---------------------------------------------------------------

        [Fact]
        public async Task ParametersAreCheckedBeforeSending()
        {
            await AssertSent("queryName", "abc");
            ArgumentException error = Assert.IsAssignableFrom<ArgumentException>(
                await AssertRejected("queryName", "minLength", "ab"));
            Assert.Equal("value", error.ParamName);
        }

        [Fact]
        public async Task FormAndMultipartFieldsAreCheckedBeforeSending()
        {
            await AssertSent("postForm", Dto("FormBody", ("Title", "abc"), ("N", 5)));
            Exception form = await AssertRejected("postForm", "minLength", Dto("FormBody", ("Title", "ab")));
            Assert.Contains("$.title", form.Message);
            await AssertRejected("postForm", "maximum", Dto("FormBody", ("Title", "abc"), ("N", 11)));

            await AssertSent("postMultipart", Dto("FormBody", ("Title", "abc")));
            Exception multipart = await AssertRejected("postMultipart", "minLength", Dto("FormBody", ("Title", "ab")));
            Assert.Contains("$.title", multipart.Message);
        }

        [Fact]
        public async Task ResponseHeadersAreChecked()
        {
            Outcome valid = await Invoke(
                Assembly31.Value, "readWithHeader", HttpStatusCode.OK, "\"x\"");
            Assert.NotNull(valid.Error);
            Assert.Contains("X-Count", valid.Error!.Message);

            Outcome ok = await InvokeWithHeader("5");
            Assert.Null(ok.Error);

            Outcome bad = await InvokeWithHeader("0");
            Exception error = MatrixMode.AssertSerialization(bad.Error);
            Assert.Contains("minimum", error.Message);
            Assert.Contains("X-Count", error.Message);
        }

        private static async Task<Outcome> InvokeWithHeader(string value)
        {
            var outcome = new Outcome();
            try
            {
                (object? result, _) = await Phase4BodyWireHarness.Call(
                    Assembly31.Value,
                    "readWithHeader",
                    _ =>
                    {
                        outcome.Calls++;
                        return Phase4BodyWireHarness.Respond(
                            HttpStatusCode.OK, "\"x\"", "application/json", ("X-Count", value));
                    }).ConfigureAwait(false);
                outcome.Result = result;
            }
            catch (Exception exception)
            {
                outcome.Error = exception;
            }

            return outcome;
        }

        // ---- review follow-ups ---------------------------------------------------------------------

        [Fact]
        public async Task EnumConstraintsAreCheckedOnTheWireValue()
        {
            // maxLength 4 on an enum: "low" / "high" fit, "medium" does not.
            Type level = Assembly31.Value.GetType("Generated.Phase4.Level", throwOnError: true)!;
            await AssertSent("sendLevel", Enum.Parse(level, "Low"));
            await AssertRejected("sendLevel", "maxLength", Enum.Parse(level, "Medium"));
            await AssertReadOk("readLevel", "\"high\"");
            await AssertReadRejected("readLevel", "\"medium\"", "maxLength");
        }

        [Fact]
        public async Task ConstraintSiblingsOfA31RefAreConstraintsOfTheReferenceSite()
        {
            // Name allows 3..5 characters, the sibling maxLength 4 narrows it; no new type is generated.
            await AssertSent("sendShort", "abcd");
            await AssertRejected("sendShort", "maxLength", "abcde");
            await AssertRejected("sendShort", "minLength", "ab");
            await AssertReadOk("readShort", "\"abcd\"");
            await AssertReadRejected("readShort", "\"abcde\"", "maxLength");
            Assert.Null(Assembly31.Value.GetType("Generated.Phase4.Short"));

            await AssertSent("sendTwoTags", new List<string> { "a", "b" });
            await AssertRejected("sendTwoTags", "minItems", new List<string> { "a" });
            await AssertReadRejected("readTwoTags", "[\"a\"]", "minItems");
        }

        [Fact]
        public async Task ReadOnlyIsNotCheckedOnRequestsAndWriteOnlyMayBeMissingFromResponses()
        {
            // readOnly: never sent, so its (invalid) value does not matter.
            await AssertSent("sendRW", Dto("RW", ("Ro", "a"), ("Wo", "abc")));
            // writeOnly: checked on requests.
            Exception request = await AssertRejected("sendRW", "minLength", Dto("RW", ("Wo", "a")));
            Assert.Contains("$.wo", request.Message);

            // Response: a missing writeOnly property is allowed, the readOnly one is checked,
            // and a writeOnly property that is present is checked too.
            await AssertReadOk("readRW", "{\"ro\":\"abc\"}");
            await AssertReadRejected("readRW", "{\"ro\":\"ab\"}", "minLength");
            await AssertReadRejected("readRW", "{\"ro\":\"abc\",\"wo\":\"a\"}", "minLength");
        }

        [Fact]
        public async Task TextBodiesAreChecked()
        {
            await AssertSent("sendText", "abc");
            await AssertRejected("sendText", "minLength", "ab");

            Outcome ok = await InvokeText("abc");
            Assert.Null(ok.Error);
            Outcome bad = await InvokeText("ab");
            Exception error = MatrixMode.AssertSerialization(bad.Error);
            Assert.Contains("minLength", error.Message);
        }

        private static async Task<Outcome> InvokeText(string body)
        {
            var outcome = new Outcome();
            try
            {
                await Phase4BodyWireHarness.Call(
                    Assembly31.Value,
                    "readText",
                    _ =>
                    {
                        outcome.Calls++;
                        return Phase4BodyWireHarness.Respond(HttpStatusCode.OK, body, "text/plain");
                    }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                outcome.Error = exception;
            }

            return outcome;
        }

        private static bool HasTimeout(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is System.Text.RegularExpressions.RegexMatchTimeoutException)
                {
                    return true;
                }
            }

            return false;
        }

        [Fact]
        public async Task AMatchTimeoutIsAValidationErrorWithTheTimeoutAsInnerException()
        {
            string value = new string('a', 60) + "b";

            Exception request = await AssertRejected("sendSlow", "pattern", value);
            Assert.True(HasTimeout(request));

            Exception response = await AssertReadRejected("readSlow", JsonConvert.SerializeObject(value), "pattern");
            Assert.True(HasTimeout(response));
        }

        [Fact]
        public async Task MessagesNeverCarryValuesOrDataKeys()
        {
            Exception names = await AssertReadRejected("readKeyNames", "{\"SecretKeyName\":1}", "propertyNames");
            Assert.DoesNotContain("SecretKeyName", names.Message);
            Exception nested = await AssertReadRejected("readPick", "\"ab\"", "oneOf");
            Assert.DoesNotContain("ab\"", nested.Message);
            Exception pattern = await AssertReadRejected("readPat", "{\"x-a\":\"SecretValue\"}", "patternProperties");
            Assert.DoesNotContain("SecretValue", pattern.Message);
            Assert.Null(pattern.InnerException);
        }

        [Fact]
        public void APatternThatNeedsDotNetSyntaxIsReportedAndEvaluatedInTheDotNetMode()
        {
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(
                DocumentWithComponent("{\"type\":\"string\",\"pattern\":\"(?i)^ab$\"}"));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Contains("/components/schemas/Payload/pattern'", warning.GetMessage());
            Assert.Contains("ECMAScript", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
            Assert.DoesNotContain("RegexOptions.Compiled", execution.GeneratedSource);
        }

        [Fact]
        public void ValidationKeywordsOnAFilePartAreReportedAsIgnored()
        {
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(
                DocumentWithComponent(
                    "{\"type\":\"object\",\"properties\":{\"file\":{\"type\":\"string\",\"format\":\"binary\",\"minLength\":3}}}"));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Contains("/components/schemas/Payload/properties/file/minLength'", warning.GetMessage());
        }

        [Fact]
        public void ARefWithAConstraintSiblingIsStillRejectedInOpenApi30()
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"x\",\"version\":\"1\"},\"paths\":{},\"components\":{\"schemas\":{" +
                "\"Name\":{\"type\":\"string\"},\"Payload\":{\"$ref\":\"#/components/schemas/Name\",\"maxLength\":4}}}}");

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("/components/schemas/Payload/maxLength'", diagnostic.GetMessage());
        }

        [Fact]
        public void NoValidationCodeIsGeneratedWithoutConstraints()
        {
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"x\",\"version\":\"1\"},\"paths\":{\"/a\":{\"post\":{\"operationId\":\"sendA\"," +
                "\"parameters\":[{\"name\":\"q\",\"in\":\"query\",\"schema\":{\"type\":\"string\"}}]," +
                "\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/A\"}}}}," +
                "\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/A\"}}}}}}}}," +
                "\"components\":{\"schemas\":{\"A\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"},\"n\":{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}}}}}}");

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.DoesNotContain("__OacgConstraint_", execution.GeneratedSource);
        }

        // ---- diagnostics ---------------------------------------------------------------------------

        private static string DocumentWithComponent(string schema, string version = "3.1.0")
        {
            return "{\"openapi\":\"" + version + "\",\"info\":{\"title\":\"Schema\",\"version\":\"1\"}," +
                   "\"paths\":{},\"components\":{\"schemas\":{\"Payload\":" + schema + "}}}";
        }

        private static Diagnostic GetOnlyDiagnostic(string document)
        {
            return Assert.Single(MatrixMode.GenerateAndCompile(document).RunResult.Diagnostics);
        }

        [Theory]
        [InlineData("3.0.3", "{\"type\":\"string\",\"pattern\":\"(\"}", "/components/schemas/Payload/pattern'")]
        [InlineData("3.1.0", "{\"type\":\"string\",\"pattern\":\"[a-\"}", "/components/schemas/Payload/pattern'")]
        [InlineData("3.1.0", "{\"type\":\"object\",\"patternProperties\":{\"(\":{\"type\":\"string\"}}}", "/components/schemas/Payload/patternProperties/")]
        [InlineData("3.0.3", "{\"type\":\"string\",\"minLength\":-1}", "/components/schemas/Payload/minLength'")]
        [InlineData("3.0.3", "{\"type\":\"string\",\"maxLength\":\"3\"}", "/components/schemas/Payload/maxLength'")]
        [InlineData("3.0.3", "{\"type\":\"number\",\"multipleOf\":0}", "/components/schemas/Payload/multipleOf'")]
        [InlineData("3.0.3", "{\"type\":\"number\",\"minimum\":1,\"exclusiveMinimum\":1}", "/components/schemas/Payload/exclusiveMinimum'")]
        [InlineData("3.1.0", "{\"type\":\"number\",\"minimum\":1,\"exclusiveMinimum\":true}", "/components/schemas/Payload/exclusiveMinimum'")]
        [InlineData("3.0.3", "{\"type\":\"array\",\"items\":{},\"uniqueItems\":\"yes\"}", "/components/schemas/Payload/uniqueItems'")]
        [InlineData("3.1.0", "{\"type\":\"object\",\"dependentRequired\":{\"a\":\"b\"}}", "/components/schemas/Payload/dependentRequired/a'")]
        public void InvalidKeywordValuesAreInvalidDocuments(string version, string schema, string logicalPath)
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(DocumentWithComponent(schema, version));

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains(logicalPath, diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("3.1.0", "{\"type\":\"string\",\"if\":{\"minLength\":1}}", "/components/schemas/Payload/if'")]
        [InlineData("3.1.0", "{\"type\":\"object\",\"dependentSchemas\":{\"a\":{}}}", "/components/schemas/Payload/dependentSchemas'")]
        [InlineData("3.1.0", "{\"type\":\"object\",\"unevaluatedProperties\":false}", "/components/schemas/Payload/unevaluatedProperties'")]
        [InlineData("3.1.0", "{\"type\":\"array\",\"items\":{},\"unevaluatedItems\":false}", "/components/schemas/Payload/unevaluatedItems'")]
        [InlineData("3.0.3", "{\"type\":\"object\",\"patternProperties\":{\"^a\":{}}}", "/components/schemas/Payload/patternProperties'")]
        [InlineData("3.1.0", "{\"oneOf\":[{\"type\":\"string\"},{\"type\":\"integer\"}],\"minLength\":2}", "/components/schemas/Payload/minLength'")]
        [InlineData("3.1.0", "{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\",\"contains\":{},\"else\":{}}}}", "/components/schemas/Payload/properties/a/else'")]
        public void KeywordsOutsideTheScopeAreUnsupportedElements(string version, string schema, string logicalPath)
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(DocumentWithComponent(schema, version));

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains(logicalPath, diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("3.0.3", "{\"type\":\"integer\",\"minLength\":2,\"pattern\":\"^a\",\"minItems\":1,\"uniqueItems\":true}")]
        [InlineData("3.1.0", "{\"type\":\"string\",\"minimum\":1,\"maxProperties\":1,\"contains\":{\"type\":\"string\"},\"prefixItems\":[{}]}")]
        [InlineData("3.1.0", "{\"type\":\"array\",\"items\":{},\"contains\":true,\"propertyNames\":{\"pattern\":\"^a\"},\"minContains\":0}")]
        public void KeywordsThatDoNotApplyToTheTypeAreIgnored(string version, string schema)
        {
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(
                DocumentWithComponent(schema, version));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void ASchemaThatContainsItselfThroughAValidationKeywordIsACyclicReference()
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(DocumentWithComponent(
                "{\"type\":\"array\",\"items\":{},\"contains\":{\"$ref\":\"#/components/schemas/Payload\"}}"));

            Assert.Contains("cyclic", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ADtoMayReferToItselfInAValidationKeyword()
        {
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(DocumentWithComponent(
                "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}}," +
                "\"patternProperties\":{\"^child-\":{\"$ref\":\"#/components/schemas/Payload\"}}}"));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }
    }

    /// <summary>The System.Text.Json run of every test of <see cref="Phase6ConstraintTests"/>.</summary>
    public sealed class Phase6ConstraintTestsSystemTextJson : Phase6ConstraintTests
    {
        public Phase6ConstraintTestsSystemTextJson()
            : base(SerializerMatrixHarness.SystemTextJson)
        {
        }

        protected override bool ExpectsSystemTextJson => true;
    }
}
