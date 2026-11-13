using System;
using System.Collections.Generic;
using System.IO;
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
    /// P5 (G28): security schemes and requirements, checked on the wire against a fake
    /// HttpMessageHandler. The client is compiled from ApiSurfaceSpecs/security.json.
    /// </summary>
    public sealed class Phase5SecurityTests
    {
        private static readonly Lazy<Assembly> SecurityAssembly = new Lazy<Assembly>(() =>
        {
            string spec = File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", "security.json"));
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);
            if (execution.RunResult.Diagnostics.Length > 0 || execution.CompilationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "security.json: " +
                    string.Join("; ", execution.RunResult.Diagnostics.Select(static value => value.ToString())) +
                    string.Join("; ", execution.CompilationErrors.Select(static value => value.ToString())));
            }

            return execution.EmitAssembly();
        });

        private sealed class Sent
        {
            internal Uri Uri { get; set; } = null!;

            internal Dictionary<string, string> Headers { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            internal List<string> CookieHeaders { get; } = new List<string>();
        }

        private sealed class Handler : HttpMessageHandler
        {
            internal Sent? Last { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var sent = new Sent { Uri = request.RequestUri! };
                foreach (var header in request.Headers)
                {
                    sent.Headers[header.Key] = string.Join(",", header.Value);
                }

                if (request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookies))
                {
                    sent.CookieHeaders.AddRange(cookies);
                }

                Last = sent;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        }

        private static async Task<Sent> Call(
            string method,
            Dictionary<string, object?>? properties = null,
            CancellationToken token = default,
            params object?[] arguments)
        {
            Assembly assembly = SecurityAssembly.Value;
            var handler = new Handler();
            using var httpClient = new HttpClient(handler);
            object client = Phase4WireHarness.CreateClient(assembly, httpClient, "https://api.example.com");
            Type type = Phase4WireHarness.ClientType(assembly);
            if (properties is not null)
            {
                foreach (KeyValuePair<string, object?> property in properties)
                {
                    type.GetProperty(property.Key)!.SetValue(client, property.Value);
                }
            }

            MethodInfo info = type.GetMethod(method)!;
            ParameterInfo[] parameters = info.GetParameters();
            var full = new object?[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                full[index] = index < arguments.Length
                    ? arguments[index]
                    : parameters[index].ParameterType == typeof(CancellationToken)
                        ? token
                        : parameters[index].HasDefaultValue ? parameters[index].DefaultValue : null;
            }

            try
            {
                await ((Task)info.Invoke(client, full)!).ConfigureAwait(false);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }

            return handler.Last!;
        }

        private static Dictionary<string, object?> With(params (string Name, object? Value)[] values)
        {
            return values.ToDictionary(static value => value.Name, static value => value.Value);
        }

        private static string Basic(string text) =>
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        // ---- the fixture -----------------------------------------------------------------------------

        [Fact]
        public void SecuritySpecGeneratesWithoutDiagnosticsOrCompileErrors()
        {
            Assert.NotNull(SecurityAssembly.Value);
        }

        [Fact]
        public void CredentialPropertiesAreGeneratedPerScheme()
        {
            Type api = Phase4WireHarness.ClientType(SecurityAssembly.Value);
            string[] expected =
            {
                "HeaderKeyApiKey", "QueryKeyApiKey", "CookieKeyApiKey", "BearerAuthAccessToken",
                "BearerAuthAccessTokenProvider", "BasicAuthUsername", "BasicAuthPassword",
                "DigestAuthCredentials", "OauthAccessToken", "OauthAccessTokenProvider", "OidcAccessToken",
                "OidcAccessTokenProvider"
            };
            foreach (string name in expected)
            {
                Assert.NotNull(api.GetProperty(name));
            }

            Assert.Null(api.GetProperty("MtlsApiKey"));
            Assert.Equal(
                typeof(Func<CancellationToken, Task<string>>),
                api.GetProperty("BearerAuthAccessTokenProvider")!.PropertyType);
        }

        // ---- apiKey ----------------------------------------------------------------------------------

        [Fact]
        public async Task ApiKeyInHeaderIsSent()
        {
            Sent sent = await Call("getHeaderKey", With(("HeaderKeyApiKey", "k-123")));

            Assert.Equal("k-123", sent.Headers["X-Api-Key"]);
        }

        [Fact]
        public async Task ApiKeyInQueryIsEncodedAndMergedWithQueryParameters()
        {
            Sent sent = await Call("getQueryKey", With(("QueryKeyApiKey", "a b&c")), default, 5);

            Assert.Equal("?limit=5&api%20key=a%20b%26c", sent.Uri.Query);
        }

        [Fact]
        public async Task ApiKeyInQueryAloneStartsTheQueryString()
        {
            Sent sent = await Call("getQueryKey", With(("QueryKeyApiKey", "k")));

            Assert.Equal("?api%20key=k", sent.Uri.Query);
        }

        [Fact]
        public async Task ApiKeyInCookieSharesOneCookieHeaderWithCookieParameters()
        {
            Sent sent = await Call("getCookieKey", With(("CookieKeyApiKey", "s1")), default, "dark");

            string header = Assert.Single(sent.CookieHeaders);
            Assert.Equal("theme=dark; sid=s1", header);
        }

        [Fact]
        public async Task ApiKeyInCookieAloneCreatesOneCookieHeader()
        {
            Sent sent = await Call("getCookieKey", With(("CookieKeyApiKey", "s1")));

            Assert.Equal("sid=s1", Assert.Single(sent.CookieHeaders));
        }

        [Fact]
        public async Task CookieParameterAloneIsStillSentWhenTheApiKeyIsNotConfigured()
        {
            Sent sent = await Call("getCookieKey", null, default, "dark");

            Assert.Equal("theme=dark", Assert.Single(sent.CookieHeaders));
        }

        [Fact]
        public async Task ApiKeysOfAnAndRequirementUseHeaderQueryAndCookieTogether()
        {
            Sent sent = await Call(
                "getTwoHeaders",
                With(("HeaderKeyApiKey", "h"), ("QueryKeyApiKey", "q"), ("CookieKeyApiKey", "c")));

            Assert.Equal("h", sent.Headers["X-Api-Key"]);
            Assert.Equal("?api%20key=q", sent.Uri.Query);
            Assert.Equal("sid=c", Assert.Single(sent.CookieHeaders));
        }

        [Fact]
        public async Task SecurityApiKeyHeaderReplacesTheSameHeaderParameterWhenConfigured()
        {
            Sent configured = await Call("getOverride", With(("HeaderKeyApiKey", "from-security")), default, "from-parameter");
            Sent unconfigured = await Call("getOverride", null, default, "from-parameter");

            Assert.Equal("from-security", configured.Headers["X-Api-Key"]);
            Assert.Equal("from-parameter", unconfigured.Headers["X-Api-Key"]);
        }

        // ---- http bearer / oauth2 / openIdConnect ----------------------------------------------------

        [Fact]
        public async Task BearerPropertyIsSent()
        {
            Sent sent = await Call("getBearer", With(("BearerAuthAccessToken", "tok")));

            Assert.Equal("Bearer tok", sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task BearerProviderWinsOverTheProperty()
        {
            Sent sent = await Call("getBearer", With(
                ("BearerAuthAccessToken", "static"),
                ("BearerAuthAccessTokenProvider", new Func<CancellationToken, Task<string?>>(
                    static _ => Task.FromResult<string?>("dynamic")))));

            Assert.Equal("Bearer dynamic", sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task BearerProviderIsCalledForEverySendAndReceivesTheCancellationToken()
        {
            using var source = new CancellationTokenSource();
            var tokens = new List<CancellationToken>();
            int calls = 0;
            var provider = new Func<CancellationToken, Task<string?>>(token =>
            {
                tokens.Add(token);
                return Task.FromResult<string?>("t" + Interlocked.Increment(ref calls));
            });

            Sent first = await Call("getBearer", With(("BearerAuthAccessTokenProvider", provider)), source.Token);
            Sent second = await Call("getBearer", With(("BearerAuthAccessTokenProvider", provider)), source.Token);

            Assert.Equal("Bearer t1", first.Headers["Authorization"]);
            Assert.Equal("Bearer t2", second.Headers["Authorization"]);
            Assert.All(tokens, token => Assert.Equal(source.Token, token));
        }

        [Fact]
        public async Task BearerProviderReturningNullFallsBackToTheProperty()
        {
            Sent sent = await Call("getBearer", With(
                ("BearerAuthAccessToken", "static"),
                ("BearerAuthAccessTokenProvider", new Func<CancellationToken, Task<string?>>(
                    static _ => Task.FromResult<string?>(null)))));

            Assert.Equal("Bearer static", sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task OAuth2TokenIsSentAsBearer()
        {
            Sent sent = await Call("getOauth", With(("OauthAccessToken", "oauth-token")));

            Assert.Equal("Bearer oauth-token", sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task OpenIdConnectTokenIsSentAsBearer()
        {
            Sent sent = await Call("getOidc", With(("OidcAccessTokenProvider", new Func<CancellationToken, Task<string?>>(
                static _ => Task.FromResult<string?>("oidc-token")))));

            Assert.Equal("Bearer oidc-token", sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task ABearerTokenWithNonAsciiCharactersFailsWithoutLeakingTheToken()
        {
            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                Call("getBearer", With(("BearerAuthAccessToken", "secret-é"))));

            Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Authorization", exception.Message, StringComparison.Ordinal);
        }

        // ---- http basic / other schemes --------------------------------------------------------------

        [Fact]
        public async Task BasicCredentialsAreUtf8Base64Encoded()
        {
            Sent sent = await Call("getBasic", With(("BasicAuthUsername", "user"), ("BasicAuthPassword", "pass")));

            Assert.Equal("Basic dXNlcjpwYXNz", sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task BasicCredentialsWithNonAsciiCharactersUseUtf8()
        {
            Sent sent = await Call(
                "getBasic",
                With(("BasicAuthUsername", "ユーザー"), ("BasicAuthPassword", "pässあ")));

            Assert.Equal(Basic("ユーザー:pässあ"), sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task BasicWithoutPasswordSendsAnEmptyPassword()
        {
            Sent sent = await Call("getBasic", With(("BasicAuthUsername", "user")));

            Assert.Equal(Basic("user:"), sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task BasicUserNameWithAColonIsRejectedWithoutLeakingCredentials()
        {
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Call("getBasic", With(("BasicAuthUsername", "us:er"), ("BasicAuthPassword", "hunter2"))));

            Assert.DoesNotContain("hunter2", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("us:er", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task OtherHttpSchemesSendTheSchemeNameAndTheCredentials()
        {
            Sent sent = await Call("getDigest", With(("DigestAuthCredentials", "username=\"a\", realm=\"r\"")));

            Assert.Equal("Digest username=\"a\", realm=\"r\"", sent.Headers["Authorization"]);
        }

        // ---- requirement evaluation ------------------------------------------------------------------

        [Fact]
        public async Task OrRequirementsUseTheFirstSatisfiableOne()
        {
            Sent both = await Call("getOr", With(("HeaderKeyApiKey", "k"), ("BearerAuthAccessToken", "t")));
            Sent second = await Call("getOr", With(("BearerAuthAccessToken", "t")));

            Assert.Equal("k", both.Headers["X-Api-Key"]);
            Assert.False(both.Headers.ContainsKey("Authorization"));
            Assert.False(second.Headers.ContainsKey("X-Api-Key"));
            Assert.Equal("Bearer t", second.Headers["Authorization"]);
        }

        [Fact]
        public async Task AndRequirementSendsEverySchemeWhenAllAreConfigured()
        {
            Sent sent = await Call("getAnd", With(("HeaderKeyApiKey", "k"), ("BearerAuthAccessToken", "t")));

            Assert.Equal("k", sent.Headers["X-Api-Key"]);
            Assert.Equal("Bearer t", sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task PartiallyConfiguredAndRequirementMovesOnToTheNextRequirement()
        {
            Sent sent = await Call("getAnd", With(
                ("HeaderKeyApiKey", "k"),
                ("BasicAuthUsername", "u"),
                ("BasicAuthPassword", "p")));

            Assert.False(sent.Headers.ContainsKey("X-Api-Key"));
            Assert.Equal(Basic("u:p"), sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task PartiallyConfiguredAndRequirementWithNothingElseSendsNoAuthentication()
        {
            Sent sent = await Call("getAnd", With(("HeaderKeyApiKey", "k")));

            Assert.False(sent.Headers.ContainsKey("X-Api-Key"));
            Assert.False(sent.Headers.ContainsKey("Authorization"));
        }

        [Fact]
        public async Task OperationSecurityOverridesTheDocumentSecurity()
        {
            Sent sent = await Call("getHeaderKey", With(("HeaderKeyApiKey", "k"), ("BearerAuthAccessToken", "t")));

            Assert.Equal("k", sent.Headers["X-Api-Key"]);
            Assert.False(sent.Headers.ContainsKey("Authorization"));
        }

        [Fact]
        public async Task OperationWithoutSecurityInheritsTheDocumentSecurity()
        {
            Sent sent = await Call("getInherited", With(("BearerAuthAccessToken", "t"), ("HeaderKeyApiKey", "k")));

            Assert.Equal("Bearer t", sent.Headers["Authorization"]);
            Assert.False(sent.Headers.ContainsKey("X-Api-Key"));
        }

        [Fact]
        public async Task EmptySecurityArrayDisablesAuthentication()
        {
            Sent sent = await Call("getNone", With(("BearerAuthAccessToken", "t"), ("HeaderKeyApiKey", "k")));

            Assert.False(sent.Headers.ContainsKey("Authorization"));
            Assert.False(sent.Headers.ContainsKey("X-Api-Key"));
        }

        [Fact]
        public async Task AnonymousRequirementDoesNotPreventAConfiguredCredentialFromBeingSent()
        {
            Sent withKey = await Call("getAnonymous", With(("HeaderKeyApiKey", "k")));
            Sent withoutKey = await Call("getAnonymous");

            Assert.Equal("k", withKey.Headers["X-Api-Key"]);
            Assert.False(withoutKey.Headers.ContainsKey("X-Api-Key"));
        }

        [Fact]
        public async Task UnconfiguredCredentialsSendNoAuthentication()
        {
            Sent sent = await Call("getBearer");

            Assert.False(sent.Headers.ContainsKey("Authorization"));
        }

        [Fact]
        public async Task EmptyCredentialCountsAsNotConfigured()
        {
            Sent sent = await Call("getOr", With(("HeaderKeyApiKey", ""), ("BearerAuthAccessToken", "t")));

            Assert.Equal("Bearer t", sent.Headers["Authorization"]);
        }

        [Fact]
        public async Task MutualTlsAddsNothingToTheRequest()
        {
            Sent sent = await Call("getMtls", With(("BearerAuthAccessToken", "t")));

            Assert.False(sent.Headers.ContainsKey("Authorization"));
        }

        // ---- scheme and requirement diagnostics ------------------------------------------------------

        private static (string Id, string Message) Diagnose(string schemes, string security = "", string operationSecurity = "", string version = "3.1.0")
        {
            string document = "{\"openapi\":\"" + version + "\",\"info\":{\"title\":\"T\",\"version\":\"1\"}," +
                              (security.Length > 0 ? "\"security\":" + security + "," : string.Empty) +
                              "\"paths\":{\"/a\":{\"get\":{\"operationId\":\"a\"," +
                              (operationSecurity.Length > 0 ? "\"security\":" + operationSecurity + "," : string.Empty) +
                              "\"responses\":{\"204\":{\"description\":\"ok\"}}}}}," +
                              "\"components\":{\"securitySchemes\":" + schemes + "}}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            return (diagnostic.Id, diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("{\"s\":{\"type\":\"apiKey\",\"name\":\"k\"}}", "/components/securitySchemes/s", "'in'")]
        [InlineData("{\"s\":{\"type\":\"apiKey\",\"in\":\"header\"}}", "/components/securitySchemes/s", "'name'")]
        [InlineData("{\"s\":{\"type\":\"apiKey\",\"in\":\"body\",\"name\":\"k\"}}", "/components/securitySchemes/s/in", "'in'")]
        [InlineData("{\"s\":{\"type\":\"apiKey\",\"in\":\"header\",\"name\":\"bad name\"}}", "/components/securitySchemes/s/name", "HTTP token")]
        [InlineData("{\"s\":{\"type\":\"http\"}}", "/components/securitySchemes/s", "'scheme'")]
        [InlineData("{\"s\":{\"type\":\"oauth2\"}}", "/components/securitySchemes/s", "'flows'")]
        [InlineData("{\"s\":{\"type\":\"oauth2\",\"flows\":{\"implicit\":{\"scopes\":{}}}}}", "/components/securitySchemes/s/flows/implicit", "'authorizationUrl'")]
        [InlineData("{\"s\":{\"type\":\"oauth2\",\"flows\":{\"password\":{\"tokenUrl\":\"u\"}}}}", "/components/securitySchemes/s/flows/password", "'scopes'")]
        [InlineData("{\"s\":{\"type\":\"oauth2\",\"flows\":{\"magic\":{}}}}", "/components/securitySchemes/s/flows/magic", "Unknown oauth2 flow")]
        [InlineData("{\"s\":{\"type\":\"openIdConnect\"}}", "/components/securitySchemes/s", "'openIdConnectUrl'")]
        [InlineData("{\"s\":{\"type\":\"saml\"}}", "/components/securitySchemes/s/type", "security scheme type")]
        [InlineData("{\"s\":{}}", "/components/securitySchemes/s", "'type'")]
        public void MalformedSecuritySchemeReportsOacg100AtItsLogicalPath(string schemes, string path, string fragment)
        {
            (string id, string message) = Diagnose(schemes);

            Assert.Equal("OACG100", id);
            Assert.Contains("logical path '" + path + "'", message);
            Assert.Contains(fragment, message);
        }

        [Theory]
        [InlineData("Cookie")]
        [InlineData("cookie")]
        [InlineData("Content-Type")]
        [InlineData("content-length")]
        [InlineData("Content-Encoding")]
        public void ApiKeyHeaderNamedCookieOrContentStarReportsOacg100(string name)
        {
            (string id, string message) = Diagnose("{\"s\":{\"type\":\"apiKey\",\"in\":\"header\",\"name\":\"" + name + "\"}}");

            Assert.Equal("OACG100", id);
            Assert.Contains("logical path '/components/securitySchemes/s/name'", message);
        }

        [Fact]
        public async Task ConfiguredCookieApiKeyReplacesTheSameNamedCookieParameter()
        {
            Sent sent = await Call("getCookieSame", With(("CookieKeyApiKey", "s1")), default, "param", "dark");
            Sent unconfigured = await Call("getCookieSame", null, default, "param", "dark");

            Assert.Equal("theme=dark; sid=s1", Assert.Single(sent.CookieHeaders));
            Assert.Equal("sid=param; theme=dark", Assert.Single(unconfigured.CookieHeaders));
        }

        [Fact]
        public void MutualTlsRequiresOpenApi31()
        {
            (string id, string message) = Diagnose("{\"s\":{\"type\":\"mutualTLS\"}}", version: "3.0.3");

            Assert.Equal("OACG100", id);
            Assert.Contains("logical path '/components/securitySchemes/s/type'", message);
        }

        [Fact]
        public void UndefinedSchemeInDocumentSecurityReportsOacg100()
        {
            (string id, string message) = Diagnose(
                "{\"s\":{\"type\":\"http\",\"scheme\":\"bearer\"}}", security: "[{\"s\":[]},{\"missing\":[]}]");

            Assert.Equal("OACG100", id);
            Assert.Contains("logical path '/security/1/missing'", message);
            Assert.Contains("'missing'", message);
        }

        [Fact]
        public void UndefinedSchemeInOperationSecurityReportsOacg100()
        {
            (string id, string message) = Diagnose(
                "{\"s\":{\"type\":\"http\",\"scheme\":\"bearer\"}}", operationSecurity: "[{\"missing\":[]}]");

            Assert.Equal("OACG100", id);
            Assert.Contains("logical path '/paths/~1a/get/security/0/missing'", message);
        }

        [Fact]
        public void SecurityRequirementScopesMustBeAnArrayOfStrings()
        {
            (string id, string message) = Diagnose(
                "{\"s\":{\"type\":\"http\",\"scheme\":\"bearer\"}}", security: "[{\"s\":[1]}]");

            Assert.Equal("OACG100", id);
            Assert.Contains("logical path '/security/0/s/0'", message);
        }

        [Fact]
        public void ValidSchemesProduceNoDiagnostics()
        {
            string document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", "security.json"));
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        // ---- generated documentation and naming ------------------------------------------------------

        [Fact]
        public void GeneratedDocumentationCarriesSchemeDetailsAndTheThreadSafetyNote()
        {
            string document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", "security.json"));
            string source = Phase4GeneratorTestHarness.GenerateAndCompile(document).GeneratedSource;

            Assert.Contains("Key issued in the console.", source);
            Assert.Contains("Bearer format: JWT.", source);
            Assert.Contains("OAuth2 flow authorizationCode:", source);
            Assert.Contains("Scope read: Read data", source);
            Assert.Contains("OpenID Connect discovery URL: https://auth.example.com/.well-known/openid-configuration", source);
            Assert.Contains("not thread safe", source);
        }

        [Fact]
        public void CredentialPropertyNamesAvoidCollisionsWithEachOther()
        {
            const string Document = @"{
  ""openapi"": ""3.0.3"",
  ""info"": { ""title"": ""Names"", ""version"": ""1"" },
  ""paths"": { ""/a"": { ""get"": { ""operationId"": ""a"", ""security"": [ { ""a-b"": [] } ], ""responses"": { ""204"": { ""description"": ""ok"" } } } } },
  ""components"": { ""securitySchemes"": {
    ""a-b"": { ""type"": ""http"", ""scheme"": ""bearer"" },
    ""a_b"": { ""type"": ""http"", ""scheme"": ""bearer"" },
    ""A-b"": { ""type"": ""http"", ""scheme"": ""bearer"" }
  } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Type api = Phase4WireHarness.ClientType(execution.EmitAssembly());
            Assert.NotNull(api.GetProperty("ABAccessToken"));
            Assert.NotNull(api.GetProperty("A_bAccessToken"));
            Assert.NotNull(api.GetProperty("ABAccessToken2"));
        }

        [Fact]
        public void ASchemeNamedLikeTheClientDoesNotBreakCompilation()
        {
            const string Document = @"{
  ""openapi"": ""3.0.3"",
  ""info"": { ""title"": ""Names"", ""version"": ""1"" },
  ""security"": [ { ""Phase4Api"": [] } ],
  ""paths"": { ""/a"": { ""get"": { ""operationId"": ""phase4ApiAccessToken"", ""responses"": { ""204"": { ""description"": ""ok"" } } } } },
  ""components"": { ""securitySchemes"": { ""Phase4Api"": { ""type"": ""http"", ""scheme"": ""bearer"" } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void AnOperationParameterNamedLikeTheSecurityLocalStillCompiles()
        {
            const string Document = @"{
  ""openapi"": ""3.0.3"",
  ""info"": { ""title"": ""Names"", ""version"": ""1"" },
  ""security"": [ { ""k"": [] } ],
  ""paths"": { ""/a"": { ""get"": { ""operationId"": ""a"",
    ""parameters"": [ { ""name"": ""oacgSecurity"", ""in"": ""query"", ""schema"": { ""type"": ""string"" } } ],
    ""responses"": { ""204"": { ""description"": ""ok"" } } } } },
  ""components"": { ""securitySchemes"": { ""k"": { ""type"": ""apiKey"", ""in"": ""query"", ""name"": ""key"" } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }
    }
}
