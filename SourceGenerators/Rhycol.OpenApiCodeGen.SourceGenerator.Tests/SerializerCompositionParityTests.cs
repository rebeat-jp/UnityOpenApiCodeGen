using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Xunit;

using static Rhycol.OpenApiCodeGen.SourceGenerator.Tests.SerializerMatrixHarness;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// oneOf / anyOf wrappers and their converters, judged by both serializers with one test body: the
    /// wire form of a request, the typed result of a response, the messages of the contract and the
    /// converters of the client (full-date, integers written as 1.0, readOnly members) that the union
    /// has to use when it reads or writes its members. The System.Text.Json specific parts of the
    /// wrapper API (FromJsonElement / ToJsonElement) are tested at the end.
    /// </summary>
    public sealed class SerializerCompositionParityTests
    {
        private const string Spec = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Composition"", ""version"": ""1"" },
  ""servers"": [ { ""url"": ""https://api.example.test"" } ],
  ""paths"": {
    ""/pet"": { ""post"": { ""operationId"": ""echoPet"",
      ""requestBody"": { ""required"": true, ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Pet"" } } } },
      ""responses"": { ""200"": { ""description"": ""ok"", ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Pet"" } } } } } } },
    ""/payload"": { ""post"": { ""operationId"": ""echoPayload"",
      ""requestBody"": { ""required"": true, ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Payload"" } } } },
      ""responses"": { ""200"": { ""description"": ""ok"", ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Payload"" } } } } } } },
    ""/holder"": { ""post"": { ""operationId"": ""echoHolder"",
      ""requestBody"": { ""required"": true, ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Holder"" } } } },
      ""responses"": { ""200"": { ""description"": ""ok"", ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Holder"" } } } } } } },
    ""/odd"": { ""post"": { ""operationId"": ""echoOdd"",
      ""requestBody"": { ""required"": true, ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Odd"" } } } },
      ""responses"": { ""200"": { ""description"": ""ok"", ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Odd"" } } } } } } }
  },
  ""components"": { ""schemas"": {
    ""Cat"": { ""type"": ""object"", ""additionalProperties"": false, ""required"": [ ""name"" ],
      ""properties"": { ""name"": { ""type"": ""string"" }, ""id"": { ""type"": ""integer"", ""format"": ""int64"", ""readOnly"": true } } },
    ""Dog"": { ""type"": ""object"", ""additionalProperties"": false, ""required"": [ ""bark"" ],
      ""properties"": { ""bark"": { ""type"": ""string"" } } },
    ""Pet"": { ""oneOf"": [ { ""$ref"": ""#/components/schemas/Cat"" }, { ""$ref"": ""#/components/schemas/Dog"" } ],
      ""discriminator"": { ""propertyName"": ""kind"", ""mapping"": { ""cat"": ""#/components/schemas/Cat"", ""dog"": ""#/components/schemas/Dog"" } } },
    ""Stamp"": { ""type"": ""object"", ""additionalProperties"": false, ""required"": [ ""when"" ],
      ""properties"": { ""when"": { ""type"": ""string"", ""format"": ""date"" }, ""n"": { ""type"": ""integer"", ""format"": ""int32"" }, ""tag"": { ""type"": ""string"" } } },
    ""Payload"": { ""oneOf"": [ { ""$ref"": ""#/components/schemas/Stamp"" }, { ""type"": ""string"" }, { ""type"": ""integer"", ""format"": ""int32"" } ] },
    ""Holder"": { ""type"": ""object"", ""additionalProperties"": false,
      ""properties"": {
        ""pet"": { ""$ref"": ""#/components/schemas/Pet"" },
        ""payloads"": { ""type"": ""array"", ""items"": { ""$ref"": ""#/components/schemas/Payload"" } },
        ""byName"": { ""type"": ""object"", ""additionalProperties"": { ""$ref"": ""#/components/schemas/Pet"" } } } },
    ""JsonElement"": { ""type"": ""object"", ""additionalProperties"": false, ""required"": [ ""x"" ], ""properties"": { ""x"": { ""type"": ""string"" } } },
    ""JToken"": { ""type"": ""object"", ""additionalProperties"": false, ""required"": [ ""y"" ], ""properties"": { ""y"": { ""type"": ""integer"" } } },
    ""Odd"": { ""anyOf"": [ { ""$ref"": ""#/components/schemas/JsonElement"" }, { ""$ref"": ""#/components/schemas/JToken"" } ] }
  } }
}";

        private static Assembly Api(string serializer) => CompileText("composition-parity", Spec, serializer);

        // The call fails with the JSON exception of the serializer (Json.NET raises a subclass of it).
        private static async Task<Exception> Rejects(string serializer, Func<Task> call)
        {
            Exception error = await Assert.ThrowsAnyAsync<Exception>(call);
            Assert.True(JsonExceptionType(serializer).IsInstanceOfType(error), error.ToString());
            return error;
        }

        private static object Union(Assembly api, string union, string factory, object value)
        {
            return TypeOf(api, union).GetMethod(factory)!.Invoke(null, new[] { value })!;
        }

        private static bool TryGet(object union, string method, out object? value)
        {
            object?[] arguments = { null };
            bool result = (bool)union.GetType().GetMethod(method)!.Invoke(union, arguments)!;
            value = arguments[0];
            return result;
        }

        // ---- discriminated union -------------------------------------------------------------------

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task DiscriminatedUnionIsWrittenWithItsDiscriminatorAndWithoutReadOnlyMembers(string serializer)
        {
            Assembly api = Api(serializer);
            object cat = New(api, "Cat", ("Name", "tom"), ("Id", 5L));

            (object? result, Phase4BodyWireHarness.Exchange sent) = await Call(
                api, "echoPet", Json(HttpStatusCode.OK, "{\"kind\":\"dog\",\"bark\":\"woof\"}"), Union(api, "Pet", "FromCat", cat));

            Assert.True(JToken.DeepEquals(JToken.Parse("{\"name\":\"tom\",\"kind\":\"cat\"}"), Body(sent)), sent.BodyText);
            Assert.Equal("dog", Get(result!, "DiscriminatorValue"));
            Assert.Equal("woof", Get(Get(result!, "Value")!, "Bark"));
            Assert.True(TryGet(result!, "TryGetDog", out _));
            Assert.False(TryGet(result!, "TryGetCat", out _));
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task DiscriminatorProblemsAreReportedWithThePathOfTheDiscriminator(string serializer)
        {
            Assembly api = Api(serializer);
            object cat = Union(api, "Pet", "FromCat", New(api, "Cat", ("Name", "tom")));

            Exception unknown = await Rejects(serializer, () => Call(api, "echoPet", Json(HttpStatusCode.OK, "{\"kind\":\"bird\"}"), cat));
            Assert.Contains("'bird'", unknown.Message);
            Assert.Contains("$.kind", unknown.Message);

            Exception missing = await Rejects(serializer, () => Call(api, "echoPet", Json(HttpStatusCode.OK, "{\"bark\":\"x\"}"), cat));
            Assert.Contains("discriminator", missing.Message);

            Exception notString = await Rejects(serializer, () => Call(api, "echoPet", Json(HttpStatusCode.OK, "{\"kind\":1,\"bark\":\"x\"}"), cat));
            Assert.Contains("not a string", notString.Message);

            // The selected member is still checked: a closed schema rejects an undeclared member.
            Exception undeclared = await Rejects(serializer, () => Call(api, "echoPet", Json(HttpStatusCode.OK, "{\"kind\":\"dog\",\"bark\":\"x\",\"extra\":1}"), cat));
            Assert.Contains("$.extra", undeclared.Message);
        }

        // ---- union without a discriminator ------------------------------------------------------------

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task RawUnionReadsItsCandidatesWithTheConvertersOfTheClient(string serializer)
        {
            Assembly api = Api(serializer);
            object input = Union(api, "Payload", "FromInt32", 1);

            // full-date and an integer written as 1.0 need the converters of the client.
            (object? stamp, _) = await Call(
                api, "echoPayload", Json(HttpStatusCode.OK, "{\"when\":\"2024-01-02\",\"n\":1.0,\"tag\":\"t\"}"), input);
            Assert.True(TryGet(stamp!, "TryGetStamp", out object? parsed));
            Assert.Equal(new DateTime(2024, 1, 2), Get(parsed!, "When"));
            Assert.Equal(1, Get(parsed!, "N"));
            Assert.Equal("t", Get(parsed!, "Tag"));
            Assert.False(TryGet(stamp!, "TryGetString", out _));
            Assert.False(TryGet(stamp!, "TryGetInt32", out _));

            (object? text, _) = await Call(api, "echoPayload", Json(HttpStatusCode.OK, "\"hello\""), input);
            Assert.True(TryGet(text!, "TryGetString", out object? str));
            Assert.Equal("hello", str);
            Assert.False(TryGet(text!, "TryGetStamp", out _));

            (object? number, _) = await Call(api, "echoPayload", Json(HttpStatusCode.OK, "5"), input);
            Assert.True(TryGet(number!, "TryGetInt32", out object? five));
            Assert.Equal(5, five);

            // 5.0 is an integer for the contract, but a candidate reads only the plain integer lexeme (as Json.NET)
            (object? decimalFive, _) = await Call(api, "echoPayload", Json(HttpStatusCode.OK, "5.0"), input);
            Assert.False(TryGet(decimalFive!, "TryGetInt32", out _));

            await Rejects(serializer, () => Call(api, "echoPayload", Json(HttpStatusCode.OK, "[1]"), input));
            await Rejects(serializer, () => Call(api, "echoPayload", Json(HttpStatusCode.OK, "{\"when\":\"not a date\"}"), input));
            await Rejects(serializer, () => Call(api, "echoPayload", Json(HttpStatusCode.OK, "{\"when\":\"2024-01-02\",\"x\":1}"), input));
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task RawUnionWritesTheSuppliedCandidateWithTheConvertersOfTheClient(string serializer)
        {
            Assembly api = Api(serializer);
            object stamp = New(api, "Stamp", ("When", new DateTime(2024, 1, 2)), ("N", 3));

            (_, Phase4BodyWireHarness.Exchange stampWire) = await Call(
                api, "echoPayload", Json(HttpStatusCode.OK, "1"), Union(api, "Payload", "FromStamp", stamp));
            Assert.True(JToken.DeepEquals(JToken.Parse("{\"when\":\"2024-01-02\",\"n\":3}"), Body(stampWire)), stampWire.BodyText);

            (_, Phase4BodyWireHarness.Exchange numberWire) = await Call(
                api, "echoPayload", Json(HttpStatusCode.OK, "1"), Union(api, "Payload", "FromInt32", 7));
            Assert.Equal("7", numberWire.BodyText);

            (_, Phase4BodyWireHarness.Exchange textWire) = await Call(
                api, "echoPayload", Json(HttpStatusCode.OK, "1"), Union(api, "Payload", "FromString", "s\"q"));
            Assert.Equal("\"s\\\"q\"", textWire.BodyText);

            (_, Phase4BodyWireHarness.Exchange implicitWire) = await Call(
                api, "echoPayload", Json(HttpStatusCode.OK, "1"), ImplicitConvert(api, "Payload", 9));
            Assert.Equal("9", implicitWire.BodyText);
        }

        private static object ImplicitConvert(Assembly api, string union, object value)
        {
            Type target = TypeOf(api, union);
            MethodInfo op = target.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "op_Implicit" && method.GetParameters()[0].ParameterType == value.GetType());
            return op.Invoke(null, new[] { value })!;
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task RawJsonOfAUnionRoundTrips(string serializer)
        {
            Assembly api = Api(serializer);
            Type payload = TypeOf(api, "Payload");
            const string Json = "{\"when\":\"2024-01-02\",\"n\":4}";
            object raw = RawUnionOf(payload, serializer, Json);

            (_, Phase4BodyWireHarness.Exchange sent) = await Call(
                api, "echoPayload", SerializerMatrixHarness.Json(HttpStatusCode.OK, "1"), raw);
            Assert.True(JToken.DeepEquals(JToken.Parse(Json), Body(sent)), sent.BodyText);
            Assert.True(TryGet(raw, "TryGetStamp", out object? stamp));
            Assert.Equal(4, Get(stamp!, "N"));
            Assert.True(JToken.DeepEquals(JToken.Parse(Json), JToken.Parse(RawJsonOf(raw, serializer))));

            // a value supplied as a candidate converts to JSON with the default settings of the serializer
            object fromNumber = Union(api, "Payload", "FromInt32", 12);
            Assert.Equal("12", RawJsonOf(fromNumber, serializer));
            await Task.CompletedTask;
        }

        private static object RawUnionOf(Type union, string serializer, string json)
        {
            using var mode = new ModeScope(serializer);
            return MatrixMode.RawUnion(union, json);
        }

        private static string RawJsonOf(object union, string serializer)
        {
            using var mode = new ModeScope(serializer);
            return MatrixMode.RawJson(union);
        }

        // Sets the ambient serializer for the helpers of MatrixMode for the duration of a call.
        private sealed class ModeScope : IDisposable
        {
            private readonly string _previous;

            internal ModeScope(string serializer)
            {
                _previous = MatrixMode.Current;
                MatrixMode.Current = serializer;
            }

            public void Dispose()
            {
                MatrixMode.Current = _previous;
            }
        }

        // ---- unions inside other values -------------------------------------------------------------------

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task UnionsInsideAnObjectAnArrayAndAMapRoundTrip(string serializer)
        {
            Assembly api = Api(serializer);
            object holder = New(
                api,
                "Holder",
                ("Pet", Union(api, "Pet", "FromDog", New(api, "Dog", ("Bark", "b")))),
                ("Payloads", MakeList(api, "Payload", Union(api, "Payload", "FromInt32", 1), Union(api, "Payload", "FromString", "two"))),
                ("ByName", MakeMap(api, "Pet", ("c", Union(api, "Pet", "FromCat", New(api, "Cat", ("Name", "x")))))));
            const string Response =
                "{\"pet\":{\"kind\":\"cat\",\"name\":\"c\"},\"payloads\":[3,\"four\",{\"when\":\"2024-05-06\"}]," +
                "\"byName\":{\"d\":{\"kind\":\"dog\",\"bark\":\"w\"}}}";

            (object? result, Phase4BodyWireHarness.Exchange sent) = await Call(
                api, "echoHolder", Json(HttpStatusCode.OK, Response), holder);

            Assert.True(
                JToken.DeepEquals(
                    JToken.Parse("{\"pet\":{\"bark\":\"b\",\"kind\":\"dog\"},\"payloads\":[1,\"two\"],\"byName\":{\"c\":{\"name\":\"x\",\"kind\":\"cat\"}}}"),
                    Body(sent)),
                sent.BodyText);
            Assert.Equal("cat", Get(Get(result!, "Pet")!, "DiscriminatorValue"));
            var payloads = ((IEnumerable)Get(result!, "Payloads")!).Cast<object>().ToArray();
            Assert.Equal(3, payloads.Length);
            Assert.True(TryGet(payloads[0], "TryGetInt32", out object? three));
            Assert.Equal(3, three);
            Assert.True(TryGet(payloads[1], "TryGetString", out object? four));
            Assert.Equal("four", four);
            Assert.True(TryGet(payloads[2], "TryGetStamp", out object? stamp));
            Assert.Equal(new DateTime(2024, 5, 6), Get(stamp!, "When"));
            var byName = (IDictionary)Get(result!, "ByName")!;
            Assert.Equal("dog", Get(byName["d"]!, "DiscriminatorValue"));

            Exception bad = await Rejects(serializer, () => Call(api, "echoHolder", Json(HttpStatusCode.OK, "{\"byName\":{\"x\":{\"kind\":\"bird\"}}}"), holder));
            Assert.Contains("$.byName.x.kind", bad.Message);
        }

        private static object MakeList(Assembly api, string elementType, params object[] items)
        {
            Type list = typeof(List<>).MakeGenericType(TypeOf(api, elementType));
            var instance = (IList)Activator.CreateInstance(list)!;
            foreach (object item in items)
            {
                instance.Add(item);
            }

            return instance;
        }

        private static object MakeMap(Assembly api, string valueType, params (string Key, object Value)[] entries)
        {
            Type map = typeof(Dictionary<,>).MakeGenericType(typeof(string), TypeOf(api, valueType));
            var instance = (IDictionary)Activator.CreateInstance(map)!;
            foreach ((string key, object value) in entries)
            {
                instance[key] = value;
            }

            return instance;
        }

        // ---- the surface of the wrapper -----------------------------------------------------------------

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public void TheRawFormFactoryTakesTheNameOfTheLibraryAndAMemberNamedLikeItIsRenamed(string serializer)
        {
            Assembly api = Api(serializer);
            Type odd = TypeOf(api, "Odd");
            string[] statics = odd.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(static method => method.Name.StartsWith("From", StringComparison.Ordinal))
                .Select(static method => method.Name + "(" + method.GetParameters()[0].ParameterType.Name + ")")
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            if (IsStj(serializer))
            {
                // FromJsonElement is the raw form; the member named JsonElement is renamed, JToken is not special.
                Assert.Equal(
                    new[] { "FromJToken(JToken)", "FromJsonElement(JsonElement)", "FromJsonElement2(JsonElement)" },
                    statics.OrderBy(static name => name, StringComparer.Ordinal).ToArray());
                Assert.NotNull(odd.GetMethod("ToJsonElement", Type.EmptyTypes));
                Assert.Null(odd.GetMethod("ToJToken"));
            }
            else
            {
                Assert.Equal(
                    new[] { "FromJToken(JToken)", "FromJToken2(JToken)", "FromJsonElement(JsonElement)" },
                    statics.OrderBy(static name => name, StringComparer.Ordinal).ToArray());
                Assert.NotNull(odd.GetMethod("ToJToken"));
                Assert.Null(odd.GetMethod("ToJsonElement"));
            }
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task AnyOfWithMembersNamedAfterTheLibraryTypesStillReadsAndWrites(string serializer)
        {
            Assembly api = Api(serializer);
            object x = New(api, "JsonElement", ("X", "v"));
            string factory = IsStj(serializer) ? "FromJsonElement2" : "FromJsonElement";

            (object? result, Phase4BodyWireHarness.Exchange sent) = await Call(
                api, "echoOdd", Json(HttpStatusCode.OK, "{\"y\":3}"), Union(api, "Odd", factory, x));

            Assert.True(JToken.DeepEquals(JToken.Parse("{\"x\":\"v\"}"), Body(sent)), sent.BodyText);
            Assert.True(TryGet(result!, IsStj(serializer) ? "TryGetJToken" : "TryGetJToken2", out object? y));
            Assert.Equal(3L, Convert.ToInt64(Get(y!, "Y")));
        }

        // ---- System.Text.Json: the raw form of the wrapper --------------------------------------------------

        [Fact]
        public void RawUnionReadersOfSystemTextJsonDocumentThatFromJsonElementValuesUseDefaultOptions()
        {
            string stj = Phase4GeneratorTestHarness.GenerateAndCompile(
                Spec, options: new Phase4CompilationOptions { JsonSerializer = SystemTextJson, Newtonsoft = false }).GeneratedSource;
            string newtonsoft = Phase4GeneratorTestHarness.GenerateAndCompile(Spec).GeneratedSource;

            Assert.Contains("A value created by FromJsonElement is read with default JsonSerializerOptions", stj);
            Assert.DoesNotContain("A value created by FromJsonElement", newtonsoft);
        }

        [Fact]
        public void FromJsonElementKeepsACopyThatOutlivesTheDocument()
        {
            Assembly api = Api(SystemTextJson);
            Type payload = TypeOf(api, "Payload");
            JsonElement element;
            using (JsonDocument document = JsonDocument.Parse("{\"when\":\"2024-01-02\",\"n\":2}"))
            {
                element = document.RootElement;
                object raw = payload.GetMethod("FromJsonElement")!.Invoke(null, new object[] { element })!;
                document.Dispose();

                Assert.Equal("{\"when\":\"2024-01-02\",\"n\":2}", ((JsonElement)raw.GetType().GetMethod("ToJsonElement", Type.EmptyTypes)!.Invoke(raw, null)!).GetRawText());
                Assert.True(TryGet(raw, "TryGetStamp", out object? stamp));
                Assert.Equal(2, Get(stamp!, "N"));
            }
        }

        [Fact]
        public void FromJsonElementRejectsAnUninitializedElement()
        {
            Assembly api = Api(SystemTextJson);
            MethodInfo factory = TypeOf(api, "Payload").GetMethod("FromJsonElement")!;

            var failure = Assert.Throws<TargetInvocationException>(() => factory.Invoke(null, new object[] { default(JsonElement) }));

            Assert.IsType<ArgumentException>(failure.InnerException);
        }

        [Fact]
        public void ToJsonElementTakesTheOptionsThatConvertASuppliedCandidate()
        {
            Assembly api = Api(SystemTextJson);
            Type payload = TypeOf(api, "Payload");
            object stamp = New(api, "Stamp", ("When", new DateTime(2024, 1, 2)), ("N", 3));
            object union = payload.GetMethod("FromStamp")!.Invoke(null, new[] { stamp })!;
            MethodInfo toElement = payload.GetMethod("ToJsonElement", new[] { typeof(JsonSerializerOptions) })!;

            // The default options write a DateTime as a date-time; the converter of the client writes a full-date,
            // so a caller that needs the wire form hands over its own options.
            var withDefaults = (JsonElement)toElement.Invoke(union, new object?[] { null })!;
            Assert.Contains("2024-01-02T00:00:00", withDefaults.GetRawText());

            var options = new JsonSerializerOptions();
            options.Converters.Add(new FullDateConverter());
            var withOptions = (JsonElement)toElement.Invoke(union, new object?[] { options })!;
            Assert.Equal("2024-01-02", withOptions.GetProperty("when").GetString());
        }

        private sealed class FullDateConverter : System.Text.Json.Serialization.JsonConverter<DateTime>
        {
            public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return DateTime.Parse(reader.GetString()!);
            }

            public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
