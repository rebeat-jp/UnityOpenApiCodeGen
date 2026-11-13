using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Newtonsoft.Json;

using Xunit;

using static Rhycol.OpenApiCodeGen.SourceGenerator.Tests.Phase4BodyWireHarness;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P4 (G21-G26): request bodies (multipart, form-urlencoded, text, binary, several media types),
    /// success contracts (text, binary, per-status wrapper, ranges, default only, omitted responses),
    /// typed errors and response headers, checked on the wire against a fake HttpMessageHandler. The
    /// client is compiled from ApiSurfaceSpecs/bodies.json.
    /// </summary>
    public sealed class Phase4BodyAndResponseTests
    {
        private static System.Net.Http.HttpResponseMessage CreatedResponse() =>
            Respond(HttpStatusCode.Created, "{\"id\":1,\"name\":\"Rex\"}");

        // ---- the fixture itself ---------------------------------------------------------------------

        [Fact]
        public void BodiesSpecGeneratesWithoutDiagnostics()
        {
            string spec = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", "bodies.json"));
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void EveryRequestMediaTypeGetsItsOwnMethod()
        {
            Type api = Phase4WireHarness.ClientType(Bodies);
            string[] names = api.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(static method => method.Name)
                .Where(static name => name.StartsWith("createPet", StringComparison.Ordinal))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                new[]
                {
                    "createPet", "createPetAsBinary", "createPetAsFormUrlEncoded", "createPetAsMultipart", "createPetAsText"
                },
                names);
            Assert.Equal(TypeOf("Pet"), api.GetMethod("createPet")!.GetParameters()[0].ParameterType);
            Assert.Equal(TypeOf("PetForm"), api.GetMethod("createPetAsMultipart")!.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(string), api.GetMethod("createPetAsText")!.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(byte[]), api.GetMethod("createPetAsBinary")!.GetParameters()[0].ParameterType);
        }

        // ---- request: json / text / binary ----------------------------------------------------------

        [Fact]
        public async Task JsonBodyIsSentByThePrimaryMethod()
        {
            object pet = New("Pet", ("Name", "Rex"));

            (object? result, Exchange sent) = await Call("createPet", CreatedResponse(), pet);

            Assert.Equal("application/json; charset=utf-8", sent.ContentType);
            Assert.Contains("\"name\":\"Rex\"", sent.BodyText);
            Assert.Equal("Rex", Get<string>(result!, "Name"));
        }

        [Fact]
        public async Task TextBodyIsSentAsUtf8Text()
        {
            (_, Exchange sent) = await Call("createPetAsText", CreatedResponse(), "héllo");

            Assert.Equal("text/plain; charset=utf-8", sent.ContentType);
            Assert.Equal("héllo", Encoding.UTF8.GetString(sent.Body));
        }

        [Fact]
        public async Task BinaryBodyIsSentAsOctetStream()
        {
            byte[] payload = { 0, 1, 2, 255, 254 };

            (_, Exchange sent) = await Call("createPetAsBinary", CreatedResponse(), payload);

            Assert.Equal("application/octet-stream", sent.ContentType);
            Assert.Equal(payload, sent.Body);
        }

        [Fact]
        public async Task WildcardBinaryMediaTypeTakesTheCallerContentTypeOrOctetStream()
        {
            byte[] payload = { 7, 8, 9 };
            var noContent = new Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage>(
                _ => Respond(HttpStatusCode.NoContent));

            (_, Exchange withType) = await Call(Bodies, "uploadAvatar", noContent, payload, System.Threading.CancellationToken.None, "image/png");
            (_, Exchange withoutType) = await Call(Bodies, "uploadAvatar", noContent, payload);

            Assert.Equal("image/png", withType.ContentType);
            Assert.Equal(payload, withType.Body);
            Assert.Equal("application/octet-stream", withoutType.ContentType);
        }

        [Fact]
        public async Task OptionalBodiesAreOmittedWhenNull()
        {
            var noContent = new Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage>(
                _ => Respond(HttpStatusCode.NoContent));

            (_, Exchange binary) = await Call(Bodies, "uploadAvatar", noContent, new object?[] { null });
            (_, Exchange json) = await Call(Bodies, "postOptional", noContent, new object?[] { null });
            (_, Exchange withJson) = await Call(Bodies, "postOptional", noContent, New("Pet", ("Name", "x")));

            Assert.Empty(binary.Body);
            Assert.Null(binary.ContentType);
            Assert.Empty(json.Body);
            Assert.Null(json.ContentType);
            Assert.Contains("\"name\":\"x\"", withJson.BodyText);
        }

        [Fact]
        public async Task RequiredBodyRejectsNull()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => Call("createPetAsText", CreatedResponse(), new object?[] { null }));
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => Call("createPetAsMultipart", CreatedResponse(), new object?[] { null }));
        }

        // ---- request: multipart ----------------------------------------------------------------------

        [Fact]
        public async Task MultipartWritesBoundaryPartsArrayPartsAndEncoding()
        {
            Type fileType = TypeOf("Phase4ApiFileParameter");
            var attachments = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(fileType))!;
            attachments.Add(NewFile(Encoding.ASCII.GetBytes("AAA"), "a.txt", "text/plain"));
            attachments.Add(NewFile(new MemoryStream(Encoding.ASCII.GetBytes("BBB")), "b.bin"));
            object form = New(
                "PetForm",
                ("Name", "Rex"),
                ("Count", 3),
                ("Tags", new List<string> { "a", "b" }),
                ("Photo", NewFile(Encoding.ASCII.GetBytes("PNG!"), "p.png")),
                ("Attachments", attachments),
                ("Meta", New("Meta", ("Origin", "x"))));

            (_, Exchange sent) = await Call("createPetAsMultipart", CreatedResponse(), form);
            List<Part> parts = ReadMultipart(sent, out string boundary);

            Assert.StartsWith("multipart/form-data", sent.ContentType);
            Assert.False(string.IsNullOrEmpty(boundary));
            // Parts follow the DTO property order (wire name order): attachments, count, meta, name, photo, tags.
            Assert.Equal(
                new[] { "attachments", "attachments", "count", "meta", "name", "photo", "tags", "tags" },
                parts.Select(static part => ExtractParameter(part.Disposition, "name")).ToArray());

            Part first = parts[0];
            Assert.Equal("a.txt", ExtractParameter(first.Disposition, "filename"));
            Assert.Equal("text/plain", first.ContentType);
            Assert.Equal("AAA", first.Body);
            Part second = parts[1];
            Assert.Equal("b.bin", ExtractParameter(second.Disposition, "filename"));
            Assert.Equal("application/octet-stream", second.ContentType);
            Assert.Equal("BBB", second.Body);

            Part count = parts[2];
            Assert.Equal("3", count.Body);
            Assert.StartsWith("text/plain", count.ContentType);
            Assert.Null(ExtractParameter(count.Disposition, "filename"));

            Part meta = parts[3];
            Assert.Equal("application/json", meta.ContentType);
            Assert.Equal("{\"origin\":\"x\"}", meta.Body);

            Assert.Equal("Rex", parts[4].Body);

            Part photo = parts[5];
            Assert.Equal("p.png", ExtractParameter(photo.Disposition, "filename"));
            // The Encoding Object declares image/png and a static part header.
            Assert.Equal("image/png", photo.ContentType);
            Assert.Equal("avatar", photo.Headers["X-Part-Note"]);
            Assert.Equal("PNG!", photo.Body);

            Assert.Equal("a", parts[6].Body);
            Assert.Equal("b", parts[7].Body);
        }

        [Fact]
        public async Task MultipartOmitsUnsetOptionalPartsAndTheFileContentTypeOverridesTheDeclaredOne()
        {
            object form = New(
                "PetForm",
                ("Name", "Rex"),
                ("Photo", NewFile(new byte[] { 1, 2 }, null, "image/jpeg")));

            (_, Exchange sent) = await Call("createPetAsMultipart", CreatedResponse(), form);
            List<Part> parts = ReadMultipart(sent, out _);

            Assert.Equal(
                new[] { "name", "photo" },
                parts.Select(static part => ExtractParameter(part.Disposition, "name")).ToArray());
            Assert.Equal("image/jpeg", parts[1].ContentType);
            Assert.Null(ExtractParameter(parts[1].Disposition, "filename"));
        }

        [Fact]
        public async Task MultipartStreamFilesAreDisposedWithTheRequest()
        {
            var stream = new MemoryStream(Encoding.ASCII.GetBytes("data"));
            object form = New("PetForm", ("Name", "Rex"), ("Photo", NewFile(stream, "s.png")));

            await Call("createPetAsMultipart", CreatedResponse(), form);

            Assert.False(stream.CanRead);
        }

        // ---- request: form-urlencoded ----------------------------------------------------------------

        [Fact]
        public async Task FormUrlEncodedUsesEncodingStylesAndPercentEncodesSpaces()
        {
            object fields = New(
                "PetFields",
                ("Name", "R x&y"),
                ("Tags", new List<string> { "a", "b" }),
                ("Attrs", new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }),
                ("Note", "a/b c"),
                ("Meta", New("Meta", ("Origin", "é"))));

            (_, Exchange sent) = await Call("createPetAsFormUrlEncoded", CreatedResponse(), fields);

            Assert.Equal("application/x-www-form-urlencoded", sent.ContentType);
            // attrs (form, explode) -> a=1&b=2; meta (object, explode) -> origin=...; name: %20 and %26;
            // note: allowReserved keeps '/'; tags: pipeDelimited, explode false.
            Assert.Equal(
                "a=1&b=2&origin=%C3%A9&name=R%20x%26y&note=a/b%20c&tags=a|b",
                sent.BodyText);
        }

        // ---- responses: binary / text / several media types ------------------------------------------

        [Fact]
        public async Task BinaryResponseIsReturnedAsBytesAndRequestsItsMediaType()
        {
            byte[] payload = { 0, 255, 10, 13, 1 };

            (object? result, Exchange sent) = await Call(
                "downloadFile",
                RespondBytes(HttpStatusCode.OK, payload, "application/octet-stream"));

            Assert.Equal(payload, Assert.IsType<byte[]>(result));
            Assert.Equal("application/octet-stream", sent.Headers["Accept"]);
        }

        [Fact]
        public async Task TextResponseIsReturnedAsStringAndMismatchedContentTypeIsRejected()
        {
            (object? result, Exchange sent) = await Call(
                "getNote", Respond(HttpStatusCode.OK, "héllo", "text/plain"));

            Assert.Equal("héllo", result);
            Assert.Equal("text/plain", sent.Headers["Accept"]);
            await Assert.ThrowsAsync<JsonSerializationException>(
                () => Call("getNote", Respond(HttpStatusCode.OK, "{}", "application/json")));
        }

        [Fact]
        public async Task SeveralResponseMediaTypesSendAnAcceptListAndDecodeByTheReceivedContentType()
        {
            (object? json, Exchange sent) = await Call(
                "getReport", Respond(HttpStatusCode.OK, "\"from json\"", "application/json"));
            (object? text, _) = await Call(
                "getReport", Respond(HttpStatusCode.OK, "from text", "text/plain; charset=utf-8"));

            Assert.Equal("application/json,text/plain;q=0.9", sent.Headers["Accept"].Replace(" ", string.Empty));
            Assert.Equal("from json", json);
            Assert.Equal("from text", text);
            await Assert.ThrowsAsync<JsonSerializationException>(
                () => Call("getReport", RespondBytes(HttpStatusCode.OK, new byte[] { 1 }, "image/png")));
        }

        [Fact]
        public async Task DeclaredRedirectIsJudgedOnTheFinalResponse()
        {
            (object? ok, _) = await Call("getRedirect", Respond(HttpStatusCode.OK, "done", "text/plain"));
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(
                () => Call("getRedirect", Respond(HttpStatusCode.Found, null)));

            Assert.Equal("done", ok);
            Assert.Equal("Phase4ApiException", failure.GetType().Name);
            Assert.Equal(HttpStatusCode.Found, Get<HttpStatusCode>(failure, "StatusCode"));
        }

        // ---- responses: different contracts, ranges, default only, omitted ---------------------------

        [Fact]
        public async Task DifferentSuccessStatusesReturnAStatusKeyedWrapper()
        {
            (object? ok, Exchange sent) = await Call(
                "getStatus", Respond(HttpStatusCode.OK, "{\"id\":4,\"name\":\"Rex\"}"));
            (object? created, _) = await Call(
                "getStatus", Respond(HttpStatusCode.Created, "{\"id\":9}"));
            (object? accepted, _) = await Call("getStatus", Respond(HttpStatusCode.Accepted));

            Assert.Equal("GetStatusResponse", ok!.GetType().Name);
            Assert.Equal("application/json", sent.Headers["Accept"]);
            Assert.True(Get<bool>(ok, "IsStatus200"));
            Assert.False(Get<bool>(ok, "IsStatus201"));
            Assert.Equal("Rex", Get<string>(GetOrNull(ok, "Status200")!, "Name"));
            Assert.Null(GetOrNull(ok, "Status201"));
            Assert.Equal(HttpStatusCode.OK, Get<HttpStatusCode>(ok, "StatusCode"));

            Assert.True(Get<bool>(created!, "IsStatus201"));
            Assert.Equal(9, Get<int>(GetOrNull(created!, "Status201")!, "Id"));
            Assert.Null(GetOrNull(created!, "Status200"));

            Assert.True(Get<bool>(accepted!, "IsStatus202"));
            Assert.Equal(HttpStatusCode.Accepted, Get<HttpStatusCode>(accepted!, "StatusCode"));
        }

        [Fact]
        public async Task StatusRangeWithATypedBodyReturnsTheBody()
        {
            (object? ok, _) = await Call("getRange", Respond(HttpStatusCode.OK, "{\"name\":\"a\"}"));
            (object? edge, _) = await Call("getRange", Respond((HttpStatusCode)299, "{\"name\":\"b\"}"));
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(
                () => Call("getRange", Respond(HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}")));

            Assert.Equal("a", Get<string>(ok!, "Name"));
            Assert.Equal("b", Get<string>(edge!, "Name"));
            Assert.Equal("boom", Get<string>(Get<object>(failure, "Error"), "Message"));
            await Assert.ThrowsAnyAsync<Exception>(
                () => Call("getRange", Respond(HttpStatusCode.MultipleChoices, "{}")));
        }

        [Fact]
        public async Task DefaultOnlyResponseIsTheSuccessContract()
        {
            (object? ok, _) = await Call("getFallback", Respond(HttpStatusCode.OK, "{\"name\":\"d\"}"));
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(
                () => Call("getFallback", Respond(HttpStatusCode.NotFound, "{\"name\":\"d\"}")));

            Assert.Equal("d", Get<string>(ok!, "Name"));
            Assert.Equal(HttpStatusCode.NotFound, Get<HttpStatusCode>(failure, "StatusCode"));
        }

        [Fact]
        public async Task OmittedResponsesSucceedOnAny2xx()
        {
            (object? ok, _) = await Call("getSilent", Respond(HttpStatusCode.OK, "{\"anything\":true}"));
            (object? noContent, _) = await Call("getSilent", Respond(HttpStatusCode.NoContent));
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(
                () => Call("getSilent", Respond(HttpStatusCode.BadGateway, "bad")));

            Assert.Null(ok);
            Assert.Null(noContent);
            Assert.Equal("bad", Get<string>(failure, "ResponseBody"));
        }

        // ---- typed errors -----------------------------------------------------------------------------

        [Fact]
        public async Task DeclaredErrorBodyIsAvailableOnTheException()
        {
            Exception badRequest = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                "createPet",
                Respond(HttpStatusCode.BadRequest, "{\"title\":\"nope\",\"status\":400}"),
                New("Pet", ("Name", "x"))));
            Exception other = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                "createPet",
                Respond(HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}"),
                New("Pet", ("Name", "x"))));

            Assert.Equal("Phase4ApiException", badRequest.GetType().Name);
            Assert.Equal("{\"title\":\"nope\",\"status\":400}", Get<string>(badRequest, "ResponseBody"));
            Assert.Equal(HttpStatusCode.BadRequest, Get<HttpStatusCode>(badRequest, "StatusCode"));
            object problem = Get<object>(badRequest, "Error");
            Assert.Equal(TypeOf("Problem"), problem.GetType());
            Assert.Equal("nope", Get<string>(problem, "Title"));
            Assert.Null(GetOrNull(badRequest, "ErrorDeserializationException"));

            // default: any other status.
            Assert.Equal(TypeOf("GenericError"), Get<object>(other, "Error").GetType());
            Assert.Equal("boom", Get<string>(Get<object>(other, "Error"), "Message"));
        }

        [Fact]
        public async Task TryGetErrorReturnsTheErrorOfTheRequestedType()
        {
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                "createPet",
                Respond(HttpStatusCode.BadRequest, "{\"title\":\"nope\"}"),
                New("Pet", ("Name", "x"))));
            MethodInfo method = failure.GetType().GetMethod("TryGetError")!;

            var problemArguments = new object?[] { null };
            Assert.True((bool)method.MakeGenericMethod(TypeOf("Problem")).Invoke(failure, problemArguments)!);
            Assert.Equal("nope", Get<string>(problemArguments[0]!, "Title"));
            Assert.False((bool)method.MakeGenericMethod(TypeOf("GenericError")).Invoke(failure, new object?[] { null })!);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("{\"status\":1}")]
        [InlineData("")]
        public async Task UnreadableErrorBodyKeepsTheTextAndDoesNotHideTheApiException(string body)
        {
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                "createPet",
                Respond(HttpStatusCode.BadRequest, body, "text/plain"),
                New("Pet", ("Name", "x"))));

            Assert.Equal("Phase4ApiException", failure.GetType().Name);
            Assert.Equal(body, Get<string>(failure, "ResponseBody"));
            Assert.Null(GetOrNull(failure, "Error"));
            if (body.Length > 0)
            {
                // The deserialization failure is kept (not swallowed) next to the raw text.
                Assert.NotNull(GetOrNull(failure, "ErrorDeserializationException"));
            }
        }

        [Fact]
        public async Task ErrorWithoutADeclaredBodyKeepsOnlyTheText()
        {
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                "downloadFile", Respond(HttpStatusCode.NotFound, "{\"title\":\"x\"}")));

            Assert.Equal("{\"title\":\"x\"}", Get<string>(failure, "ResponseBody"));
            Assert.Null(GetOrNull(failure, "Error"));
        }

        [Fact]
        public async Task ErrorOfAStatusKeyedOperationIsTyped()
        {
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                "getStatus", Respond(HttpStatusCode.NotFound, "{\"title\":\"gone\"}")));

            Assert.Equal("gone", Get<string>(Get<object>(failure, "Error"), "Title"));
        }

        // ---- response headers -------------------------------------------------------------------------

        [Fact]
        public async Task WithHttpInfoReturnsStatusRawAndTypedHeadersAndData()
        {
            System.Net.Http.HttpResponseMessage response = Respond(
                HttpStatusCode.OK,
                "{\"name\":\"Rex\"}",
                "application/json",
                ("X-Rate-Limit", "42"),
                ("X-Tags", "a, b"),
                ("X-Trace", "t-1"));

            (object? info, _) = await Call("getWithHeadersWithHttpInfo", response);

            Assert.Equal("GetWithHeadersHttpInfo", info!.GetType().Name);
            Assert.Equal(HttpStatusCode.OK, Get<HttpStatusCode>(info, "StatusCode"));
            Assert.Equal(42, Get<int>(info, "XRateLimit"));
            Assert.Equal(new List<string> { "a", "b" }, Get<List<string>>(info, "XTags"));
            Assert.Equal("t-1", Get<string>(info, "XTrace"));
            Assert.Equal("Rex", Get<string>(Get<object>(info, "Data"), "Name"));
            var raw = Get<IReadOnlyDictionary<string, string[]>>(info, "Headers");
            Assert.Equal(new[] { "42" }, raw["x-rate-limit"]);
            Assert.True(raw.ContainsKey("Content-Type"));
        }

        [Fact]
        public async Task OptionalHeadersAreNullAndAMissingRequiredHeaderIsAnError()
        {
            (object? info, _) = await Call(
                "getWithHeadersWithHttpInfo",
                Respond(HttpStatusCode.OK, "{\"name\":\"Rex\"}", "application/json", ("X-Rate-Limit", "1")));

            Assert.Null(GetOrNull(info!, "XTags"));
            Assert.Null(GetOrNull(info!, "XTrace"));
            JsonSerializationException missing = await Assert.ThrowsAsync<JsonSerializationException>(() => Call(
                "getWithHeadersWithHttpInfo",
                Respond(HttpStatusCode.OK, "{\"name\":\"Rex\"}")));
            Assert.Contains("X-Rate-Limit", missing.Message);
            await Assert.ThrowsAsync<JsonSerializationException>(() => Call(
                "getWithHeadersWithHttpInfo",
                Respond(HttpStatusCode.OK, "{\"name\":\"Rex\"}", "application/json", ("X-Rate-Limit", "many"))));
        }

        [Fact]
        public async Task TheDataMethodStillReturnsTheBodyAndAlsoChecksRequiredHeaders()
        {
            (object? pet, _) = await Call(
                "getWithHeaders",
                Respond(HttpStatusCode.OK, "{\"name\":\"Rex\"}", "application/json", ("X-Rate-Limit", "1")));

            Assert.Equal("Rex", Get<string>(pet!, "Name"));
            await Assert.ThrowsAsync<JsonSerializationException>(
                () => Call("getWithHeaders", Respond(HttpStatusCode.OK, "{\"name\":\"Rex\"}")));
        }

        [Fact]
        public async Task HeaderValuesOfEveryScalarKindAreParsedWithTheInvariantCulture()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ("{'openapi':'3.1.0','info':{'title':'t','version':'1'},'servers':[{'url':'https://api.example.test'}],'paths':{'/x':{'get':{'operationId':'probe','responses':{" +
                 "'200':{'description':'ok','headers':{" +
                 "'X-Int':{'schema':{'type':'integer'}},'X-Long':{'schema':{'type':'integer','format':'int64'}}," +
                 "'X-Double':{'schema':{'type':'number'}},'X-Bool':{'schema':{'type':'boolean'}}," +
                 "'X-Date':{'schema':{'type':'string','format':'date'}},'X-Time':{'schema':{'type':'string','format':'date-time'}}," +
                 "'X-Guid':{'schema':{'type':'string','format':'uuid'}},'X-Enum':{'schema':{'type':'string','enum':['red','blue']}}," +
                 "'X-Ints':{'schema':{'type':'array','items':{'type':'integer'}}}}}," +
                 "'202':{'description':'accepted','headers':{'X-Int':{'schema':{'type':'integer'}}},'content':{'text/plain':{'schema':{'type':'integer'}}}}}}}}}")
                .Replace('\'', '"'));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            System.Reflection.Assembly assembly = execution.EmitAssembly();

            (object? info, _) = await Call(
                assembly,
                "probeWithHttpInfo",
                _ => Respond(
                    HttpStatusCode.OK,
                    null,
                    "application/json",
                    ("X-Int", "-3"), ("X-Long", "9000000000"), ("X-Double", "1.5"), ("X-Bool", "true"),
                    ("X-Date", "2026-11-14"), ("X-Time", "2026-11-14T01:02:03+09:00"),
                    ("X-Guid", "0d8f9d2c-4a43-4d2e-9a2b-111111111111"), ("X-Enum", "blue"), ("X-Ints", "1, 2,3")));

            Assert.Equal(-3, Get<int?>(info!, "XInt"));
            Assert.Equal(9000000000L, Get<long?>(info!, "XLong"));
            Assert.Equal(1.5, Get<double?>(info!, "XDouble"));
            Assert.True(Get<bool?>(info!, "XBool"));
            Assert.Equal(new DateTime(2026, 11, 14), Get<DateTime?>(info!, "XDate"));
            Assert.Equal(new DateTimeOffset(2026, 11, 14, 1, 2, 3, TimeSpan.FromHours(9)), Get<DateTimeOffset?>(info!, "XTime"));
            Assert.Equal(Guid.Parse("0d8f9d2c-4a43-4d2e-9a2b-111111111111"), Get<Guid?>(info!, "XGuid"));
            Assert.Equal("Blue", GetOrNull(info!, "XEnum")!.ToString());
            Assert.Equal(new List<int> { 1, 2, 3 }, Get<List<int>>(info!, "XInts"));

            // A wrapper (200 without a body, 202 with a text integer) combined with headers.
            object? wrapper = Get<object>(info!, "Data");
            Assert.True(Get<bool>(wrapper!, "IsStatus200"));
            (object? accepted, _) = await Call(
                assembly, "probeWithHttpInfo", _ => Respond(HttpStatusCode.Accepted, "42", "text/plain", ("X-Int", "7")));
            Assert.Equal(7, Get<int?>(accepted!, "XInt"));
            Assert.Equal(42, Get<int?>(Get<object>(accepted!, "Data"), "Status202"));
        }

        [Fact]
        public async Task DeclaredErrorStatusWithoutJsonDoesNotFallBackToDefault()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ("{'openapi':'3.1.0','info':{'title':'t','version':'1'},'servers':[{'url':'https://api.example.test'}],'paths':{'/x':{'get':{'operationId':'probe','responses':{" +
                 "'204':{'description':'ok'},'404':{'description':'missing','content':{'text/plain':{'schema':{'type':'string'}}}}," +
                 "'default':{'description':'err','content':{'application/json':{'schema':{'type':'object','properties':{'m':{'type':'string'}}}}}}}}}}}")
                .Replace('\'', '"'));
            Assert.Empty(execution.RunResult.Diagnostics);
            System.Reflection.Assembly assembly = execution.EmitAssembly();

            Exception notFound = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                assembly, "probe", _ => Respond(HttpStatusCode.NotFound, "{\"m\":\"x\"}")));
            Exception other = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                assembly, "probe", _ => Respond(HttpStatusCode.InternalServerError, "{\"m\":\"x\"}")));

            Assert.Null(GetOrNull(notFound, "Error"));
            Assert.Null(GetOrNull(notFound, "ErrorDeserializationException"));
            Assert.Equal("x", Get<string>(Get<object>(other, "Error"), "M"));
        }

        [Fact]
        public void UngeneratableErrorSchemaReportsOacg108AndStillGenerates()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ("{'openapi':'3.1.0','info':{'title':'t','version':'1'},'paths':{'/x':{'get':{'operationId':'probe','responses':{" +
                 "'204':{'description':'ok'},'400':{'description':'bad','content':{'application/json':{'schema':{'type':'object','if':{}}}}}}}}}}")
                .Replace('\'', '"'));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("cannot be generated", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public async Task MultipartContentDispositionIsBuiltPerRfc7578()
        {
            var files = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(TypeOf("Phase4ApiFileParameter")))!;
            files.Add(NewFile(new byte[] { 1 }, "we\"ird\\naéme.txt"));
            object form = New("PetForm", ("Name", "Rex"), ("Attachments", files));

            (_, Exchange sent) = await Call("createPetAsMultipart", CreatedResponse(), form);
            List<Part> parts = ReadMultipart(sent, out _);

            Assert.Equal("form-data; name=\"attachments\"; filename=\"we\\\"ird\\\\na_me.txt\"; filename*=UTF-8''we%22ird%5Cna%C3%A9me.txt", parts[0].Disposition);
            Assert.Equal("form-data; name=\"name\"", parts[1].Disposition);

            var bad = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(TypeOf("Phase4ApiFileParameter")))!;
            bad.Add(NewFile(new byte[] { 1 }, "a\r\nb.txt"));
            await Assert.ThrowsAsync<ArgumentException>(
                () => Call("createPetAsMultipart", CreatedResponse(), New("PetForm", ("Name", "Rex"), ("Attachments", bad))));
        }

        [Fact]
        public async Task MultipartPropertyNamesAreQuotedAndPercentEncoded()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ("{'openapi':'3.1.0','info':{'title':'t','version':'1'},'servers':[{'url':'https://api.example.test'}],'paths':{'/x':{'post':{'operationId':'probe'," +
                 "'requestBody':{'content':{'multipart/form-data':{'schema':{'type':'object','properties':{'QUOTED':{'type':'string'},'名前':{'type':'string'},'x😀':{'type':'string'}}}}}}," +
                 "'responses':{'204':{'description':'ok'}}}}}}").Replace('\'', '"').Replace("QUOTED", "na\\\"me"));
            Assert.Empty(execution.RunResult.Diagnostics);
            System.Reflection.Assembly assembly = execution.EmitAssembly();
            Type dto = Phase4WireHarness.ClientType(assembly).GetMethod("probe")!.GetParameters()[0].ParameterType;
            object body = Activator.CreateInstance(dto)!;
            foreach (var property in dto.GetProperties().Where(static p => p.PropertyType == typeof(string)))
            {
                property.SetValue(body, "v");
            }

            (_, Exchange sent) = await Call(assembly, "probe", _ => Respond(HttpStatusCode.NoContent), body);
            List<Part> parts = ReadMultipart(sent, out _);

            Assert.Contains(parts, part => part.Disposition == "form-data; name=\"na\\\"me\"");
            Assert.Contains(parts, part => part.Disposition == "form-data; name=\"%E5%90%8D%E5%89%8D\"");
            // A surrogate pair is one code point: four UTF-8 octets, not two U+FFFD replacements.
            Assert.Contains(parts, part => part.Disposition == "form-data; name=\"x%F0%9F%98%80\"");
        }

        [Fact]
        public async Task ResponseContentTypeMatchingIgnoresCaseParametersAndSuffixRanges()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ("{'openapi':'3.1.0','info':{'title':'t','version':'1'},'servers':[{'url':'https://api.example.test'}],'paths':{'/x':{'get':{'operationId':'probe','responses':{" +
                 "'200':{'description':'ok','content':{'application/json':{'schema':{'type':'string'}},'application/*+json':{'schema':{'type':'string'}},'text/plain':{'schema':{'type':'string'}}}}}}}}}")
                .Replace('\'', '"'));
            Assert.Empty(execution.RunResult.Diagnostics);
            System.Reflection.Assembly assembly = execution.EmitAssembly();

            foreach (string contentType in new[] { "application/json; charset=utf-8", "Application/JSON", "application/problem+json" })
            {
                (object? json, _) = await Call(assembly, "probe", _ => Respond(HttpStatusCode.OK, "\"j\"", contentType));
                Assert.Equal("j", json);
            }

            (object? text, _) = await Call(assembly, "probe", _ => Respond(HttpStatusCode.OK, "t", "TEXT/Plain; charset=utf-8"));
            Assert.Equal("t", text);
            await Assert.ThrowsAsync<JsonSerializationException>(
                () => Call(assembly, "probe", _ => Respond(HttpStatusCode.OK, "<a/>", "application/xml")));
        }

        // ---- diagnostics ------------------------------------------------------------------------------

        private static string Document(string operation, string components = "")
        {
            return ("{'openapi':'3.1.0','info':{'title':'t','version':'1'},'servers':[{'url':'https://api.example.test'}]," +
                    "'paths':{'/x':{'post':{'operationId':'probe'," + operation + "}}}" +
                    (components.Length == 0 ? string.Empty : ",'components':" + components) + "}").Replace('\'', '"');
        }

        [Fact]
        public void XmlObjectBodyRemainsUnsupportedAtItsLogicalPath()
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(Document(
                "'requestBody':{'content':{'application/xml':{'schema':{'type':'object','properties':{'a':{'type':'string'}}}}}}," +
                "'responses':{'204':{'description':'ok'}}")).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/paths/~1x/post/requestBody/content/application~1xml/schema'", diagnostic.GetMessage());
        }

        [Fact]
        public async Task XmlStringBodyIsSentAsText()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document(
                "'requestBody':{'content':{'application/xml':{'schema':{'type':'string'}}}},'responses':{'204':{'description':'ok'}}"));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);

            (_, Exchange sent) = await Call(
                execution.EmitAssembly(),
                "probe",
                _ => Respond(HttpStatusCode.NoContent),
                "<a/>");

            Assert.Equal("application/xml; charset=utf-8", sent.ContentType);
            Assert.Equal("<a/>", Encoding.UTF8.GetString(sent.Body));
        }

        [Theory]
        [InlineData("'requestBody':{'content':{'multipart/form-data':{'schema':{'type':'string'}}}},'responses':{'204':{'description':'ok'}}", "must be an object")]
        [InlineData("'requestBody':{'content':{'multipart/mixed':{'schema':{'type':'object'}}}},'responses':{'204':{'description':'ok'}}", "Only multipart/form-data")]
        [InlineData("'responses':{'200':{'description':'ok','content':{'multipart/form-data':{'schema':{'type':'object'}}}}}", "request-only")]
        [InlineData("'requestBody':{'content':{'application/x-www-form-urlencoded':{'schema':{'type':'object','properties':{'f':{'type':'string','format':'binary'}}}}}},'responses':{'204':{'description':'ok'}}", "Binary schemas")]
        [InlineData("'requestBody':{'content':{'application/octet-stream':{'schema':{'type':'object'}}}},'responses':{'204':{'description':'ok'}}", "binary payload")]
        [InlineData("'requestBody':{'content':{'text/plain':{'schema':{'type':'array','items':{'type':'string'}}}}},'responses':{'204':{'description':'ok'}}", "scalar")]
        public void UnsupportedBodyShapesReportOacg101(string operation, string expected)
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(Document(operation)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains(expected, diagnostic.GetMessage());
            Assert.Contains("logical path '/paths/~1x/post/", diagnostic.GetMessage());
        }

        [Fact]
        public void BinaryInAHeaderIsRejected()
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(Document(
                "'responses':{'200':{'description':'ok','headers':{'X':{'schema':{'type':'string','format':'binary'}}}}}")).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("must use a primitive", diagnostic.GetMessage());
            Assert.Contains("/headers/X", diagnostic.GetMessage());
        }

        [Fact]
        public void ContentMediaTypeWithoutEncodingIsAFilePart()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document(
                "'requestBody':{'content':{'multipart/form-data':{'schema':{'type':'object','properties':{'f':{'type':'string','contentMediaType':'image/png'}}}}}},'responses':{'204':{'description':'ok'}}"));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("Phase4ApiFileParameter", execution.GeneratedSource);
        }

        [Fact]
        public void ResponseMediaTypeWithAnotherBodyTypeIsReportedNotDropped()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ("{'openapi':'3.1.0','info':{'title':'t','version':'1'},'paths':{'/x':{'get':{'operationId':'probe','responses':{" +
                 "'200':{'description':'ok','content':{'application/json':{'schema':{'type':'integer'}},'text/csv':{'schema':{'type':'string'}}}}}}}}}")
                .Replace('\'', '"'));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("text/csv", warning.GetMessage());
            Assert.Contains("logical path '/paths/~1x/get/responses/200/content/text~1csv'", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void EncodingOfAnUnknownPropertyIsReported()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document(
                "'requestBody':{'content':{'multipart/form-data':{'schema':{'type':'object','properties':{'a':{'type':'string'}}}," +
                "'encoding':{'ghost':{'contentType':'text/csv'}}}}},'responses':{'204':{'description':'ok'}}"));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Contains("ghost", warning.GetMessage());
        }

        [Fact]
        public void ResponsesAreRequiredBefore31AndOptionalFrom31()
        {
            Diagnostic missing = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                ("{'openapi':'3.0.3','info':{'title':'t','version':'1'},'paths':{'/x':{'get':{'operationId':'probe'}}}}")
                .Replace('\'', '"')).RunResult.Diagnostics);
            Phase4GeneratorExecution omitted = Phase4GeneratorTestHarness.GenerateAndCompile(
                ("{'openapi':'3.1.0','info':{'title':'t','version':'1'},'paths':{'/x':{'get':{'operationId':'probe'}}}}")
                .Replace('\'', '"'));

            Assert.Equal("OACG100", missing.Id);
            Assert.Empty(omitted.RunResult.Diagnostics);
            Assert.Empty(omitted.CompilationErrors);
        }

        [Fact]
        public void AdditionalMethodNamesDoNotCollideWithOperations()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ("{'openapi':'3.1.0','info':{'title':'t','version':'1'},'paths':{" +
                 "'/a':{'post':{'operationId':'send','requestBody':{'content':{'application/json':{'schema':{'type':'string'}},'text/plain':{'schema':{'type':'string'}}}},'responses':{'204':{'description':'ok'}}}}," +
                 "'/b':{'post':{'operationId':'sendAsText','responses':{'204':{'description':'ok'}}}}}}")
                .Replace('\'', '"'));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            string[] names = Phase4WireHarness.ClientType(execution.EmitAssembly())
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(static method => method.Name)
                .ToArray();
            Assert.Contains("send", names);
            Assert.Contains("sendAsText", names);
            Assert.Contains("sendAsText2", names);
        }

        private static string? ExtractParameter(string disposition, string name)
        {
            foreach (string segment in disposition.Split(';').Skip(1))
            {
                string[] pair = segment.Split(new[] { '=' }, 2);
                if (string.Equals(pair[0].Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return pair[1].Trim().Trim('"');
                }
            }

            return null;
        }
    }
}
