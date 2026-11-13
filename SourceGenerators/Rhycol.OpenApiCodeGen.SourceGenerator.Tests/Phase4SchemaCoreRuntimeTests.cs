using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P1 schema core: every feature is generated, compiled, then exercised through a fake
    /// HttpMessageHandler so the wire JSON and the deserialized result are both asserted.
    /// </summary>
    public sealed class Phase4SchemaCoreRuntimeTests
    {
        private const string Ns = "Generated.Phase4.";

        // ---- G04 recursion ------------------------------------------------------------------

        [Fact]
        public async Task RecursiveDtoRoundTripsThroughPropertyItemsAndMap()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""required"":[""name""],
              ""properties"":{
                ""name"":{""type"":""string""},
                ""children"":{""type"":""array"",""items"":{""$ref"":""#/components/schemas/Body""}},
                ""parent"":{""$ref"":""#/components/schemas/Body""},
                ""byName"":{""type"":""object"",""additionalProperties"":{""$ref"":""#/components/schemas/Body""}}}}";
            const string Json = @"{""name"":""root"",
              ""children"":[{""name"":""c1"",""children"":[{""name"":""c2""}]}],
              ""parent"":{""name"":""p"",""parent"":{""name"":""gp""}},
              ""byName"":{""k"":{""name"":""v"",""byName"":{""deep"":{""name"":""d""}}}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, Json, Json);

            AssertJsonEqual(Json, exchange.RequestJson);
            AssertJsonEqual(Json, JsonConvert.SerializeObject(exchange.Result));
            Assert.Equal("gp", ReadPath(exchange.Result!, "Parent", "Parent", "Name"));
        }

        [Fact]
        public async Task MutuallyRecursiveDtosRoundTrip()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""properties"":{""b"":{""$ref"":""#/components/schemas/Other""},""id"":{""type"":""integer""}}},
              ""Other"":{""type"":""object"",""additionalProperties"":false,
              ""properties"":{""a"":{""$ref"":""#/components/schemas/Body""},""tags"":{""type"":""array"",""items"":{""$ref"":""#/components/schemas/Other""}}}}";
            const string Json = @"{""id"":1,""b"":{""a"":{""id"":2,""b"":{""tags"":[{""tags"":[]}]}}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, Json, Json);

            AssertJsonEqual(Json, exchange.RequestJson);
            Assert.Equal(2, ReadPath(exchange.Result!, "B", "A", "Id"));
        }

        [Fact]
        public async Task RecursiveResponseStillRejectsUndeclaredPropertyAtDepth()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""properties"":{""child"":{""$ref"":""#/components/schemas/Body""},""id"":{""type"":""integer""}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(() =>
                Echo(execution, "{\"id\":1}", "{\"id\":1,\"child\":{\"child\":{\"surprise\":true}}}"));

            Assert.Contains("$.child.child.surprise", exception.Message);
        }

        [Fact]
        public void ArrayOnlyCycleReportsOacg103AtTheReference()
        {
            const string Schemas = @"""Body"":{""type"":""array"",""items"":{""$ref"":""#/components/schemas/Body""}}";

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG103", diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Body/items'", diagnostic.GetMessage());
        }

        [Fact]
        public void MapOnlyCycleReportsOacg103()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":{""$ref"":""#/components/schemas/Body""}}";

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG103", diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Body/additionalProperties'", diagnostic.GetMessage());
        }

        [Fact]
        public void PureAliasCycleThroughAContainerFreePathRemainsOacg103()
        {
            const string Schemas = @"""Body"":{""$ref"":""#/components/schemas/Other""},""Other"":{""$ref"":""#/components/schemas/Body""}";

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG103", diagnostic.Id);
            Assert.Contains("cyclic internal $ref", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Other/$ref'", diagnostic.GetMessage());
        }

        // ---- G01 open objects ---------------------------------------------------------------

        [Theory]
        [InlineData("")]
        [InlineData(",\"additionalProperties\":true")]
        public async Task OpenDtoKeepsUnknownPropertiesAndSendsThemBack(string additionalProperties)
        {
            string schemas = @"""Body"":{""type"":""object"",""required"":[""id""],
              ""properties"":{""id"":{""type"":""integer""}}" + additionalProperties + "}";
            const string Json = @"{""id"":1,""extra"":{""x"":[1,2,{""y"":null}]},""flag"":true,""when"":""2020-01-01T00:00:00Z""}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(schemas));

            Exchange exchange = await Echo(execution, Json, Json);

            AssertJsonEqual(Json, exchange.RequestJson);
            var extra = (IDictionary<string, JToken>)exchange.Result!.GetType()
                .GetProperty("AdditionalProperties")!.GetValue(exchange.Result)!;
            Assert.Equal(3, extra.Count);
            Assert.Equal(2, extra["extra"]["x"]![1]!.Value<int>());
            Assert.True(extra["flag"].Value<bool>());
            // Dates stay as the raw string; the response reader never converts them.
            Assert.Equal(JTokenType.String, extra["when"].Type);
            Assert.Equal("2020-01-01T00:00:00Z", extra["when"].Value<string>());
            AssertJsonEqual(Json, JsonConvert.SerializeObject(exchange.Result));
        }

        [Fact]
        public async Task ExplicitlyClosedDtoStillRejectsUnknownResponseProperties()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""properties"":{""id"":{""type"":""integer""}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(() =>
                Echo(execution, "{\"id\":1}", "{\"id\":1,\"extra\":1}"));

            Assert.Contains("Undeclared response property", exception.Message);
            Assert.DoesNotContain("AdditionalProperties", execution.GeneratedSource);
        }

        [Fact]
        public void ExtensionMemberNameAvoidsADeclaredAdditionalPropertiesProperty()
        {
            const string Schemas = @"""Body"":{""type"":""object"",
              ""properties"":{""additionalProperties"":{""type"":""string""}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type body = Load(execution).GetType(Ns + "Body", true)!;

            Assert.Equal(typeof(string), body.GetProperty("AdditionalProperties")!.PropertyType);
            Assert.NotNull(body.GetProperty("AdditionalProperties2"));
        }

        // ---- G02 map / free-form / typed extension data ---------------------------------------

        [Fact]
        public async Task MapRoundTripsAndValidatesValues()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":{""type"":""integer""}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, "{\"a\":1,\"b\":2}", "{\"a\":3,\"b\":4}");

            AssertJsonEqual("{\"a\":1,\"b\":2}", exchange.RequestJson);
            var result = Assert.IsType<Dictionary<string, int>>(exchange.Result);
            Assert.Equal(4, result["b"]);
            JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(() =>
                Echo(execution, "{\"a\":1}", "{\"a\":\"nope\"}"));
            Assert.Contains("$.a", exception.Message);
        }

        [Fact]
        public async Task MapOfDtoRejectsNullValueAndNestedUndeclaredProperty()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":{""$ref"":""#/components/schemas/Item""}},
              ""Item"":{""type"":""object"",""additionalProperties"":false,""properties"":{""n"":{""type"":""number"",""format"":""double""}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange ok = await Echo(execution, "{\"a\":{\"n\":1.5}}", "{\"a\":{\"n\":2.5}}");
            Assert.Equal("{\"a\":{\"n\":2.5}}", JsonConvert.SerializeObject(ok.Result));
            await Assert.ThrowsAsync<JsonSerializationException>(() =>
                Echo(execution, "{}", "{\"a\":null}"));
            JsonSerializationException nested = await Assert.ThrowsAsync<JsonSerializationException>(() =>
                Echo(execution, "{}", "{\"a\":{\"zzz\":1}}"));
            Assert.Contains("$.a.zzz", nested.Message);
        }

        [Fact]
        public async Task MapRejectsNonFiniteRequestValue()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":{""type"":""number"",""format"":""double""}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type clientType = Load(execution).GetType(Ns + "Phase4Api", true)!;
            var dictionary = new Dictionary<string, double> { ["x"] = double.NaN };

            using var httpClient = new HttpClient(new StubHandler("{}"));
            object client = Activator.CreateInstance(clientType, httpClient)!;
            var task = (Task)clientType.GetMethod("echo")!.Invoke(
                client, new object?[] { dictionary, CancellationToken.None })!;

            await Assert.ThrowsAsync<JsonSerializationException>(() => task);
        }

        [Fact]
        public async Task TypedAdditionalPropertiesOnDtoAreValidatedInResponses()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""properties"":{""id"":{""type"":""integer""}},
              ""additionalProperties"":{""type"":""integer""}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, "{\"id\":1,\"a\":5}", "{\"id\":1,\"a\":6,\"b\":7}");
            var extra = (IDictionary<string, JToken>)exchange.Result!.GetType()
                .GetProperty("AdditionalProperties")!.GetValue(exchange.Result)!;
            Assert.Equal(7, extra["b"].Value<int>());
            AssertJsonEqual("{\"id\":1,\"a\":5}", exchange.RequestJson);

            JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(() =>
                Echo(execution, "{\"id\":1}", "{\"id\":1,\"a\":\"x\"}"));
            Assert.Contains("$.a", exception.Message);
        }

        [Fact]
        public async Task FreeFormObjectIsAJObject()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""properties"":{""meta"":{""type"":""object""}},""additionalProperties"":false}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type body = Load(execution).GetType(Ns + "Body", true)!;
            Assert.Equal(typeof(JObject), body.GetProperty("Meta")!.PropertyType);

            Exchange exchange = await Echo(execution, "{\"meta\":{\"a\":[1,{\"b\":null}]}}", "{\"meta\":{\"z\":1}}");

            AssertJsonEqual("{\"meta\":{\"a\":[1,{\"b\":null}]}}", exchange.RequestJson);
            Assert.Equal(1, ((JObject)ReadPath(exchange.Result!, "Meta")!)["z"]!.Value<int>());
            await Assert.ThrowsAsync<JsonSerializationException>(() =>
                Echo(execution, "{}", "{\"meta\":[1]}"));
        }

        [Fact]
        public async Task TopLevelFreeFormObjectResponseIsAJObject()
        {
            const string Schemas = @"""Body"":{""type"":""object""}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, "{\"k\":\"v\"}", "{\"x\":{\"y\":2}}");

            AssertJsonEqual("{\"k\":\"v\"}", exchange.RequestJson);
            Assert.Equal(2, ((JObject)exchange.Result!)["x"]!["y"]!.Value<int>());
        }

        // ---- G03 type inference / any ---------------------------------------------------------

        [Theory]
        [InlineData("3.1.0", "{}")]
        [InlineData("3.0.3", "{}")]
        public async Task EmptySchemaIsAnyJToken(string version, string schema)
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument("\"Body\":" + schema, version));

            Exchange exchange = await Echo(execution, "{\"a\":[1,\"two\",null]}", "[true,{\"b\":1.5}]");

            AssertJsonEqual("{\"a\":[1,\"two\",null]}", exchange.RequestJson);
            Assert.IsAssignableFrom<JToken>(exchange.Result);
            AssertJsonEqual("[true,{\"b\":1.5}]", ((JToken)exchange.Result!).ToString(Formatting.None));
        }

        [Fact]
        public async Task JTokenHoldingADateTimeKeepsItsTimeOfDay()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument("\"Body\":{}"));
            var token = new JObject { ["t"] = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc) };

            Exchange exchange = await Echo(execution, (object?)token, "{}");

            // The date-only converter used for format: date members must not truncate JToken values.
            Assert.Equal("{\"t\":\"2020-01-02T03:04:05Z\"}", exchange.RequestJson);
        }

        [Fact]
        public async Task AnyResponseAcceptsJsonNull()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument("\"Body\":{}"));

            Exchange exchange = await Echo(execution, "1", "null");

            Assert.Equal("1", exchange.RequestJson);
            Assert.True(exchange.Result is null || ((JToken)exchange.Result).Type == JTokenType.Null);
        }

        [Fact]
        public async Task BooleanTrueSchemaAndMultiTypeArrayBecomeJToken()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""properties"":{
                ""anything"":true,
                ""mixed"":{""type"":[""string"",""integer""]},
                ""maybe"":{""type"":[""string"",""integer"",""null""]}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type body = Load(execution).GetType(Ns + "Body", true)!;
            Assert.Equal(typeof(JToken), body.GetProperty("Anything")!.PropertyType);
            Assert.Equal(typeof(JToken), body.GetProperty("Mixed")!.PropertyType);
            Assert.Equal(typeof(JToken), body.GetProperty("Maybe")!.PropertyType);

            const string Json = "{\"anything\":{\"a\":1},\"mixed\":7,\"maybe\":null}";
            Exchange exchange = await Echo(execution, Json, Json);

            AssertJsonEqual(Json, exchange.RequestJson);
            Assert.Equal(7, ((JToken)ReadPath(exchange.Result!, "Mixed")!).Value<int>());
        }

        [Fact]
        public void FalseBooleanSchemaIsReportedAtItsPath()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""properties"":{""never"":false}}";

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("'false' boolean schema", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/properties/never'", diagnostic.GetMessage());
        }

        [Fact]
        public void BooleanSchemaIsNotAValidSchemaInOpenApi30()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""properties"":{""anything"":true}}";

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(EchoDocument(Schemas, "3.0.3")).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Body/properties/anything'", diagnostic.GetMessage());
        }

        [Fact]
        public void TypeIsInferredFromPropertiesItemsAndEnumValues()
        {
            const string Schemas = @"""Body"":{""additionalProperties"":false,
                ""properties"":{
                  ""list"":{""items"":{""type"":""string""}},
                  ""mode"":{""enum"":[""on"",""off""]},
                  ""level"":{""enum"":[1,2]},
                  ""nested"":{""properties"":{""z"":{""type"":""boolean""}},""additionalProperties"":false}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);
            Type body = assembly.GetType(Ns + "Body", true)!;

            Assert.Equal(typeof(List<string>), body.GetProperty("List")!.PropertyType);
            Assert.True(Nullable.GetUnderlyingType(body.GetProperty("Mode")!.PropertyType)!.IsEnum);
            Assert.Equal(new[] { "Off", "On" }, Enum.GetNames(Nullable.GetUnderlyingType(body.GetProperty("Mode")!.PropertyType)!));
            Assert.Equal(new[] { "Value1", "Value2" }, Enum.GetNames(Nullable.GetUnderlyingType(body.GetProperty("Level")!.PropertyType)!));
            Assert.Equal("BodyNested", body.GetProperty("Nested")!.PropertyType.Name);
        }

        [Fact]
        public void ConstInfersItsTypeWhenTypeIsOmitted()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
                ""properties"":{""kind"":{""const"":""pet""},""max"":{""const"":3}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type body = Load(execution).GetType(Ns + "Body", true)!;

            Assert.Equal(typeof(string), body.GetProperty("Kind")!.PropertyType);
            Assert.Equal(typeof(int?), body.GetProperty("Max")!.PropertyType);
        }

        // ---- G10 pointers --------------------------------------------------------------------

        [Fact]
        public async Task SchemaRefMayTargetAPropertyOfAnotherSchema()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""properties"":{
                ""a"":{""type"":""object"",""additionalProperties"":false,""properties"":{""x"":{""type"":""string""}}},
                ""b"":{""$ref"":""#/components/schemas/Body/properties/a""}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);

            // The pointer's last token names the type; the inline property keeps its own name.
            Assert.NotNull(assembly.GetType(Ns + "A"));
            Assert.NotNull(assembly.GetType(Ns + "BodyA"));
            Assert.Equal(assembly.GetType(Ns + "A"), assembly.GetType(Ns + "Body")!.GetProperty("B")!.PropertyType);

            const string Json = "{\"a\":{\"x\":\"1\"},\"b\":{\"x\":\"2\"}}";
            Exchange exchange = await Echo(execution, Json, Json);

            AssertJsonEqual(Json, exchange.RequestJson);
            Assert.Equal("2", ReadPath(exchange.Result!, "B", "X"));
        }

        [Fact]
        public async Task SchemaRefMayTargetADefsEntryInsideASchema()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""$defs"":{""Part"":{""type"":""object"",""additionalProperties"":false,
                          ""properties"":{""n"":{""type"":""integer""}}}},
              ""properties"":{""part"":{""$ref"":""#/components/schemas/Body/$defs/Part""},
                              ""parts"":{""type"":""array"",""items"":{""$ref"":""#/components/schemas/Body/$defs/Part""}}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assert.NotNull(Load(execution).GetType(Ns + "Part"));

            const string Json = "{\"part\":{\"n\":1},\"parts\":[{\"n\":2},{\"n\":3}]}";
            Exchange exchange = await Echo(execution, Json, Json);

            AssertJsonEqual(Json, exchange.RequestJson);
            AssertJsonEqual(Json, JsonConvert.SerializeObject(exchange.Result));
        }

        [Fact]
        public void PointerTypeNameCollidingWithAComponentUsesTheCollisionWarning()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""$defs"":{""Pet"":{""type"":""object"",""additionalProperties"":false,
                          ""properties"":{""n"":{""type"":""integer""}}}},
              ""properties"":{""pet"":{""$ref"":""#/components/schemas/Body/$defs/Pet""},
                              ""other"":{""$ref"":""#/components/schemas/Pet""}}},
              ""Pet"":{""type"":""object"",""additionalProperties"":false,""properties"":{""s"":{""type"":""string""}}}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(EchoDocument(Schemas));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG107", warning.Id);
            Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = Load(execution);
            Assert.NotNull(assembly.GetType(Ns + "Pet"));
            Assert.NotNull(assembly.GetType(Ns + "Pet2"));
        }

        [Fact]
        public async Task ExternalFragmentTargetsAnyPointerAndMayRecurse()
        {
            const string RootPointer =
                "/paths/~1tag/get/responses/200/content/application~1json/schema/$ref";
            const string Root = @"{""openapi"":""3.1.0"",""info"":{""title"":""Ext"",""version"":""1""},""servers"":[{""url"":""https://api.example.test""}],
              ""paths"":{""/tag"":{""get"":{""operationId"":""getTag"",""responses"":{""200"":{""description"":""OK"",
                ""content"":{""application/json"":{""schema"":{""$ref"":""ext.json#/definitions/Tag""}}}}}}}}}";
            const string External = @"{""definitions"":{
              ""Tag"":{""type"":""object"",""additionalProperties"":false,
                ""properties"":{""name"":{""type"":""string""},""parent"":{""$ref"":""#/definitions/Tag""},
                                ""inner"":{""$ref"":""#/definitions/Tag/properties/name""}}}}}";
            string bundle = TestBundleFactory.CreateV2(
                Root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec("Assets/ext.json", "Assets/ext.json", "json", External)
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "Assets/ext.json", "/definitions/Tag/properties/inner/$ref", "Assets/ext.json",
                        "/definitions/Tag/properties/name"),
                    new TestBundleFactory.V2ReferenceSpec(
                        "Assets/ext.json", "/definitions/Tag/properties/parent/$ref", "Assets/ext.json", "/definitions/Tag"),
                    new TestBundleFactory.V2ReferenceSpec("root", RootPointer, "Assets/ext.json", "/definitions/Tag")
                });
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Root, bundle: bundle);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = Load(execution);
            Type tag = assembly.GetType(Ns + "Tag", true)!;
            Assert.Equal(tag, tag.GetProperty("Parent")!.PropertyType);

            var handler = new StubHandler("{\"name\":\"a\",\"parent\":{\"name\":\"b\",\"inner\":\"c\"},\"inner\":\"d\"}");
            using var httpClient = new HttpClient(handler);
            Type clientType = assembly.GetType(Ns + "Phase4Api", true)!;
            object client = Activator.CreateInstance(clientType, httpClient)!;
            var task = (Task)clientType.GetMethod("getTag")!.Invoke(client, new object?[] { CancellationToken.None })!;
            await task;
            object result = task.GetType().GetProperty("Result")!.GetValue(task)!;

            Assert.Equal("b", ReadPath(result, "Parent", "Name"));
            Assert.Equal("d", ReadPath(result, "Inner"));
        }

        // ---- G11 enums -----------------------------------------------------------------------

        [Fact]
        public async Task IntegerEnumUsesNumericWireValuesAndDeclaredMemberNames()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""required"":[""priority""],
              ""properties"":{""priority"":{""$ref"":""#/components/schemas/Priority""}}},
              ""Priority"":{""type"":""integer"",""enum"":[1,2,3],
                ""x-enum-varnames"":[""Low"",""Medium"",""High""],
                ""x-enum-descriptions"":[""Not urgent"",""Normal"",""Drop everything""]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);
            Type priority = assembly.GetType(Ns + "Priority", true)!;

            Assert.Equal(new[] { "Low", "Medium", "High" }, Enum.GetNames(priority));
            Assert.Equal(
                new[] { 1, 2, 3 },
                Enum.GetValues(priority).Cast<object>().Select(Convert.ToInt32).ToArray());
            Assert.Contains("Drop everything", execution.GeneratedSource);

            object body = Activator.CreateInstance(assembly.GetType(Ns + "Body", true)!)!;
            body.GetType().GetProperty("Priority")!.SetValue(body, Enum.ToObject(priority, 2));
            Exchange exchange = await Echo(execution, body, "{\"priority\":3}");

            AssertJsonEqual("{\"priority\":2}", exchange.RequestJson);
            Assert.Equal("High", ReadPath(exchange.Result!, "Priority")!.ToString());
            foreach (string badWire in new[] { "\"3\"", "4", "2.5", "2.0", "1e0", "null", "true" })
            {
                await Assert.ThrowsAsync<JsonSerializationException>(() =>
                    Echo(execution, body, "{\"priority\":" + badWire + "}"));
            }
        }

        [Fact]
        public void IntegerEnumWithoutNamesAndWithNegativeValuesGetsStableNames()
        {
            const string Schemas = @"""Body"":{""type"":""integer"",""enum"":[1,-1,0]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type body = Load(execution).GetType(Ns + "Body", true)!;

            Assert.Equal(
                new[] { "Value0", "Value1", "ValueMinus1" },
                Enum.GetNames(body).OrderBy(static name => name, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void LargeIntegerEnumUsesALongUnderlyingType()
        {
            const string Schemas = @"""Body"":{""type"":""integer"",""format"":""int64"",""enum"":[1,9000000000]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type body = Load(execution).GetType(Ns + "Body", true)!;

            Assert.Equal(typeof(long), Enum.GetUnderlyingType(body));
        }

        [Fact]
        public async Task EnumContainingNullIsNullable()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""required"":[""state"",""level""],
              ""properties"":{
                ""state"":{""type"":[""string"",""null""],""enum"":[""on"",""off"",null]},
                ""level"":{""type"":[""integer"",""null""],""enum"":[1,2,null]}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type body = Load(execution).GetType(Ns + "Body", true)!;
            Assert.NotNull(Nullable.GetUnderlyingType(body.GetProperty("State")!.PropertyType));
            Assert.NotNull(Nullable.GetUnderlyingType(body.GetProperty("Level")!.PropertyType));

            Exchange exchange = await Echo(
                execution,
                "{\"state\":null,\"level\":null}",
                "{\"state\":\"off\",\"level\":null}");

            AssertJsonEqual("{\"state\":null,\"level\":null}", exchange.RequestJson);
            Assert.Equal("Off", ReadPath(exchange.Result!, "State")!.ToString());
            Assert.Null(ReadPath(exchange.Result!, "Level"));
        }

        [Fact]
        public async Task EnumWithoutTypeIsInferredFromItsValuesAndHonoursVarnames()
        {
            const string Schemas = @"""Body"":{""enum"":[""in-progress"",""done""],
              ""x-enumNames"":[""Working"",""Finished""]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type body = Load(execution).GetType(Ns + "Body", true)!;

            Assert.Equal(new[] { "Finished", "Working" }, Enum.GetNames(body));
            Exchange exchange = await Echo(execution, "\"in-progress\"", "\"done\"");
            Assert.Equal("\"in-progress\"", exchange.RequestJson);
            Assert.Equal("Finished", exchange.Result!.ToString());
        }

        [Fact]
        public void MalformedEnumVarnamesAreIgnoredWithAnOacg108Warning()
        {
            const string Schemas = @"""Body"":{""type"":""string"",""enum"":[""a"",""b""],
              ""x-enum-varnames"":[""OnlyOne""]}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(EchoDocument(Schemas));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Contains("logical path '/components/schemas/Body/x-enum-varnames'", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
            Assert.Equal(new[] { "A", "B" }, Enum.GetNames(Load(execution).GetType(Ns + "Body", true)!));
        }

        [Theory]
        [InlineData("{\"type\":\"number\",\"enum\":[1.5,2.5]}")]
        [InlineData("{\"type\":\"number\",\"enum\":[1,2]}")]
        [InlineData("{\"type\":\"boolean\",\"enum\":[true]}")]
        [InlineData("{\"enum\":[true,false]}")]
        [InlineData("{\"enum\":[1.5]}")]
        [InlineData("{\"enum\":[\"a\",1]}")]
        public void NumberBooleanAndMixedEnumsRemainUnsupported(string schema)
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(EchoDocument("\"Body\":" + schema)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Body/enum'", diagnostic.GetMessage());
        }

        [Fact]
        public void EnumValueTypeMismatchAndDuplicatesReportTheValueLocation()
        {
            Diagnostic mismatch = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                EchoDocument("\"Body\":{\"type\":\"integer\",\"enum\":[1,\"two\"]}")).RunResult.Diagnostics);
            Assert.Equal("OACG100", mismatch.Id);
            Assert.Contains("logical path '/components/schemas/Body/enum/1'", mismatch.GetMessage());

            Diagnostic duplicate = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                EchoDocument("\"Body\":{\"type\":\"integer\",\"enum\":[1,2,1]}")).RunResult.Diagnostics);
            Assert.Equal("OACG101", duplicate.Id);
            Assert.Contains("enum values must be unique", duplicate.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/enum/2'", duplicate.GetMessage());
        }

        // ---- G12 const -----------------------------------------------------------------------

        [Fact]
        public async Task ConstIsAnInitialRequestValueAndValidatedInResponses()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""required"":[""kind""],
              ""properties"":{
                ""kind"":{""type"":""string"",""const"":""pet""},
                ""version"":{""type"":""integer"",""const"":2},
                ""ratio"":{""type"":""number"",""const"":1.5},
                ""active"":{""type"":""boolean"",""const"":true}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);
            object body = Activator.CreateInstance(assembly.GetType(Ns + "Body", true)!)!;
            Assert.Equal("pet", body.GetType().GetProperty("Kind")!.GetValue(body));

            const string Good = "{\"kind\":\"pet\",\"version\":2,\"ratio\":1.5,\"active\":true}";
            Exchange exchange = await Echo(execution, body, Good);

            AssertJsonEqual(Good, exchange.RequestJson);
            Assert.Equal("pet", ReadPath(exchange.Result!, "Kind"));
            // 2.0 is an integral number lexeme and still equals the integer const.
            await Echo(execution, body, "{\"kind\":\"pet\",\"version\":2.0}");

            foreach (string bad in new[]
                     {
                         "{\"kind\":\"dog\"}",
                         "{\"kind\":\"pet\",\"version\":3}",
                         "{\"kind\":\"pet\",\"ratio\":1.6}",
                         "{\"kind\":\"pet\",\"active\":false}"
                     })
            {
                JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(() =>
                    Echo(execution, body, bad));
                Assert.Contains("declared const", exception.Message);
            }
        }

        [Fact]
        public void ConstValueOfTheWrongTypeIsInvalid()
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                EchoDocument("\"Body\":{\"type\":\"string\",\"const\":1}")).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Body/const'", diagnostic.GetMessage());
        }

        [Fact]
        public void ConstWithAnObjectValueIsUnsupported()
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                EchoDocument("\"Body\":{\"type\":\"object\",\"const\":{\"a\":1}}")).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Body/const'", diagnostic.GetMessage());
        }

        // ---- G13 readOnly / writeOnly ----------------------------------------------------------

        private const string DirectionalSchemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
          ""required"":[""id"",""etag"",""name"",""password""],
          ""properties"":{
            ""id"":{""type"":""integer"",""readOnly"":true},
            ""etag"":{""type"":""string"",""readOnly"":true},
            ""owner"":{""$ref"":""#/components/schemas/Owner""},
            ""name"":{""type"":""string""},
            ""password"":{""type"":""string"",""writeOnly"":true}}},
          ""Owner"":{""type"":""object"",""additionalProperties"":false,""readOnly"":true,
            ""properties"":{""n"":{""type"":""string""}}}";

        [Fact]
        public async Task ReadOnlyPropertiesAreNotSentAndNotRequiredInRequests()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(DirectionalSchemas));
            Assembly assembly = Load(execution);
            Type bodyType = assembly.GetType(Ns + "Body", true)!;
            object body = Activator.CreateInstance(bodyType)!;
            bodyType.GetProperty("Name")!.SetValue(body, "n");
            bodyType.GetProperty("Password")!.SetValue(body, "secret");
            // Even when set, readOnly members stay out of the request.
            bodyType.GetProperty("Id")!.SetValue(body, 99);
            bodyType.GetProperty("Owner")!.SetValue(body, JsonConvert.DeserializeObject("{\"n\":\"o\"}", bodyType.GetProperty("Owner")!.PropertyType));

            Exchange exchange = await Echo(execution, body, "{\"id\":7,\"etag\":\"e\",\"name\":\"n\",\"owner\":{\"n\":\"x\"}}");

            AssertJsonEqual("{\"name\":\"n\",\"password\":\"secret\"}", exchange.RequestJson);
            Assert.Equal(7, ReadPath(exchange.Result!, "Id"));
            Assert.Equal("e", ReadPath(exchange.Result!, "Etag"));
            Assert.Equal("x", ReadPath(exchange.Result!, "Owner", "N"));
        }

        [Fact]
        public async Task ReadOnlyPropertyRequiredByTheSchemaIsStillRequiredInResponses()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(DirectionalSchemas));
            Type bodyType = Load(execution).GetType(Ns + "Body", true)!;
            object body = Activator.CreateInstance(bodyType)!;
            bodyType.GetProperty("Name")!.SetValue(body, "n");
            bodyType.GetProperty("Password")!.SetValue(body, "secret");

            await Assert.ThrowsAsync<JsonSerializationException>(() =>
                Echo(execution, body, "{\"etag\":\"e\",\"name\":\"n\"}"));
        }

        [Fact]
        public async Task WriteOnlyPropertyMayBeMissingFromResponsesButIsRequiredInRequests()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(DirectionalSchemas));
            Type bodyType = Load(execution).GetType(Ns + "Body", true)!;
            object body = Activator.CreateInstance(bodyType)!;
            bodyType.GetProperty("Name")!.SetValue(body, "n");

            // password stays null: the writeOnly property is still mandatory when sending.
            // Json.NET reports the OnSerializing failure wrapped in a TargetInvocationException.
            Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
                Echo(execution, body, "{\"id\":1,\"etag\":\"e\",\"name\":\"n\"}"));
            Assert.Contains("'password' cannot be null", (thrown.InnerException ?? thrown).Message);

            bodyType.GetProperty("Password")!.SetValue(body, "p");
            Exchange exchange = await Echo(execution, body, "{\"id\":1,\"etag\":\"e\",\"name\":\"n\"}");
            Assert.Null(ReadPath(exchange.Result!, "Password"));
        }

        [Fact]
        public async Task ReadOnlyMembersAreNotValidatedOrSentInRequests()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""properties"":{
                ""name"":{""type"":""string""},
                ""stats"":{""type"":""object"",""readOnly"":true,
                  ""additionalProperties"":{""type"":""number"",""format"":""double""}},
                ""owners"":{""type"":""object"",""readOnly"":true,
                  ""additionalProperties"":{""$ref"":""#/components/schemas/Owner""}}}},
              ""Owner"":{""type"":""object"",""additionalProperties"":false,""properties"":{""n"":{""type"":""string""}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type bodyType = Load(execution).GetType(Ns + "Body", true)!;
            object body = Activator.CreateInstance(bodyType)!;
            bodyType.GetProperty("Name")!.SetValue(body, "n");
            bodyType.GetProperty("Stats")!.SetValue(body, new Dictionary<string, double> { ["x"] = double.NaN });
            var owners = (System.Collections.IDictionary)Activator.CreateInstance(bodyType.GetProperty("Owners")!.PropertyType)!;
            owners["o"] = null;
            bodyType.GetProperty("Owners")!.SetValue(body, owners);

            Exchange exchange = await Echo(execution, body, "{\"name\":\"n\",\"stats\":{\"x\":1.5}}");

            Assert.Equal("{\"name\":\"n\"}", exchange.RequestJson);
            Assert.Equal(1.5, ((Dictionary<string, double>)ReadPath(exchange.Result!, "Stats")!)["x"]);
        }

        [Fact]
        public void ReflectionOnlyMembersCarryAnInternalPreserveAttribute()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(DirectionalSchemas));
            Assembly assembly = Load(execution);
            Type preserve = assembly.GetType(Ns + "PreserveAttribute", true)!;
            Assert.False(preserve.IsPublic);
            MethodInfo method = assembly.GetType(Ns + "Body", true)!.GetMethod("ShouldSerializeId")!;
            Assert.Contains(method.GetCustomAttributes(), attribute => attribute.GetType() == preserve);
            Assert.Contains(
                method.GetCustomAttributes<System.ComponentModel.EditorBrowsableAttribute>(),
                attribute => attribute.State == System.ComponentModel.EditorBrowsableState.Never);

            Phase4GeneratorExecution open = Generate(EchoDocument("\"Body\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}"));
            Type openBody = Load(open).GetType(Ns + "Body", true)!;
            Assert.Contains(
                openBody.GetProperty("AdditionalProperties")!.GetCustomAttributes(),
                attribute => attribute.GetType().Name == "PreserveAttribute");
        }

        [Fact]
        public void IntegerEnumConverterAcceptsAnyIntegralTokenValueAndLongEnumsRoundTrip()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(
                "\"Body\":{\"type\":\"integer\",\"enum\":[1,2,3]},\"Big\":{\"type\":\"integer\",\"format\":\"int64\",\"enum\":[1,9000000000]}"));
            Assembly assembly = Load(execution);
            Type priority = assembly.GetType(Ns + "Body", true)!;
            Assert.Equal("Value2", new JValue(2).ToObject(priority)!.ToString());
            Assert.Equal("Value2", new JValue(2L).ToObject(priority)!.ToString());

            Type big = assembly.GetType(Ns + "Big", true)!;
            object value = JsonConvert.DeserializeObject("9000000000", big)!;
            Assert.Equal("9000000000", JsonConvert.SerializeObject(value));
            Assert.Equal(9000000000L, Convert.ToInt64(value));
        }

        [Fact]
        public void ReadOnlyHelperMethodNamesAvoidDeclaredMembers()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""properties"":{""id"":{""type"":""integer"",""readOnly"":true},
                              ""shouldSerializeId"":{""type"":""string""}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type body = Load(execution).GetType(Ns + "Body", true)!;

            Assert.NotNull(body.GetMethod("ShouldSerializeId2"));
            Assert.NotNull(body.GetProperty("ShouldSerializeId"));
        }

        // ---- G14 documentation / deprecated / default --------------------------------------------

        [Fact]
        public async Task DeprecatedBecomesObsoleteAndDescriptionAndDefaultBecomeDocumentation()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""title"":""A body"",""description"":""Body description."",""deprecated"":true,
              ""properties"":{
                ""old"":{""type"":""string"",""deprecated"":true,""description"":""Use new <instead> & more.""},
                ""count"":{""type"":""integer"",""default"":5,""description"":""How many.""},
                ""label"":{""type"":""string"",""default"":""none""},
                ""blob"":{""type"":""object"",""default"":{""a"":1}}}},
              ""Mode"":{""type"":""string"",""enum"":[""a"",""b""],""deprecated"":true}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);
            Type body = assembly.GetType(Ns + "Body", true)!;

            Assert.NotNull(body.GetCustomAttribute<ObsoleteAttribute>());
            Assert.NotNull(body.GetProperty("Old")!.GetCustomAttribute<ObsoleteAttribute>());
            Assert.Null(body.GetProperty("Count")!.GetCustomAttribute<ObsoleteAttribute>());
            Assert.NotNull(assembly.GetType(Ns + "Mode", true)!.GetCustomAttribute<ObsoleteAttribute>());
            string source = execution.GeneratedSource;
            Assert.Contains("#pragma warning disable CS0612, CS0618", source);
            Assert.Contains("/// A body", source);
            Assert.Contains("/// Body description.", source);
            Assert.Contains("/// Use new &lt;instead&gt; &amp; more.", source);
            Assert.Contains("/// Default: <c>5</c>.", source);
            Assert.Contains("/// Default: <c>\"none\"</c>.", source);
            // Generated code that refers to deprecated declarations compiles without CS0612/CS0618.
            Assert.DoesNotContain(
                execution.OutputCompilation.GetDiagnostics(),
                static diagnostic => diagnostic.Id == "CS0612" || diagnostic.Id == "CS0618");

            // default never changes the wire contract.
            object instance = Activator.CreateInstance(body)!;
            Exchange exchange = await Echo(execution, instance, "{\"count\":1}");
            Assert.Equal("{}", exchange.RequestJson);
        }

        [Fact]
        public void DocumentationOnlyAnnotationsDoNotEmitThePragma()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""description"":""Plain."",""properties"":{""a"":{""type"":""string"",""description"":""A.""}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Assert.DoesNotContain("#pragma warning disable", execution.GeneratedSource);
            Assert.Contains("/// Plain.", execution.GeneratedSource);
        }

        // ---- G15 format: byte -----------------------------------------------------------------

        [Fact]
        public async Task FormatByteIsAByteArrayWithBase64Wire()
        {
            const string Schemas = @"""Body"":{""type"":""object"",""additionalProperties"":false,
              ""required"":[""payload""],
              ""properties"":{""payload"":{""type"":""string"",""format"":""byte""},
                              ""optional"":{""type"":[""string"",""null""],""format"":""byte""}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);
            Type bodyType = assembly.GetType(Ns + "Body", true)!;
            Assert.Equal(typeof(byte[]), bodyType.GetProperty("Payload")!.PropertyType);
            Assert.Equal(typeof(byte[]), bodyType.GetProperty("Optional")!.PropertyType);

            object body = Activator.CreateInstance(bodyType)!;
            bodyType.GetProperty("Payload")!.SetValue(body, new byte[] { 1, 2, 3 });
            Exchange exchange = await Echo(execution, body, "{\"payload\":\"AQID\",\"optional\":null}");

            AssertJsonEqual("{\"payload\":\"AQID\"}", exchange.RequestJson);
            Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])ReadPath(exchange.Result!, "Payload")!);
            Assert.Null(ReadPath(exchange.Result!, "Optional"));
            foreach (string bad in new[] { "\"@@@\"", "5", "true" })
            {
                await Assert.ThrowsAsync<JsonSerializationException>(() =>
                    Echo(execution, body, "{\"payload\":" + bad + "}"));
            }
        }

        [Fact]
        public async Task FormatByteQueryParameterIsSentAsBase64()
        {
            const string Document = @"{""openapi"":""3.1.0"",""info"":{""title"":""B"",""version"":""1""},
              ""servers"":[{""url"":""https://api.example.test""}],
              ""paths"":{""/p"":{""get"":{""operationId"":""probe"",
                ""parameters"":[{""name"":""data"",""in"":""query"",""required"":true,
                                ""schema"":{""type"":""string"",""format"":""byte""}}],
                ""responses"":{""204"":{""description"":""none""}}}}}}";
            Phase4GeneratorExecution execution = Generate(Document);
            Assembly assembly = Load(execution);
            Type clientType = assembly.GetType(Ns + "Phase4Api", true)!;
            var handler = new StubHandler(string.Empty, HttpStatusCode.NoContent);
            using var httpClient = new HttpClient(handler);
            object client = Activator.CreateInstance(clientType, httpClient)!;

            await (Task)clientType.GetMethod("probe")!.Invoke(
                client, new object?[] { new byte[] { 251, 255, 190 }, CancellationToken.None })!;

            // 0xFB 0xFF 0xBE encodes to "+/++", which must be percent-encoded in the query.
            Assert.Equal("?data=%2B%2F%2B%2B", handler.RequestUri!.Query);
        }

        [Fact]
        public void FormatBinaryInAJsonSchemaRemainsUnsupported()
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                EchoDocument("\"Body\":{\"type\":\"string\",\"format\":\"binary\"}")).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Binary schemas", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body'", diagnostic.GetMessage());
        }

        // ---- Parameters keep rejecting non-scalar schemas ---------------------------------------

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"type\":\"object\"}")]
        [InlineData("{\"type\":\"object\",\"additionalProperties\":{\"type\":\"string\"}}")]
        [InlineData("{\"type\":[\"string\",\"integer\"]}")]
        public void ParametersWithoutASingleScalarTypeAreRejected(string schema)
        {
            string document = @"{""openapi"":""3.1.0"",""info"":{""title"":""P"",""version"":""1""},
              ""paths"":{""/p"":{""get"":{""operationId"":""probe"",
                ""parameters"":[{""name"":""q"",""in"":""query"",""schema"":" + schema + @"}],
                ""responses"":{""204"":{""description"":""none""}}}}}}";

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/paths/~1p/get/parameters/0'", diagnostic.GetMessage());
        }

        // ---- helpers -----------------------------------------------------------------------------

        private static string EchoDocument(string schemas, string version = "3.1.0")
        {
            return "{\"openapi\":\"" + version + "\",\"info\":{\"title\":\"Core\",\"version\":\"1\"}," +
                   "\"servers\":[{\"url\":\"https://api.example.test\"}]," +
                   "\"paths\":{\"/echo\":{\"post\":{\"operationId\":\"echo\"," +
                   "\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"schema\":" +
                   "{\"$ref\":\"#/components/schemas/Body\"}}}}," +
                   "\"responses\":{\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{\"schema\":" +
                   "{\"$ref\":\"#/components/schemas/Body\"}}}}}}}}," +
                   "\"components\":{\"schemas\":{" + schemas + "}}}";
        }

        // The default load context rejects a second assembly with the same name, so each
        // execution is emitted once and then reused by every call in the test.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Phase4GeneratorExecution, Assembly> Loaded =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Phase4GeneratorExecution, Assembly>();

        private static Assembly Load(Phase4GeneratorExecution execution)
        {
            return Loaded.GetValue(execution, static value => value.EmitAssembly());
        }

        private static Phase4GeneratorExecution Generate(string document)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution;
        }

        private static Task<Exchange> Echo(Phase4GeneratorExecution execution, string requestJson, string responseJson)
        {
            return Echo(
                execution,
                (Type type) => JsonConvert.DeserializeObject(requestJson, type),
                responseJson);
        }

        private static Task<Exchange> Echo(Phase4GeneratorExecution execution, object? body, string responseJson)
        {
            return Echo(execution, (Type _) => body, responseJson);
        }

        private static async Task<Exchange> Echo(
            Phase4GeneratorExecution execution,
            Func<Type, object?> createBody,
            string responseJson)
        {
            Assembly assembly = Load(execution);
            Type clientType = assembly.GetType(Ns + "Phase4Api", true)!;
            MethodInfo method = clientType.GetMethod("echo")!;
            var handler = new StubHandler(responseJson);
            using var httpClient = new HttpClient(handler);
            object client = Activator.CreateInstance(clientType, httpClient)!;
            object? body = createBody(method.GetParameters()[0].ParameterType);

            var task = (Task)method.Invoke(client, new object?[] { body, CancellationToken.None })!;
            await task.ConfigureAwait(false);
            object? result = task.GetType().GetProperty("Result")!.GetValue(task);
            return new Exchange(handler.Content, result);
        }

        private static object? ReadPath(object value, params string[] properties)
        {
            object? current = value;
            foreach (string property in properties)
            {
                current = current!.GetType().GetProperty(property)!.GetValue(current);
            }

            return current;
        }

        private static void AssertJsonEqual(string expected, string actual)
        {
            Assert.True(
                JToken.DeepEquals(JToken.Parse(expected), JToken.Parse(actual)),
                "Expected JSON " + expected + " but was " + actual);
        }

        private sealed class Exchange
        {
            internal Exchange(string requestJson, object? result)
            {
                RequestJson = requestJson;
                Result = result;
            }

            internal string RequestJson { get; }

            internal object? Result { get; }
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _response;
            private readonly HttpStatusCode _status;

            internal StubHandler(string response, HttpStatusCode status = HttpStatusCode.OK)
            {
                _response = response;
                _status = status;
            }

            internal string Content { get; private set; } = string.Empty;

            internal Uri? RequestUri { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestUri = request.RequestUri;
                Content = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                var response = new HttpResponseMessage(_status);
                if (_status != HttpStatusCode.NoContent)
                {
                    response.Content = new StringContent(_response, Encoding.UTF8, "application/json");
                }

                return response;
            }
        }
    }
}
