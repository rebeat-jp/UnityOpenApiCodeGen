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

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P3 (G16-G19): parameter serialization. The OpenAPI "Style Examples" table (color = blue,
    /// black, brown / R=100,G=200,B=150 / empty / single value) is checked against the bytes the
    /// generated client puts on the wire, through a fake HttpMessageHandler.
    /// </summary>
    public sealed class Phase4ParameterSerializationTests
    {
        private const string Ns = Phase4WireHarness.Ns;

        // ---- the Style Examples table ---------------------------------------------------------------

        // location, style, explode, shape, expected. The expected strings are the table cells:
        // path: what follows the literal "/v"; query: the query string; header: the header value;
        // cookie: the Cookie header value.
        private static readonly object[][] StyleRows =
        {
            // matrix
            new object[] { "path", "matrix", false, "empty", ";color" },
            new object[] { "path", "matrix", false, "string", ";color=blue" },
            new object[] { "path", "matrix", false, "array", ";color=blue,black,brown" },
            new object[] { "path", "matrix", false, "object", ";color=R,100,G,200,B,150" },
            new object[] { "path", "matrix", true, "empty", ";color" },
            new object[] { "path", "matrix", true, "string", ";color=blue" },
            new object[] { "path", "matrix", true, "array", ";color=blue;color=black;color=brown" },
            new object[] { "path", "matrix", true, "object", ";R=100;G=200;B=150" },
            // label
            new object[] { "path", "label", false, "empty", "." },
            new object[] { "path", "label", false, "string", ".blue" },
            new object[] { "path", "label", false, "array", ".blue.black.brown" },
            new object[] { "path", "label", false, "object", ".R.100.G.200.B.150" },
            new object[] { "path", "label", true, "empty", "." },
            new object[] { "path", "label", true, "string", ".blue" },
            new object[] { "path", "label", true, "array", ".blue.black.brown" },
            new object[] { "path", "label", true, "object", ".R=100.G=200.B=150" },
            // simple (path)
            new object[] { "path", "simple", false, "empty", "" },
            new object[] { "path", "simple", false, "string", "blue" },
            new object[] { "path", "simple", false, "array", "blue,black,brown" },
            new object[] { "path", "simple", false, "object", "R,100,G,200,B,150" },
            new object[] { "path", "simple", true, "string", "blue" },
            new object[] { "path", "simple", true, "array", "blue,black,brown" },
            new object[] { "path", "simple", true, "object", "R=100,G=200,B=150" },
            // form (query)
            new object[] { "query", "form", false, "empty", "color=" },
            new object[] { "query", "form", false, "string", "color=blue" },
            new object[] { "query", "form", false, "array", "color=blue,black,brown" },
            new object[] { "query", "form", false, "object", "color=R,100,G,200,B,150" },
            new object[] { "query", "form", true, "empty", "color=" },
            new object[] { "query", "form", true, "string", "color=blue" },
            new object[] { "query", "form", true, "array", "color=blue&color=black&color=brown" },
            new object[] { "query", "form", true, "object", "R=100&G=200&B=150" },
            // spaceDelimited / pipeDelimited / deepObject (query)
            new object[] { "query", "spaceDelimited", false, "array", "color=blue%20black%20brown" },
            new object[] { "query", "spaceDelimited", false, "object", "color=R%20100%20G%20200%20B%20150" },
            new object[] { "query", "pipeDelimited", false, "array", "color=blue|black|brown" },
            new object[] { "query", "pipeDelimited", false, "object", "color=R|100|G|200|B|150" },
            new object[] { "query", "deepObject", true, "object", "color[R]=100&color[G]=200&color[B]=150" },
            // simple (header)
            new object[] { "header", "simple", false, "string", "blue" },
            new object[] { "header", "simple", false, "array", "blue,black,brown" },
            new object[] { "header", "simple", false, "object", "R,100,G,200,B,150" },
            new object[] { "header", "simple", true, "array", "blue,black,brown" },
            new object[] { "header", "simple", true, "object", "R=100,G=200,B=150" },
            // form (cookie)
            new object[] { "cookie", "form", false, "empty", "color=" },
            new object[] { "cookie", "form", false, "string", "color=blue" },
            new object[] { "cookie", "form", false, "array", "color=blue,black,brown" },
            new object[] { "cookie", "form", false, "object", "color=R,100,G,200,B,150" },
            new object[] { "cookie", "form", true, "array", "color=blue; color=black; color=brown" },
            new object[] { "cookie", "form", true, "object", "R=100; G=200; B=150" },
        };

        private static readonly Lazy<Assembly> StyleAssembly = new Lazy<Assembly>(CreateStyleAssembly);

        public static IEnumerable<object[]> StyleTable()
        {
            return StyleRows;
        }

        [Theory]
        [MemberData(nameof(StyleTable))]
        public async Task StyleExamplesTableMatchesTheWireFormat(
            string location,
            string style,
            bool explode,
            string shape,
            string expected)
        {
            object value = shape switch
            {
                "empty" => string.Empty,
                "string" => "blue",
                "array" => new List<string> { "blue", "black", "brown" },
                _ => new Dictionary<string, int> { ["R"] = 100, ["G"] = 200, ["B"] = 150 }
            };

            Phase4WireHarness.WireRequest sent = await Send(StyleAssembly.Value, OperationId(location, style, explode, shape), value);

            switch (location)
            {
                case "path":
                    Assert.Equal("/v1/" + OperationId(location, style, explode, shape) + "/v" + expected, sent.Uri.AbsolutePath);
                    Assert.Equal(string.Empty, sent.Uri.Query);
                    break;
                case "query":
                    // System.Uri writes the pipe delimiter as %7C (it is not a legal URI character), which is
                    // what HttpClient puts on the wire.
                    Assert.Equal("?" + expected.Replace("|", "%7C"), sent.Uri.Query);
                    break;
                case "header":
                    Assert.Equal(expected, sent.Headers["color"]);
                    break;
                default:
                    Assert.Equal(expected, sent.Headers["Cookie"]);
                    break;
            }
        }

        [Fact]
        public void StyleTableSpecGeneratesWithoutDiagnostics()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(CreateStyleSpec());

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        private static string OperationId(string location, string style, bool explode, string shape)
        {
            return location + "_" + style + "_" + (explode ? "x" : "n") + "_" + shape;
        }

        private static string CreateStyleSpec()
        {
            var paths = new StringBuilder();
            foreach (object[] row in StyleRows)
            {
                string location = (string)row[0];
                string style = (string)row[1];
                bool explode = (bool)row[2];
                string shape = (string)row[3];
                string schema = shape switch
                {
                    "array" => "{'type':'array','items':{'type':'string'}}",
                    "object" => "{'type':'object','additionalProperties':{'type':'integer'}}",
                    _ => "{'type':'string'}"
                };
                string path = location switch
                {
                    "path" => "/v{color}",
                    "query" => "/q",
                    "header" => "/h",
                    _ => "/c"
                };
                string id = OperationId(location, style, explode, shape);
                // One path per operation: the path itself is made unique with a literal suffix.
                string uniquePath = "/" + id + path;
                if (paths.Length > 0)
                {
                    paths.Append(',');
                }

                paths.Append("'").Append(uniquePath).Append("':{'get':{'operationId':'").Append(id)
                    .Append("','parameters':[{'name':'color','in':'").Append(location)
                    .Append("','required':true,'style':'").Append(style)
                    .Append("','explode':").Append(explode ? "true" : "false")
                    .Append(",'schema':").Append(schema)
                    .Append("}],'responses':{'204':{'description':'ok'}}}}");
            }

            return ("{'openapi':'3.1.0','info':{'title':'t','version':'1'}," +
                    "'servers':[{'url':'https://api.example.test/v1'}],'paths':{" + paths + "}}")
                .Replace('\'', '"');
        }

        private static Assembly CreateStyleAssembly()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(CreateStyleSpec());
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        // ---- cookies --------------------------------------------------------------------------------

        private const string CookieSpec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://api.example.test'}],
  'paths':{
    '/cookies':{'get':{'operationId':'sendCookies','parameters':[
      {'name':'session','in':'cookie','required':true,'schema':{'type':'string'}},
      {'name':'theme','in':'cookie','schema':{'type':'string'}},
      {'name':'ids','in':'cookie','schema':{'type':'array','items':{'type':'integer'}}},
      {'name':'X-Trace','in':'header','schema':{'type':'string'}}
    ],'responses':{'204':{'description':'ok'}}}}
  }
}";

        [Fact]
        public async Task CookieParametersAreSentInOneEncodedCookieHeader()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CookieSpec.Replace('\'', '"'));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();

            // Arguments follow the sorted parameter identities: cookie:ids, cookie:session, cookie:theme, header:X-TRACE.
            Phase4WireHarness.WireRequest sent = await SendAll(
                assembly,
                "sendCookies",
                new List<int> { 1, 2 },
                "a b;c,d=é",
                "dark",
                "t-1");

            // Values are percent-encoded to RFC 6265 cookie-octets; the cookies are joined with "; ".
            Assert.Equal("ids=1; ids=2; session=a%20b%3Bc%2Cd%3D%C3%A9; theme=dark", sent.Headers["Cookie"]);
            Assert.Equal("t-1", sent.Headers["X-Trace"]);
            Assert.Single(sent.RawCookieHeaders);
        }

        [Fact]
        public async Task OptionalCookieParametersAreOmittedAndNoHeaderIsSentWithoutCookies()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CookieSpec.Replace('\'', '"'));
            Assembly assembly = execution.EmitAssembly();

            Phase4WireHarness.WireRequest sent = await SendAll(assembly, "sendCookies", null, "only", null, null);

            Assert.Equal("session=only", sent.Headers["Cookie"]);
            Assert.False(sent.Headers.ContainsKey("X-Trace"));
        }

        [Fact]
        public void CookieAndCookieNamedHeaderParametersTogetherAreRejected()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{'/x':{'get':{'operationId':'x','parameters':[
    {'name':'a','in':'cookie','schema':{'type':'string'}},
    {'name':'Cookie','in':'header','schema':{'type':'string'}}
  ],'responses':{'204':{'description':'ok'}}}}}
}".Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
        }

        // ---- allowReserved --------------------------------------------------------------------------

        private const string ReservedSpec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://api.example.test'}],
  'paths':{
    '/search':{'get':{'operationId':'search','parameters':[
      {'name':'raw','in':'query','allowReserved':true,'schema':{'type':'string'}},
      {'name':'strict','in':'query','schema':{'type':'string'}},
      {'name':'many','in':'query','allowReserved':true,'explode':false,'schema':{'type':'array','items':{'type':'string'}}}
    ],'responses':{'204':{'description':'ok'}}}}
  }
}";

        [Fact]
        public async Task AllowReservedKeepsReservedCharactersAndEncodesTheRest()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReservedSpec.Replace('\'', '"'));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();

            // Arguments: many, raw, strict (sorted by identity).
            Phase4WireHarness.WireRequest sent = await SendAll(
                assembly,
                "search",
                new List<string> { "x/y", "z:w" },
                "a/b:c@d,e f#g%2Fh",
                "a/b:c");

            Assert.Equal(
                "?many=x/y,z:w&raw=a/b:c@d,e%20f%23g%2Fh&strict=a%2Fb%3Ac",
                sent.Uri.Query);
        }

        [Fact]
        public async Task AllowReservedEncodesLoneAndIncompletePercentSignsAndNonAscii()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReservedSpec.Replace('\'', '"'));
            Assembly assembly = execution.EmitAssembly();

            // Arguments: many, raw, strict. A '%' that does not start a %XX triplet is data and is
            // encoded; non-ASCII is UTF-8 percent-encoded both with and without allowReserved.
            Phase4WireHarness.WireRequest sent = await SendAll(
                assembly,
                "search",
                new List<string> { "100%" },
                "a%b%X%4%2Fé",
                "é");

            Assert.Equal(
                "?many=100%25&raw=a%25b%25X%254%2F%C3%A9&strict=%C3%A9",
                sent.Uri.Query);
        }

        [Fact]
        public async Task AdditionalPropertiesOfAnObjectParameterFollowTheDeclaredProperties()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://api.example.test'}],
  'paths':{'/find':{'get':{'operationId':'find','parameters':[
    {'name':'color','in':'query','explode':false,'schema':{'$ref':'#/components/schemas/Rgb'}}
  ],'responses':{'204':{'description':'ok'}}}}},
  'components':{'schemas':{'Rgb':{'type':'object','additionalProperties':{'type':'integer'},
    'properties':{'R':{'type':'integer'},'A':{'type':'integer'}}}}}
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            Type rgb = assembly.GetType(Ns + "Rgb", throwOnError: true)!;
            object color = Activator.CreateInstance(rgb)!;
            rgb.GetProperty("R")!.SetValue(color, 1);
            rgb.GetProperty("A")!.SetValue(color, 9);
            var extras = (IDictionary<string, Newtonsoft.Json.Linq.JToken>)rgb.GetProperty("AdditionalProperties")!.GetValue(color)!;
            extras["z"] = 26;
            extras["b"] = 2;
            extras["n"] = Newtonsoft.Json.Linq.JValue.CreateNull();

            Phase4WireHarness.WireRequest sent = await SendAll(assembly, "find", color);

            // Declared members by name (A, R), then the additional ones by key (b, z); null is skipped.
            Assert.Equal("?color=A,9,R,1,b,2,z,26", sent.Uri.Query);
        }

        // ---- content parameters ---------------------------------------------------------------------

        private const string ContentSpec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://api.example.test'}],
  'paths':{
    '/items/{key}':{'get':{'operationId':'getItem','parameters':[
      {'name':'key','in':'path','required':true,'content':{'application/json':{'schema':{'type':'array','items':{'type':'string'}}}}},
      {'name':'filter','in':'query','content':{'application/json':{'schema':{'type':'object','additionalProperties':false,
        'properties':{'a':{'type':'integer'},'b':{'type':'string'}}}}}},
      {'name':'X-Opts','in':'header','content':{'application/json':{'schema':{'type':'object','additionalProperties':{'type':'integer'}}}}}
    ],'responses':{'204':{'description':'ok'}}}}
  }
}";

        [Fact]
        public async Task ContentParametersAreSerializedAsJsonThenPercentEncoded()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ContentSpec.Replace('\'', '"'));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            Type filterType = assembly.GetType(Ns + "FilterParameter", throwOnError: true)!;
            object filter = Activator.CreateInstance(filterType)!;
            filterType.GetProperty("A")!.SetValue(filter, 1);
            filterType.GetProperty("B")!.SetValue(filter, "x y");

            // Arguments sorted by identity: X-Opts (header), key (path), filter (query).
            Phase4WireHarness.WireRequest sent = await SendAll(
                assembly,
                "getItem",
                new Dictionary<string, int> { ["k"] = 2 },
                new List<string> { "a/b", "c" },
                filter);

            Assert.Equal("/items/%5B%22a%2Fb%22%2C%22c%22%5D", sent.Uri.AbsolutePath);
            Assert.Equal("?filter=%7B%22a%22%3A1%2C%22b%22%3A%22x%20y%22%7D", sent.Uri.Query);
            Assert.Equal("{\"k\":2}", sent.Headers["X-Opts"]);
        }

        // ---- object (DTO) parameters ----------------------------------------------------------------

        [Fact]
        public async Task DtoParameterIsSerializedFromItsDeclaredPropertiesAndSkipsNulls()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://api.example.test'}],
  'paths':{'/find':{'get':{'operationId':'find','parameters':[
    {'name':'color','in':'query','style':'deepObject','explode':true,'schema':{'$ref':'#/components/schemas/Rgb'}}
  ],'responses':{'204':{'description':'ok'}}}}},
  'components':{'schemas':{'Rgb':{'type':'object','additionalProperties':false,
    'properties':{'R':{'type':'integer'},'G':{'type':'integer'},'B':{'type':'string'}}}}}
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            Type rgb = assembly.GetType(Ns + "Rgb", throwOnError: true)!;
            object color = Activator.CreateInstance(rgb)!;
            rgb.GetProperty("R")!.SetValue(color, 100);
            rgb.GetProperty("G")!.SetValue(color, 200);

            Phase4WireHarness.WireRequest sent = await SendAll(assembly, "find", color);

            Assert.Equal("?color[G]=200&color[R]=100", sent.Uri.Query);
        }

        [Fact]
        public async Task ArrayWithNullElementIsRejectedBeforeSending()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateStyleSpec());
            Assembly assembly = StyleAssembly.Value;

            Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => Send(
                assembly,
                OperationId("query", "form", true, "array"),
                new List<string?> { "a", null }.Select(static v => v!).ToList()));

            Assert.IsType<ArgumentException>(exception);
            Assert.Empty(execution.RunResult.Diagnostics);
        }

        // ---- header parameters that OpenAPI says to ignore (G19) -------------------------------------

        [Fact]
        public async Task AcceptContentTypeAndAuthorizationHeaderParametersAreIgnoredWithWarnings()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://api.example.test'}],
  'paths':{'/h':{'get':{'operationId':'headers','parameters':[
    {'name':'Accept','in':'header','schema':{'type':'string'}},
    {'name':'content-type','in':'header','schema':{'type':'string'}},
    {'name':'AUTHORIZATION','in':'header','required':true,'schema':{'type':'string'}},
    {'name':'X-Keep','in':'header','schema':{'type':'string'}}
  ],'responses':{'204':{'description':'ok'}}}}}
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);

            Diagnostic[] warnings = execution.RunResult.Diagnostics.ToArray();
            Assert.Equal(3, warnings.Length);
            Assert.All(warnings, static warning =>
            {
                Assert.Equal("OACG108", warning.Id);
                Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
            });
            Assert.Contains(warnings, w => w.GetMessage().Contains("logical path '/paths/~1h/get/parameters/0/name'"));
            Assert.Contains(warnings, w => w.GetMessage().Contains("logical path '/paths/~1h/get/parameters/1/name'"));
            Assert.Contains(warnings, w => w.GetMessage().Contains("logical path '/paths/~1h/get/parameters/2/name'"));
            Assert.Empty(execution.CompilationErrors);

            Assembly assembly = execution.EmitAssembly();
            MethodInfo method = assembly.GetType(Ns + "Phase4Api")!.GetMethod("headers")!;
            // Only X-Keep and the cancellation token remain.
            Assert.Equal(new[] { "XKeep", "cancellationToken" }, method.GetParameters().Select(p => p.Name).ToArray());
            Phase4WireHarness.WireRequest sent = await SendAll(assembly, "headers", "kept");
            Assert.Equal("kept", sent.Headers["X-Keep"]);
            Assert.False(sent.Headers.ContainsKey("Authorization"));
            Assert.False(sent.Headers.ContainsKey("Accept"));
        }

        // ---- nested / undefined values stay OACG101 ---------------------------------------------------

        [Theory]
        [InlineData("{'type':'array','items':{'type':'array','items':{'type':'string'}}}", "query", "form", "true")]
        [InlineData("{'type':'object','additionalProperties':false,'properties':{'a':{'type':'object','additionalProperties':false,'properties':{'b':{'type':'string'}}}}}", "query", "deepObject", "true")]
        [InlineData("{'type':'object'}", "query", "form", "true")]
        [InlineData("{'type':'array','items':{'type':'string'}}", "query", "deepObject", "true")]
        [InlineData("{'type':'string'}", "query", "pipeDelimited", "false")]
        [InlineData("{'type':'array','items':{'type':'string'}}", "query", "pipeDelimited", "true")]
        [InlineData("{'type':'object','additionalProperties':{'type':'string'}}", "query", "deepObject", "false")]
        public void UndefinedOrNestedParameterSerializationIsReportedAsUnsupported(
            string schema,
            string location,
            string style,
            string explode)
        {
            string spec = (@"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{'/x':{'get':{'operationId':'x','parameters':[
    {'name':'p','in':'" + location + "','style':'" + style + "','explode':" + explode + ",'schema':" + schema + @"}
  ],'responses':{'204':{'description':'ok'}}}}}
}").Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/paths/~1x/get/parameters/0'", diagnostic.GetMessage());
        }

        [Fact]
        public void StyleThatIsInvalidForTheLocationIsAnInvalidDocument()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{'/x':{'get':{'operationId':'x','parameters':[
    {'name':'p','in':'header','style':'form','schema':{'type':'string'}}
  ],'responses':{'204':{'description':'ok'}}}}}
}".Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
        }

        // ---- deprecated parameters and documentation --------------------------------------------------

        [Fact]
        public void DeprecatedOperationIsObsoleteAndDocumentationCarriesDescriptions()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{'/x':{'get':{'operationId':'oldCall','summary':'Old call','description':'Use newCall.\nSecond line.','deprecated':true,
    'parameters':[{'name':'p','in':'query','deprecated':true,'description':'The p & q','schema':{'type':'string'}}],
    'responses':{'204':{'description':'ok'}}}}}
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            string source = execution.GeneratedSource;
            Assert.Contains("[global::System.Obsolete]", source);
            Assert.Contains("#pragma warning disable CS0612, CS0618", source);
            Assert.Contains("/// <summary>Old call</summary>", source);
            Assert.Contains("/// <remarks>", source);
            Assert.Contains("/// Use newCall.", source);
            Assert.Contains("/// Second line.", source);
            Assert.Contains("(deprecated): The p &amp; q</param>", source);
            MethodInfo method = execution.EmitAssembly().GetType(Ns + "Phase4Api")!.GetMethod("oldCall")!;
            Assert.NotNull(method.GetCustomAttribute<ObsoleteAttribute>());
        }

        // ---- harness ---------------------------------------------------------------------------------

        private static Task<Phase4WireHarness.WireRequest> Send(Assembly assembly, string methodName, params object?[] arguments)
        {
            return Phase4WireHarness.Send(assembly, methodName, arguments);
        }

        private static Task<Phase4WireHarness.WireRequest> SendAll(Assembly assembly, string methodName, params object?[] arguments)
        {
            return Phase4WireHarness.Send(assembly, methodName, arguments);
        }
    }
}
