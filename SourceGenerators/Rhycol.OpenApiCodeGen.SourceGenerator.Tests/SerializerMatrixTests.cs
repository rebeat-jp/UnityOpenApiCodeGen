using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Xunit;

using static Rhycol.OpenApiCodeGen.SourceGenerator.Tests.SerializerMatrixHarness;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// The core features (primitives, enums, dates, nullable / required, Specified, readOnly, maps, Any,
    /// FreeObject, extension data, request and response bodies, declared error bodies, the response
    /// contract) run against both JSON serializers. Every test body is the same for both, so a passing
    /// pair shows that Newtonsoft.Json and System.Text.Json agree on the wire and on the contract.
    /// The client is compiled from ApiSurfaceSpecs/serializer-core.json.
    /// </summary>
    public sealed class SerializerMatrixTests
    {
        // The constraint spec of Phase6ConstraintTests stands in the spec file lists as a name.
        private const string InlineConstraints = "constraints (inline)";

        private static string ReadSpecOrInline(string specFile)
        {
            return specFile == InlineConstraints
                ? Phase6ConstraintTests.Spec31()
                : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", specFile));
        }

        private const string Created = "2024-03-01T10:20:30+00:00";

        private const string MinimalWidget = "{\"id\":1,\"name\":\"w\",\"created\":\"" + Created + "\"}";

        private static readonly Guid Ticket = Guid.Parse("123e4567-e89b-12d3-a456-426614174000");

        private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2024, 3, 1, 10, 20, 30, TimeSpan.Zero);

        // ---- the fixture ----------------------------------------------------------------------------

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public void CoreSpecGeneratesWithoutDiagnosticsAndExposesTheExpectedMemberTypes(string serializer)
        {
            Assembly api = Core(serializer);
            Type widget = TypeOf(api, "Widget");

            Assert.Equal(typeof(long), widget.GetProperty("Id")!.PropertyType);
            Assert.Equal(typeof(DateTimeOffset), widget.GetProperty("Created")!.PropertyType);
            Assert.Equal(typeof(DateTime?), widget.GetProperty("Born")!.PropertyType);
            Assert.Equal(typeof(Dictionary<string, int>), widget.GetProperty("Attrs")!.PropertyType);
            Assert.Equal(
                IsStj(serializer) ? typeof(System.Text.Json.JsonElement?) : typeof(JToken),
                widget.GetProperty("Any")!.PropertyType);
            Assert.Equal(
                IsStj(serializer) ? typeof(System.Text.Json.Nodes.JsonObject) : typeof(JObject),
                widget.GetProperty("Extra")!.PropertyType);
            Assert.Equal(
                IsStj(serializer)
                    ? typeof(IDictionary<string, System.Text.Json.JsonElement>)
                    : typeof(IDictionary<string, JToken>),
                TypeOf(api, "Open").GetProperty("AdditionalProperties")!.PropertyType);
        }

        [Theory]
        [InlineData("representative.json")]
        [InlineData("bodies.json")]
        [InlineData("coverage.json")]
        [InlineData("operations.json")]
        [InlineData("security.json")]
        [InlineData("serializer-core.json")]
        [InlineData("composition.json")]
        [InlineData("openapi32.json")]
        [InlineData("swagger2.json")]
        [InlineData(InlineConstraints)]
        public void SystemTextJsonOutputCompilesWithoutReferencingNewtonsoft(string specFile)
        {
            string spec = ReadSpecOrInline(specFile);

            Phase4GeneratorExecution stj = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec,
                options: new Phase4CompilationOptions { Newtonsoft = false, JsonSerializer = SystemTextJson });

            Assert.Empty(stj.RunResult.Diagnostics);
            Assert.Empty(stj.CompilationErrors);
            Assert.DoesNotContain("Newtonsoft", stj.GeneratedSource);

            Phase4GeneratorExecution newtonsoft = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec,
                options: new Phase4CompilationOptions { SystemTextJson = false });
            Assert.Empty(newtonsoft.CompilationErrors);
            Assert.DoesNotContain("System.Text.Json", newtonsoft.GeneratedSource);
        }

        // ---- request: the same wire ------------------------------------------------------------------

        private static object FullWidget(Assembly api, string serializer)
        {
            return New(
                api,
                "Widget",
                ("Id", 42L),
                ("Name", "Gadget"),
                ("Count", 7),
                ("Ratio", 1.5),
                ("Flag", true),
                ("Color", EnumValue(api, "Color", "DarkBlue")),
                ("Level", EnumValue(api, "Level", "Value2")),
                ("Born", new DateTime(2024, 2, 29)),
                ("Created", CreatedAt),
                ("Ident", Ticket),
                ("Blob", new byte[] { 1, 2, 3 }),
                ("Tags", new List<string> { "a", "b" }),
                ("Attrs", new Dictionary<string, int> { ["x"] = 1 }),
                ("Any", MakeAny(serializer, "{\"k\":[1,2,null]}")),
                ("Extra", MakeFreeObject(serializer, "{\"free\":\"form\"}")),
                ("Note", null),
                ("Secret", "hidden"),
                ("Child", New(api, "Address", ("City", "Tokyo"))));
        }

        private const string FullWidgetWire =
            "{\"id\":42,\"name\":\"Gadget\",\"count\":7,\"ratio\":1.5,\"flag\":true,\"color\":\"dark-blue\"," +
            "\"level\":2,\"born\":\"2024-02-29\",\"created\":\"" + Created + "\"," +
            "\"ident\":\"123e4567-e89b-12d3-a456-426614174000\",\"blob\":\"AQID\",\"tags\":[\"a\",\"b\"]," +
            "\"attrs\":{\"x\":1},\"any\":{\"k\":[1,2,null]},\"extra\":{\"free\":\"form\"},\"note\":null," +
            "\"child\":{\"city\":\"Tokyo\"}}";

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task RequestBodyKeepsTheWireRepresentationOfEveryCoreType(string serializer)
        {
            Assembly api = Core(serializer);

            (object? result, Phase4BodyWireHarness.Exchange sent) = await Call(
                api, "createWidget", Json(HttpStatusCode.Created, MinimalWidget), FullWidget(api, serializer));

            Assert.Equal("application/json; charset=utf-8", sent.ContentType);
            // secret is readOnly: never sent. note is Specified as null: sent as null.
            Assert.True(
                JToken.DeepEquals(JToken.Parse(FullWidgetWire), Body(sent)),
                "Unexpected request body: " + sent.BodyText);
            Assert.Equal("w", Get(result!, "Name"));
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task OptionalMembersAreOmittedUntilTheyAreSpecified(string serializer)
        {
            Assembly api = Core(serializer);

            object minimal = New(api, "Widget", ("Id", 1L), ("Name", "n"), ("Created", CreatedAt));
            (_, Phase4BodyWireHarness.Exchange first) = await Call(
                api, "createWidget", Json(HttpStatusCode.Created, MinimalWidget), minimal);
            Assert.Equal(
                new[] { "created", "id", "name" },
                ((JObject)Body(first)).Properties().Select(static p => p.Name).OrderBy(static n => n, StringComparer.Ordinal));

            object nullNote = New(api, "Widget", ("Id", 1L), ("Name", "n"), ("Created", CreatedAt), ("Note", null), ("Any", null));
            (_, Phase4BodyWireHarness.Exchange second) = await Call(
                api, "createWidget", Json(HttpStatusCode.Created, MinimalWidget), nullNote);
            var secondBody = (JObject)Body(second);
            Assert.Equal(JTokenType.Null, secondBody["note"]!.Type);
            Assert.Equal(JTokenType.Null, secondBody["any"]!.Type);
            Assert.False(secondBody.ContainsKey("count"));
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task AMissingRequiredReferenceMemberFailsBeforeSending(string serializer)
        {
            Assembly api = Core(serializer);
            object widget = New(api, "Widget", ("Id", 1L), ("Created", CreatedAt));

            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                api, "createWidget", Json(HttpStatusCode.Created, MinimalWidget), widget));

            Assert.True(
                JsonExceptionType(serializer).IsInstanceOfType(failure) ||
                JsonExceptionType(serializer).IsInstanceOfType(failure.InnerException),
                failure.ToString());
        }

        [Fact]
        public async Task RequestWireAndQueryAreIdenticalForBothSerializers()
        {
            var bodies = new List<JToken>();
            var queries = new List<string>();
            foreach (string serializer in new[] { NewtonsoftJson, SystemTextJson })
            {
                Assembly api = Core(serializer);
                (_, Phase4BodyWireHarness.Exchange sent) = await Call(
                    api, "createWidget", Json(HttpStatusCode.Created, MinimalWidget), FullWidget(api, serializer));
                bodies.Add(Body(sent));

                (_, Phase4BodyWireHarness.Exchange query) = await Call(
                    api,
                    "getWidget",
                    Json(HttpStatusCode.OK, MinimalWidget),
                    5L,
                    CreatedAt,
                    EnumValue(api, "Color", "DarkBlue"),
                    EnumValue(api, "Level", "Value2"),
                    1.5,
                    new DateTime(2024, 2, 29),
                    Ticket);
                queries.Add(query.Uri!.ToString());
            }

            Assert.True(JToken.DeepEquals(bodies[0], bodies[1]), bodies[0] + " != " + bodies[1]);
            Assert.Equal(queries[0], queries[1]);
            Assert.Contains("/widgets/5?", queries[0]);
            Assert.Contains("color=dark-blue", queries[0]);
            Assert.Contains("level=2", queries[0]);
            Assert.Contains("since=2024-02-29", queries[0]);
            Assert.Contains("ratio=1.5", queries[0]);
            Assert.Contains("ticket=123e4567-e89b-12d3-a456-426614174000", queries[0]);
        }

        // ---- response: typed values ------------------------------------------------------------------

        private const string FullWidgetResponse =
            "{\"id\":42,\"name\":\"Gadget\",\"count\":7,\"ratio\":1.5,\"flag\":true,\"color\":\"dark-blue\"," +
            "\"level\":2,\"born\":\"2024-02-29\",\"created\":\"2024-03-01T10:20:30Z\"," +
            "\"ident\":\"123e4567-e89b-12d3-a456-426614174000\",\"blob\":\"AQID\",\"tags\":[\"a\",\"b\"]," +
            "\"attrs\":{\"x\":1},\"any\":{\"k\":[1,2,null]},\"extra\":{\"free\":\"form\"},\"note\":null," +
            "\"secret\":\"s\",\"child\":{\"city\":\"Tokyo\",\"zip\":\"100\"}}";

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task ResponseBodyIsDeserializedToTheTypedValues(string serializer)
        {
            Assembly api = Core(serializer);

            (object? result, _) = await Call(
                api, "getWidget", Json(HttpStatusCode.OK, FullWidgetResponse), 42L, null, null, null, null, null, null);

            object widget = result!;
            Assert.Equal(42L, Get(widget, "Id"));
            Assert.Equal("Gadget", Get(widget, "Name"));
            Assert.Equal(7, Get(widget, "Count"));
            Assert.Equal(1.5, Get(widget, "Ratio"));
            Assert.Equal(true, Get(widget, "Flag"));
            Assert.Equal(EnumValue(api, "Color", "DarkBlue"), Get(widget, "Color"));
            Assert.Equal(EnumValue(api, "Level", "Value2"), Get(widget, "Level"));
            Assert.Equal(new DateTime(2024, 2, 29), Get(widget, "Born"));
            Assert.Equal(CreatedAt, Get(widget, "Created"));
            Assert.Equal(Ticket, Get(widget, "Ident"));
            Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])Get(widget, "Blob")!);
            Assert.Equal(new[] { "a", "b" }, (List<string>)Get(widget, "Tags")!);
            Assert.Equal(1, ((Dictionary<string, int>)Get(widget, "Attrs")!)["x"]);
            Assert.Equal("{\"k\":[1,2,null]}", JsonText(Get(widget, "Any")));
            Assert.Equal("{\"free\":\"form\"}", JsonText(Get(widget, "Extra")));
            Assert.Null(Get(widget, "Note"));
            Assert.Equal(true, Get(widget, "NoteSpecified"));
            Assert.Equal("s", Get(widget, "Secret"));
            object child = Get(widget, "Child")!;
            Assert.Equal("Tokyo", Get(child, "City"));
            Assert.Equal("100", Get(child, "Zip"));
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task AbsentOptionalMembersStayUnspecified(string serializer)
        {
            Assembly api = Core(serializer);

            (object? result, _) = await Call(
                api, "getWidget", Json(HttpStatusCode.OK, MinimalWidget), 1L, null, null, null, null, null, null);

            Assert.Equal(false, Get(result!, "NoteSpecified"));
            Assert.Equal(false, Get(result!, "AnySpecified"));
            Assert.Null(Get(result!, "Count"));
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task ArraysMapsAndScalarsAreDeserializedAtTheTopLevel(string serializer)
        {
            Assembly api = Core(serializer);

            (object? list, _) = await Call(api, "listWidgets", Json(HttpStatusCode.OK, "[" + MinimalWidget + "," + MinimalWidget + "]"));
            Assert.Equal(2, ((IList)list!).Count);

            (object? empty, _) = await Call(api, "listWidgets", Json(HttpStatusCode.OK, "[]"));
            Assert.Empty((IList)empty!);

            (object? ints, _) = await Call(api, "getInts", Json(HttpStatusCode.OK, "[1, 2.0, 3e0, -4]"));
            Assert.Equal(new[] { 1, 2, 3, -4 }, (List<int>)ints!);

            (object? level, _) = await Call(api, "getLevel", Json(HttpStatusCode.OK, "2"));
            Assert.Equal(EnumValue(api, "Level", "Value2"), level);

            (object? dates, _) = await Call(api, "getDates", Json(HttpStatusCode.OK, "{\"a\":\"2020-01-02\"}"));
            Assert.Equal(new DateTime(2020, 1, 2), ((Dictionary<string, DateTime>)dates!)["a"]);

            (object? any, _) = await Call(api, "getAny", Json(HttpStatusCode.OK, "[1,{\"a\":null}]"));
            Assert.Equal("[1,{\"a\":null}]", JsonText(any));
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task OpenDtoKeepsUndeclaredMembersAsRawJsonInBothDirections(string serializer)
        {
            Assembly api = Core(serializer);
            object open = New(api, "Open", ("Kind", "k"));
            var extension = (IDictionary)Get(open, "AdditionalProperties")!;
            extension["n"] = MakeAny(serializer, "1");
            extension["s"] = MakeAny(serializer, "\"x\"");
            extension["o"] = MakeAny(serializer, "{\"p\":true}");

            (object? result, Phase4BodyWireHarness.Exchange sent) = await Call(
                api,
                "putOpen",
                Json(HttpStatusCode.OK, "{\"kind\":\"k\",\"extra1\":[1,2],\"extra2\":null,\"extra3\":\"t\"}"),
                open);

            Assert.True(
                JToken.DeepEquals(JToken.Parse("{\"kind\":\"k\",\"n\":1,\"s\":\"x\",\"o\":{\"p\":true}}"), Body(sent)),
                sent.BodyText);
            Assert.Equal("k", Get(result!, "Kind"));
            var received = (IDictionary)Get(result!, "AdditionalProperties")!;
            Assert.Equal(3, received.Count);
            Assert.Equal("[1,2]", JsonText(received["extra1"]));
            Assert.Equal("null", JsonText(received["extra2"]));
            Assert.Equal("\"t\"", JsonText(received["extra3"]));
        }

        // ---- response: the contract --------------------------------------------------------------------

        private static string Widget(string tail) =>
            "{\"id\":1,\"name\":\"w\",\"created\":\"" + Created + "\"" + tail + "}";

        public static IEnumerable<object[]> InvalidWidgets()
        {
            string[] bodies =
            {
                "{\"id\":1,\"created\":\"" + Created + "\"}",
                Widget(",\"zzz\":1"),
                "{\"id\":1,\"name\":null,\"created\":\"" + Created + "\"}",
                "{\"id\":\"1\",\"name\":\"w\",\"created\":\"" + Created + "\"}",
                Widget(",\"color\":\"RED\""),
                Widget(",\"color\":1"),
                Widget(",\"born\":\"2024-13-01\""),
                Widget(",\"born\":\"2024-02-29T00:00:00Z\""),
                Widget(",\"ident\":\"nope\""),
                Widget(",\"level\":2.0"),
                Widget(",\"level\":4"),
                Widget(",\"level\":\"2\""),
                Widget(",\"count\":1.5"),
                Widget(",\"count\":3000000000"),
                Widget(",\"count\":null"),
                Widget(",\"extra\":[1]"),
                Widget(",\"attrs\":{\"x\":\"s\"}"),
                Widget(",\"tags\":[null]"),
                Widget(",\"blob\":\"***\""),
                "{\"id\":1,\"id\":2,\"name\":\"w\",\"created\":\"" + Created + "\"}",
                Widget(",\"extra\":{\"a\":1,\"a\":2}"),
                MinimalWidget + " x",
                "{\"id\":1,\"name\":\"w\",\"created\":\"" + Created + "\"",
                "nope",
                "[]",
                "null",
                string.Empty
            };
            foreach (string serializer in new[] { NewtonsoftJson, SystemTextJson })
            {
                foreach (string body in bodies)
                {
                    yield return new object[] { serializer, body };
                }
            }
        }

        [Theory]
        [MemberData(nameof(InvalidWidgets))]
        public async Task ResponseThatViolatesTheContractIsRejected(string serializer, string body)
        {
            Assembly api = Core(serializer);

            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                api, "getWidget", Json(HttpStatusCode.OK, body), 1L, null, null, null, null, null, null));

            Assert.IsAssignableFrom(JsonExceptionType(serializer), failure);
        }

        [Theory]
        [InlineData(NewtonsoftJson, "[1.5]")]
        [InlineData(SystemTextJson, "[1.5]")]
        [InlineData(NewtonsoftJson, "[\"1\"]")]
        [InlineData(SystemTextJson, "[\"1\"]")]
        [InlineData(NewtonsoftJson, "[null]")]
        [InlineData(SystemTextJson, "[null]")]
        [InlineData(NewtonsoftJson, "[2147483648]")]
        [InlineData(SystemTextJson, "[2147483648]")]
        [InlineData(NewtonsoftJson, "{}")]
        [InlineData(SystemTextJson, "{}")]
        public async Task IntegerArraysRejectNonIntegersNullsAndOutOfRangeValues(string serializer, string body)
        {
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                Core(serializer), "getInts", Json(HttpStatusCode.OK, body)));

            Assert.IsAssignableFrom(JsonExceptionType(serializer), failure);
        }

        [Theory]
        [InlineData(NewtonsoftJson, "{\"a\":\"2020-01-02T00:00:00Z\"}")]
        [InlineData(SystemTextJson, "{\"a\":\"2020-01-02T00:00:00Z\"}")]
        [InlineData(NewtonsoftJson, "{\"a\":null}")]
        [InlineData(SystemTextJson, "{\"a\":null}")]
        public async Task DateMapRejectsNonDatesAndNulls(string serializer, string body)
        {
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                Core(serializer), "getDates", Json(HttpStatusCode.OK, body)));

            Assert.IsAssignableFrom(JsonExceptionType(serializer), failure);
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task ResponseWithoutAJsonContentTypeIsRejected(string serializer)
        {
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                Core(serializer),
                "listWidgets",
                Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "[]", "text/plain")));

            Assert.IsAssignableFrom(JsonExceptionType(serializer), failure);
        }

        // ---- declared error bodies --------------------------------------------------------------------

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task DeclaredErrorBodyIsDeserializedIntoTheException(string serializer)
        {
            Assembly api = Core(serializer);
            object widget = New(api, "Widget", ("Id", 1L), ("Name", "n"), ("Created", CreatedAt));

            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                api,
                "createWidget",
                Json(HttpStatusCode.BadRequest, "{\"code\":7,\"message\":\"nope\"}"),
                widget));

            Assert.Equal("Phase4ApiException", failure.GetType().Name);
            Assert.Equal(HttpStatusCode.BadRequest, Get(failure, "StatusCode"));
            Assert.Equal("{\"code\":7,\"message\":\"nope\"}", Get(failure, "ResponseBody"));
            Assert.Null(Get(failure, "ErrorDeserializationException"));
            object error = Get(failure, "Error")!;
            Assert.Equal("Problem", error.GetType().Name);
            Assert.Equal(7, Get(error, "Code"));
            Assert.Equal("nope", Get(error, "Message"));
        }

        [Theory]
        [InlineData(NewtonsoftJson, "{\"code\":\"x\",\"message\":\"m\"}")]
        [InlineData(SystemTextJson, "{\"code\":\"x\",\"message\":\"m\"}")]
        [InlineData(NewtonsoftJson, "{\"code\":7}")]
        [InlineData(SystemTextJson, "{\"code\":7}")]
        [InlineData(NewtonsoftJson, "not json")]
        [InlineData(SystemTextJson, "not json")]
        public async Task UnreadableErrorBodyKeepsTheRawTextAndTheReason(string serializer, string body)
        {
            Assembly api = Core(serializer);
            object widget = New(api, "Widget", ("Id", 1L), ("Name", "n"), ("Created", CreatedAt));

            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                api, "createWidget", Json(HttpStatusCode.BadRequest, body), widget));

            Assert.Equal(body, Get(failure, "ResponseBody"));
            Assert.Null(Get(failure, "Error"));
            Assert.IsAssignableFrom(
                JsonExceptionType(serializer),
                Assert.IsAssignableFrom<Exception>(Get(failure, "ErrorDeserializationException")));
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task Rfc3339DateTimeWithLowerCaseSeparatorsAndOffsetsIsRead(string serializer)
        {
            Assembly api = Core(serializer);

            (object? result, _) = await Call(
                api,
                "getWidget",
                Json(HttpStatusCode.OK, "{\"id\":1,\"name\":\"w\",\"created\":\"2024-03-01t10:20:30.5z\"}"),
                1L, null, null, null, null, null, null);

            Assert.Equal(
                new DateTimeOffset(2024, 3, 1, 10, 20, 30, 500, TimeSpan.Zero),
                Get(result!, "Created"));
        }

        /// <summary>
        /// The only differences of the wire between the serializers: a whole double has no fraction with
        /// System.Text.Json (2 instead of 2.0) and characters outside the BMP are written as an escaped
        /// surrogate pair. Both are the same JSON value.
        /// </summary>
        [Fact]
        public async Task KnownWireDifferencesAreEquivalentJson()
        {
            var bodies = new Dictionary<string, Phase4BodyWireHarness.Exchange>();
            foreach (string serializer in new[] { NewtonsoftJson, SystemTextJson })
            {
                Assembly api = Core(serializer);
                object widget = New(
                    api,
                    "Widget",
                    ("Id", 1L),
                    ("Name", "<a&b'+> é日 " + char.ConvertFromUtf32(0x1F600)),
                    ("Ratio", 2.0),
                    ("Created", CreatedAt));
                (_, bodies[serializer]) = await Call(
                    api, "createWidget", Json(HttpStatusCode.Created, MinimalWidget), widget);
            }

            Assert.Contains("\"ratio\":2.0", bodies[NewtonsoftJson].BodyText);
            Assert.Contains("\"ratio\":2", bodies[SystemTextJson].BodyText);
            Assert.DoesNotContain("\"ratio\":2.0", bodies[SystemTextJson].BodyText);
            Assert.DoesNotContain("\\uD83D", bodies[NewtonsoftJson].BodyText);
            Assert.Contains("\\uD83D\\uDE00", bodies[SystemTextJson].BodyText);
            // HTML-sensitive characters are not escaped by either serializer.
            Assert.Contains("<a&b'+>", bodies[NewtonsoftJson].BodyText);
            Assert.Contains("<a&b'+>", bodies[SystemTextJson].BodyText);
            var expected = (JObject)Body(bodies[NewtonsoftJson]);
            var actual = (JObject)Body(bodies[SystemTextJson]);
            Assert.Equal(expected["ratio"]!.Value<double>(), actual["ratio"]!.Value<double>());
            expected.Remove("ratio");
            actual.Remove("ratio");
            Assert.True(JToken.DeepEquals(expected, actual));
            Assert.Equal(
                Body(bodies[NewtonsoftJson])["name"]!.Value<string>(),
                Body(bodies[SystemTextJson])["name"]!.Value<string>());
        }

        // ---- the same through the existing bodies fixture -----------------------------------------------

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task BodiesFixtureJsonBodyRoundTrips(string serializer)
        {
            Assembly bodies = Bodies(serializer);
            object pet = New(bodies, "Pet", ("Name", "Rex"));

            (object? result, Phase4BodyWireHarness.Exchange sent) = await Call(
                bodies, "createPet", Json(HttpStatusCode.Created, "{\"id\":1,\"name\":\"Rex\"}"), pet);

            Assert.Equal("application/json; charset=utf-8", sent.ContentType);
            Assert.Contains("\"name\":\"Rex\"", sent.BodyText);
            Assert.Equal("Rex", Get(result!, "Name"));
        }

        // ---- System.Text.Json 8 --------------------------------------------------------------------------

        /// <summary>
        /// Unity compiles against netstandard2.1 with the netstandard2.0 build of System.Text.Json 8 and
        /// Newtonsoft.Json 13. The generated code of both serializers must compile against exactly that
        /// set, which catches BCL members that exist in net8 but not in netstandard2.1.
        /// </summary>
        [Theory]
        [InlineData("serializer-core.json", NewtonsoftJson)]
        [InlineData("serializer-core.json", SystemTextJson)]
        [InlineData("bodies.json", NewtonsoftJson)]
        [InlineData("bodies.json", SystemTextJson)]
        [InlineData("representative.json", NewtonsoftJson)]
        [InlineData("representative.json", SystemTextJson)]
        [InlineData("coverage.json", NewtonsoftJson)]
        [InlineData("coverage.json", SystemTextJson)]
        [InlineData("operations.json", NewtonsoftJson)]
        [InlineData("operations.json", SystemTextJson)]
        [InlineData("security.json", NewtonsoftJson)]
        [InlineData("security.json", SystemTextJson)]
        [InlineData("composition.json", NewtonsoftJson)]
        [InlineData("composition.json", SystemTextJson)]
        [InlineData("openapi32.json", NewtonsoftJson)]
        [InlineData("openapi32.json", SystemTextJson)]
        [InlineData("swagger2.json", NewtonsoftJson)]
        [InlineData("swagger2.json", SystemTextJson)]
        [InlineData(InlineConstraints, NewtonsoftJson)]
        [InlineData(InlineConstraints, SystemTextJson)]
        public void GeneratedCodeCompilesAgainstTheUnityNetStandard21ReferenceSet(string specFile, string serializer)
        {
            string root = typeof(SerializerMatrixTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(static attribute => attribute.Key == "NuGetPackageRoot")
                .Value!;
            string Lib(string package, string version, string file) =>
                Path.Combine(root, package, version, "lib", "netstandard2.0", file);
            var references = Directory
                .GetFiles(Path.Combine(root, "netstandard.library.ref", "2.1.0", "ref", "netstandard2.1"), "*.dll")
                .ToList();
            if (IsStj(serializer))
            {
                references.AddRange(new[]
                {
                    Lib("system.text.json", "8.0.5", "System.Text.Json.dll"),
                    Lib("system.text.encodings.web", "8.0.0", "System.Text.Encodings.Web.dll"),
                    Lib("system.memory", "4.5.5", "System.Memory.dll"),
                    Lib("system.buffers", "4.5.1", "System.Buffers.dll"),
                    Lib("system.numerics.vectors", "4.5.0", "System.Numerics.Vectors.dll"),
                    Lib("system.runtime.compilerservices.unsafe", "6.0.0", "System.Runtime.CompilerServices.Unsafe.dll"),
                    Lib("system.threading.tasks.extensions", "4.5.4", "System.Threading.Tasks.Extensions.dll"),
                    Lib("microsoft.bcl.asyncinterfaces", "8.0.0", "Microsoft.Bcl.AsyncInterfaces.dll")
                });
            }
            else
            {
                references.Add(Lib("newtonsoft.json", "13.0.3", "Newtonsoft.Json.dll"));
            }

            Assert.All(references, path => Assert.True(File.Exists(path), path));
            string spec = ReadSpecOrInline(specFile);

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec,
                options: new Phase4CompilationOptions { JsonSerializer = serializer, ReferencePaths = references });

            Assert.DoesNotContain(
                execution.OutputCompilation.References.Select(static reference => reference.Display),
                static display => display != null && display.Contains("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Theory]
        [InlineData("serializer-core.json")]
        [InlineData("bodies.json")]
        [InlineData("representative.json")]
        [InlineData("operations.json")]
        [InlineData("composition.json")]
        [InlineData("openapi32.json")]
        [InlineData("swagger2.json")]
        [InlineData(InlineConstraints)]
        public void SystemTextJsonOutputCompilesAgainstTheNet8ReferenceAssemblies(string specFile)
        {
            string directory = typeof(SerializerMatrixTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(static attribute => attribute.Key == "Net8ReferenceDirectory")
                .Value!;
            Assert.True(Directory.Exists(directory), "The .NET 8 reference assemblies are missing: " + directory);
            string spec = ReadSpecOrInline(specFile);

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec,
                options: new Phase4CompilationOptions
                {
                    Newtonsoft = false,
                    JsonSerializer = SystemTextJson,
                    ReferenceDirectory = directory
                });

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains(
                execution.OutputCompilation.References.Select(static reference => reference.Display),
                static display => display != null && display.EndsWith("System.Text.Json.dll", StringComparison.OrdinalIgnoreCase) &&
                                  display.Contains("microsoft.netcore.app.ref", StringComparison.OrdinalIgnoreCase));
        }
    }
}
