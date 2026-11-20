using System;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xunit;

using static Rhycol.OpenApiCodeGen.SourceGenerator.Tests.Phase4BodyWireHarness;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Issue 110: System.Net.Http re-parses headers it knows (Cache-Control, Content-Language, ...) and
    /// returns them reformatted, so a value that matches the declared enum or const on the wire must
    /// still match after .NET reorders or respaces it. A value outside the declared set still fails.
    /// </summary>
    public sealed class Phase4TypedResponseHeaderTests
    {
        private const string Spec =
            "{'openapi':'3.1.0','info':{'title':'t','version':'1'},'servers':[{'url':'https://api.example.test'}],'paths':{" +
            "'/boot':{'get':{'operationId':'getBoot','responses':{'200':{'description':'ok','headers':{" +
            "'Cache-Control':{'required':true,'schema':{'type':'string','enum':['public, max-age=0, s-maxage=30, must-revalidate','private, no-store']}}," +
            "'Content-Language':{'schema':{'type':'string','enum':['en, ja']}}}," +
            "'content':{'application/json':{'schema':{'type':'object'}}}}}}}," +
            "'/constant':{'get':{'operationId':'getConstant','responses':{'200':{'description':'ok','headers':{" +
            "'Cache-Control':{'required':true,'schema':{'type':'string','const':'public, max-age=0, s-maxage=30, must-revalidate'}}}," +
            "'content':{'application/json':{'schema':{'type':'object'}}}}}}}," +
            "'/shared':{'get':{'operationId':'getShared','responses':{'200':{'description':'ok','headers':{" +
            "'Cache-Control':{'$ref':'#/components/headers/SharedCacheControl'}}," +
            "'content':{'application/json':{'schema':{'type':'object'}}}}}}}}," +
            "'components':{'headers':{'SharedCacheControl':{'required':true,'schema':{'$ref':'#/components/schemas/CachePolicy'}}}," +
            "'schemas':{'CachePolicy':{'type':'string','enum':['public, max-age=0, s-maxage=30, must-revalidate','private, no-store']}}}}";

        private static readonly Lazy<Assembly> Client = new Lazy<Assembly>(() =>
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Spec.Replace('\'', '"'));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        });

        private static Task<(object? Result, Exchange Request)> Boot(string method, params (string Name, string Value)[] headers) =>
            Call(Client.Value, method, _ => Respond(HttpStatusCode.OK, "{}", "application/json", headers));

        [Theory]
        [InlineData("public, max-age=0, s-maxage=30, must-revalidate", "PublicMaxAge0SMaxage30MustRevalidate")]
        [InlineData("private, no-store", "PrivateNoStore")]
        [InlineData("must-revalidate,public,  max-age=0, s-maxage=30", "PublicMaxAge0SMaxage30MustRevalidate")]
        public async Task ACacheControlEnumMatchesAfterDotNetReformatsTheValue(string wire, string member)
        {
            (object? info, _) = await Boot("getBootWithHttpInfo", ("Cache-Control", wire));

            Assert.Equal(member, GetOrNull(info!, "CacheControl")!.ToString());
        }

        [Theory]
        [InlineData("no-cache")]
        [InlineData("public, max-age=0")]
        public async Task ACacheControlOutsideTheEnumStillFails(string wire)
        {
            JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(
                () => Boot("getBootWithHttpInfo", ("Cache-Control", wire)));
            Assert.Contains("Cache-Control", exception.Message);
        }

        [Fact]
        public async Task AReferencedHeaderWithAReferencedEnumIsMatchedTheSameWay()
        {
            (object? info, _) = await Boot(
                "getSharedWithHttpInfo", ("Cache-Control", "public, max-age=0, s-maxage=30, must-revalidate"));
            Assert.Equal("PublicMaxAge0SMaxage30MustRevalidate", GetOrNull(info!, "CacheControl")!.ToString());

            await Assert.ThrowsAsync<JsonSerializationException>(
                () => Boot("getSharedWithHttpInfo", ("Cache-Control", "no-cache")));
        }

        [Fact]
        public async Task AContentHeaderEnumMatchesAfterDotNetSplitsTheValue()
        {
            (object? info, _) = await Boot(
                "getBootWithHttpInfo",
                ("Cache-Control", "private, no-store"),
                ("Content-Language", "en,ja"));

            Assert.Equal("EnJa", GetOrNull(info!, "ContentLanguage")!.ToString());
        }

        [Fact]
        public async Task ACacheControlConstIsReturnedInItsDeclaredSpelling()
        {
            (object? info, _) = await Boot(
                "getConstantWithHttpInfo", ("Cache-Control", "public, max-age=0, s-maxage=30, must-revalidate"));
            Assert.Equal("public, max-age=0, s-maxage=30, must-revalidate", Get<string>(info!, "CacheControl"));
        }

        [Fact]
        public void APatternOnAReformattedHeaderIsReportedAsOacg109AndStillGenerates()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile((
                "{'openapi':'3.1.0','info':{'title':'t','version':'1'},'servers':[{'url':'https://api.example.test'}],'paths':{" +
                "'/p':{'get':{'operationId':'getP','responses':{'200':{'description':'ok','headers':{" +
                "'cache-control':{'schema':{'type':'string','pattern':'^public'}}," +
                "'X-Custom':{'schema':{'type':'string','pattern':'^a'}}}}}}}}}").Replace('\'', '"'));

            Microsoft.CodeAnalysis.Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG109", warning.Id);
            Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("'cache-control'", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public async Task APatternOnAReformattedHeaderIsMatchedAgainstTheReformattedValue()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile((
                "{'openapi':'3.1.0','info':{'title':'t','version':'1'},'servers':[{'url':'https://api.example.test'}],'paths':{" +
                "'/p':{'get':{'operationId':'getP','responses':{'200':{'description':'ok','headers':{" +
                "'Cache-Control':{'schema':{'type':'string','pattern':'^public, must-revalidate'}}}}}}}}}").Replace('\'', '"'));
            Assembly assembly = execution.EmitAssembly();

            // System.Net.Http returns "public, must-revalidate, max-age=0".
            (object? info, _) = await Call(
                assembly, "getPWithHttpInfo", _ => Respond(HttpStatusCode.OK, null, "application/json", ("Cache-Control", "max-age=0, public, must-revalidate")));
            Assert.Equal("public, must-revalidate, max-age=0", Get<string>(info!, "CacheControl"));
        }
    }
}
