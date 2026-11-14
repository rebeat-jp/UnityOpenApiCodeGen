using System;
using System.Collections;
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

using static Rhycol.OpenApiCodeGen.SourceGenerator.Tests.SerializerMatrixHarness;
using static Rhycol.OpenApiCodeGen.SourceGenerator.Tests.UnityWebRequestHarness;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// The UnityWebRequest transport (adapter) of the generated client, with both JSON serializers.
    /// The generated code is compiled against an in-memory UnityEngine stub (UnityEngineStub). The stub sends
    /// through an HttpMessageHandler, so the same scenario can run through an HttpClient client and a
    /// UnityWebRequest client and the requests, the results and the errors can be compared.
    /// </summary>
    public sealed class UnityWebRequestTransportTests
    {
        private const string Ns = Phase4WireHarness.Ns;

        private static readonly string[] AllSpecs =
        {
            "representative.json", "bodies.json", "coverage.json", "operations.json", "security.json",
            "serializer-core.json", "composition.json", "openapi32.json", "swagger2.json"
        };

        // ---- compiles ----------------------------------------------------------------------------------

        public static IEnumerable<object[]> SpecsAndSerializers()
        {
            foreach (string spec in AllSpecs.Concat(new[] { "constraints (inline)" }))
            {
                foreach (string serializer in new[] { NewtonsoftJson, SystemTextJson })
                {
                    yield return new object[] { spec, serializer };
                }
            }
        }

        private static string ReadSpec(string specFile)
        {
            return specFile == "constraints (inline)"
                ? Phase6ConstraintTests.Spec31()
                : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", specFile));
        }

        [Theory]
        [MemberData(nameof(SpecsAndSerializers))]
        public void ClientCompilesAgainstTheUnityEngineStubWithoutHttpClient(string specFile, string serializer)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReadSpec(specFile),
                options: new Phase4CompilationOptions
                {
                    UnityWebRequestStub = true,
                    HttpTransport = "UnityWebRequest",
                    JsonSerializer = serializer,
                    Newtonsoft = !IsStj(serializer)
                });

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.DoesNotContain("HttpClient _httpClient", execution.GeneratedSource);
            Assert.DoesNotContain("_httpClient.", execution.GeneratedSource);
            Assert.Contains("global::UnityEngine.Networking.UnityWebRequest", execution.GeneratedSource);
        }

        /// <summary>
        /// Unity compiles against netstandard2.1 (with the netstandard2.0 build of System.Text.Json 8 or
        /// Newtonsoft.Json 13) and UnityEngine. The adapter must compile against that set too: BCL members
        /// that only exist in newer .NET (for example a CancellationToken overload of ReadAsByteArrayAsync)
        /// are caught here.
        /// </summary>
        [Theory]
        [MemberData(nameof(SpecsAndSerializers))]
        public void ClientCompilesAgainstTheUnityNetStandard21ReferenceSetAndTheStub(string specFile, string serializer)
        {
            string root = typeof(UnityWebRequestTransportTests).Assembly
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

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReadSpec(specFile),
                options: new Phase4CompilationOptions
                {
                    UnityWebRequestStub = true,
                    HttpTransport = "UnityWebRequest",
                    JsonSerializer = serializer,
                    ReferencePaths = references
                });

            Assert.DoesNotContain(
                execution.OutputCompilation.References.Select(static reference => reference.Display),
                static display => display != null && display.Contains("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void HttpClientOutputHasNoUnityWebRequestMember()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReadSpec("security.json"));

            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("private readonly global::System.Net.Http.HttpClient _httpClient;", execution.GeneratedSource);
            Assert.DoesNotContain("UnityEngine", execution.GeneratedSource);
            Assert.DoesNotContain("RequestTimeout", execution.GeneratedSource);
            Assert.DoesNotContain("ConfigureAwait(true)", execution.GeneratedSource);
        }

        [Theory]
        [InlineData("RequestTimeout")]
        [InlineData("ConfigureUnityWebRequest")]
        [InlineData("SendUnityWebRequestAsync")]
        [InlineData("_baseAddress")]
        public void NamesOfTheUnityWebRequestMembersAreReservedForTheClientName(string apiName)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReadSpec("representative.json"),
                apiName: apiName,
                options: new Phase4CompilationOptions { UnityWebRequestStub = true, HttpTransport = "UnityWebRequest" });

            Diagnostic invalid = Assert.Single(execution.RunResult.Diagnostics, static item => item.Id == "OACG005");
            Assert.Contains("conflicts with a generated client member", invalid.GetMessage());
        }

        [Theory]
        [InlineData("SetUnityRequestHeader")]
        [InlineData("AddUnityResponseHeader")]
        public void EveryUnityWebRequestMemberNameIsReservedForTheUnityWebRequestClientName(string apiName)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReadSpec("representative.json"),
                apiName: apiName,
                options: new Phase4CompilationOptions { UnityWebRequestStub = true, HttpTransport = "UnityWebRequest" });

            Assert.Single(execution.RunResult.Diagnostics, static item => item.Id == "OACG005");
        }

        /// <summary>The UnityWebRequest member names do not restrict the client names of the HttpClient transport.</summary>
        [Theory]
        [InlineData("RequestTimeout")]
        [InlineData("ConfigureUnityWebRequest")]
        [InlineData("SendUnityWebRequestAsync")]
        [InlineData("SetUnityRequestHeader")]
        [InlineData("AddUnityResponseHeader")]
        [InlineData("_baseAddress")]
        public void HttpClientStillAcceptsTheUnityWebRequestMemberNamesAsClientName(string apiName)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReadSpec("representative.json"),
                apiName: apiName);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        // ---- the main thread ---------------------------------------------------------------------------

        /// <summary>
        /// No ConfigureAwait(false) may stand between the start of an operation method and the send: the
        /// UnityWebRequest has to be created and sent on the context of the caller.
        /// </summary>
        [Theory]
        [InlineData("security.json", NewtonsoftJson)]
        [InlineData("security.json", SystemTextJson)]
        [InlineData("bodies.json", NewtonsoftJson)]
        [InlineData("operations.json", SystemTextJson)]
        public void NoOperationUsesConfigureAwaitFalseBeforeTheSend(string specFile, string serializer)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReadSpec(specFile),
                options: new Phase4CompilationOptions
                {
                    UnityWebRequestStub = true,
                    HttpTransport = "UnityWebRequest",
                    JsonSerializer = serializer,
                    Newtonsoft = !IsStj(serializer)
                });
            string source = execution.GeneratedSource;
            const string Send = "using (var response = await SendUnityWebRequestAsync(request, cancellationToken))";
            const string MethodStart = "        public async ";

            int sends = 0;
            int from = 0;
            while (true)
            {
                int send = source.IndexOf(Send, from, StringComparison.Ordinal);
                if (send < 0)
                {
                    break;
                }

                int start = source.LastIndexOf(MethodStart, send, StringComparison.Ordinal);
                Assert.True(start >= 0);
                string beforeSend = source.Substring(start, send - start);
                Assert.DoesNotContain("ConfigureAwait(false)", beforeSend);
                sends++;
                from = send + Send.Length;
            }

            Assert.True(sends > 0);
            Assert.DoesNotContain("await _httpClient", source);
            if (specFile == "security.json")
            {
                Assert.Contains("(cancellationToken).ConfigureAwait(true);", source);
            }
        }

        private static async Task<TransportOutcome> CallOnPump(
            MainThreadPump pump,
            Assembly assembly,
            string method,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
            object?[] arguments,
            Action<object>? configure = null,
            CancellationToken cancellationToken = default,
            string? baseUrl = null)
        {
            object?[] withToken = arguments.Concat(new object?[] { cancellationToken }).ToArray();
            return await Call(assembly, true, method, respond, withToken, configure, baseUrl: baseUrl, pump: pump);
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task GetAndPostStayOnTheMainThread(string serializer)
        {
            using var pump = new MainThreadPump();
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", serializer);
            object pet = New(bodies, "Pet", ("Name", "Rex"));

            TransportOutcome post = await CallOnPump(
                pump, bodies, "createPet", Respond(() => Phase4BodyWireHarness.Respond(HttpStatusCode.Created, "{\"id\":1,\"name\":\"Rex\"}")), new[] { pet });
            TransportOutcome get = await CallOnPump(
                pump, bodies, "getNote", Respond(() => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "hi", "text/plain")), Array.Empty<object?>());

            Assert.Null(post.Error);
            Assert.Null(get.Error);
            Assert.Equal("POST", post.Request!.Method);
            Assert.Equal("GET", get.Request!.Method);
            foreach (TransportOutcome outcome in new[] { post, get })
            {
                foreach (string name in new[] { "ctor", "header", "send", "dispose" })
                {
                    Assert.True(outcome.Has(name), name);
                    Assert.All(outcome.ThreadsOf(name), id => Assert.Equal(pump.ThreadId, id));
                }
            }
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task AnAsyncCredentialProviderThatLeavesTheThreadDoesNotMoveTheSend(string serializer)
        {
            using var pump = new MainThreadPump();
            Assembly security = UnityWebRequestHarness.Compile("security.json", serializer);
            var provider = new Func<CancellationToken, Task<string?>>(async token =>
            {
                // Continues on a thread pool thread.
                await Task.Delay(20, token).ConfigureAwait(false);
                Assert.NotEqual(pump.ThreadId, Environment.CurrentManagedThreadId);
                return "tok";
            });

            TransportOutcome outcome = await CallOnPump(
                pump,
                security,
                "getBearer",
                Respond(() => new HttpResponseMessage(HttpStatusCode.NoContent)),
                Array.Empty<object?>(),
                client => client.GetType().GetProperty("BearerAuthAccessTokenProvider")!.SetValue(client, provider),
                baseUrl: "https://api.example.com");

            Assert.Null(outcome.Error);
            Assert.Equal("Bearer tok", outcome.Request!.Headers["Authorization"]);
            Assert.All(outcome.ThreadsOf("ctor"), id => Assert.Equal(pump.ThreadId, id));
            Assert.All(outcome.ThreadsOf("send"), id => Assert.Equal(pump.ThreadId, id));
            Assert.All(outcome.ThreadsOf("dispose"), id => Assert.Equal(pump.ThreadId, id));
        }

        [Fact]
        public async Task ConfigureUnityWebRequestRunsOnTheCallerThreadBeforeTheSend()
        {
            using var pump = new MainThreadPump();
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);
            int threadOfCallback = -1;
            object? timeoutSeen = null;
            Delegate configure = MakeConfigure(request =>
            {
                threadOfCallback = Environment.CurrentManagedThreadId;
                timeoutSeen = Read(request, "timeout");
                request.GetType().GetMethod("SetRequestHeader")!.Invoke(request, new object[] { "X-Configured", "yes" });
            });

            TransportOutcome outcome = await CallOnPump(
                pump,
                bodies,
                "getNote",
                Respond(() => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "hi", "text/plain")),
                Array.Empty<object?>(),
                client =>
                {
                    client.GetType().GetProperty("ConfigureUnityWebRequest")!.SetValue(client, configure);
                    client.GetType().GetProperty("RequestTimeout")!.SetValue(client, TimeSpan.FromMilliseconds(1500));
                });

            Assert.Null(outcome.Error);
            Assert.Equal(pump.ThreadId, threadOfCallback);
            Assert.Equal(2, timeoutSeen);
            Assert.Equal("yes", outcome.Request!.Headers["X-Configured"]);
        }

        // ---- cancellation, timeout, errors -------------------------------------------------------------

        private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> NeverAnswers()
        {
            return async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK);
            };
        }

        [Fact]
        public async Task CancellingTheTokenAbortsTheRequestOnTheMainThreadAndThrowsOperationCanceled()
        {
            using var pump = new MainThreadPump();
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);
            using var source = new CancellationTokenSource();

            Task<TransportOutcome> running = CallOnPump(
                pump, bodies, "getNote", NeverAnswers(), Array.Empty<object?>(), null, source.Token);
            // The request is sent when the log has the "send" entry; the token is cancelled from this (other) thread.
            // The log lives on the outcome, which is only returned at the end, so poll the stub statics instead.
            await Task.Delay(300);
            source.Cancel();
            TransportOutcome outcome = await running.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);
            Assert.True(outcome.Has("send"));
            Assert.True(outcome.Has("abort"));
            Assert.Equal(new[] { pump.ThreadId }, outcome.ThreadsOf("abort").Distinct());
            Assert.Equal(new[] { pump.ThreadId }, outcome.ThreadsOf("dispose").Distinct());
        }

        [Fact]
        public async Task ATokenThatIsAlreadyCancelledNeverCreatesARequest()
        {
            using var pump = new MainThreadPump();
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);
            using var source = new CancellationTokenSource();
            source.Cancel();

            TransportOutcome outcome = await CallOnPump(
                pump, bodies, "getNote", NeverAnswers(), Array.Empty<object?>(), null, source.Token);

            Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);
            Assert.False(outcome.Has("ctor"));
        }

        [Fact]
        public async Task ATimeoutThrowsTaskCanceledExceptionWithATimeoutExceptionInside()
        {
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);

            TransportOutcome outcome = await Call(
                bodies,
                true,
                "getNote",
                NeverAnswers(),
                Array.Empty<object?>(),
                client => client.GetType().GetProperty("RequestTimeout")!.SetValue(client, TimeSpan.FromMilliseconds(400)));

            TaskCanceledException canceled = Assert.IsType<TaskCanceledException>(outcome.Error);
            Assert.IsType<TimeoutException>(canceled.InnerException);
            Assert.False(outcome.Has("abort"));
            Assert.True(outcome.Has("dispose"));
        }

        [Fact]
        public async Task ANetworkFailureThrowsHttpRequestException()
        {
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);

            TransportOutcome outcome = await Call(
                bodies,
                true,
                "getNote",
                (_, _) => throw new HttpRequestException("connection refused by the test"),
                Array.Empty<object?>());

            HttpRequestException failure = Assert.IsType<HttpRequestException>(outcome.Error);
            Assert.Contains("connection refused by the test", failure.Message);
            Assert.True(outcome.Has("dispose"));
        }

        [Fact]
        public async Task AnHttpErrorStatusIsAnApiExceptionNotANetworkFailure()
        {
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);

            TransportOutcome outcome = await Call(
                bodies,
                true,
                "getStatus",
                Respond(() => Phase4BodyWireHarness.Respond(HttpStatusCode.NotFound, "{\"title\":\"gone\"}")),
                Array.Empty<object?>());

            Exception failure = Assert.IsAssignableFrom<Exception>(outcome.Error);
            Assert.Equal("Phase4ApiException", failure.GetType().Name);
            Assert.Equal(HttpStatusCode.NotFound, failure.GetType().GetProperty("StatusCode")!.GetValue(failure));
            Assert.Equal("gone", Get(Get(failure, "Error")!, "Title"));
        }

        // ---- restricted headers, timeout detection, header names, null context ---------------------------

        private const string RestrictedHeaderSpec =
            "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"servers\":[{\"url\":\"https://api.example.test\"}]," +
            "\"paths\":{\"/p\":{\"get\":{\"operationId\":\"getP\",\"parameters\":[{\"name\":\"Expect\",\"in\":\"header\",\"schema\":{\"type\":\"string\"}}]," +
            "\"responses\":{\"204\":{\"description\":\"ok\"}}}}}}";

        [Fact]
        public async Task ARestrictedRequestHeaderThrowsAnExceptionThatNamesTheHeader()
        {
            Assembly api = CompileInline("restricted", RestrictedHeaderSpec, NewtonsoftJson);

            TransportOutcome outcome = await Call(
                api, true, "getP", Respond(() => new HttpResponseMessage(HttpStatusCode.NoContent)), new object?[] { "100-continue" });

            InvalidOperationException failure = Assert.IsType<InvalidOperationException>(outcome.Error);
            Assert.Contains("'Expect'", failure.Message);
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Null(outcome.Request);
            Assert.True(outcome.Has("dispose"));
        }

        [Fact]
        public async Task ATimeoutIsRecognisedByTheElapsedTimeWhenTheErrorTextDoesNotSayTimeout()
        {
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);

            TransportOutcome outcome = await Call(
                bodies,
                true,
                "getNote",
                NeverAnswers(),
                Array.Empty<object?>(),
                client => client.GetType().GetProperty("RequestTimeout")!.SetValue(client, TimeSpan.FromMilliseconds(400)),
                stubSetup: stub => stub.GetProperty("TimeoutText")!.SetValue(null, "the player gave up"));

            TaskCanceledException canceled = Assert.IsType<TaskCanceledException>(outcome.Error);
            TimeoutException inner = Assert.IsType<TimeoutException>(canceled.InnerException);
            Assert.Contains("the player gave up", inner.Message);
        }

        [Fact]
        public async Task AFastNetworkFailureWithATimeoutSetIsNotATimeout()
        {
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);

            TransportOutcome outcome = await Call(
                bodies,
                true,
                "getNote",
                (_, _) => throw new HttpRequestException("refused"),
                Array.Empty<object?>(),
                client => client.GetType().GetProperty("RequestTimeout")!.SetValue(client, TimeSpan.FromSeconds(30)));

            Assert.IsType<HttpRequestException>(outcome.Error);
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task ResponseHeaderNamesInUpperCaseAreRoutedLikeHttpClient(string serializer)
        {
            Assembly viaHttpClient = SerializerMatrixHarness.Compile("bodies.json", serializer);
            Assembly viaUnityWebRequest = UnityWebRequestHarness.Compile("bodies.json", serializer);
            Func<HttpResponseMessage> response = () => Phase4BodyWireHarness.Respond(
                HttpStatusCode.OK,
                "{\"name\":\"Rex\"}",
                "application/json",
                ("X-Rate-Limit", "42"),
                ("X-Tags", "a, b"),
                ("X-Trace", "t-1"));

            TransportOutcome reference = await Call(
                viaHttpClient, false, "getWithHeadersWithHttpInfo", Respond(response), Array.Empty<object?>());
            TransportOutcome adapted = await Call(
                viaUnityWebRequest,
                true,
                "getWithHeadersWithHttpInfo",
                Respond(response),
                Array.Empty<object?>(),
                stubSetup: stub => stub.GetProperty("UppercaseResponseHeaderNames")!.SetValue(null, "1"));

            Assert.Null(adapted.Error);
            Assert.Equal(Describe(reference.Result), Describe(adapted.Result));
        }

        [Fact]
        public async Task SeveralCookiesAreSentAsOneHeaderJoinedWithSemicolons()
        {
            Assembly security = UnityWebRequestHarness.Compile("security.json", NewtonsoftJson);

            TransportOutcome outcome = await Call(
                security,
                true,
                "getCookieKey",
                Respond(() => new HttpResponseMessage(HttpStatusCode.NoContent)),
                new object?[] { "dark" },
                SetProperties(("CookieKeyApiKey", "s1")),
                baseUrl: "https://api.example.com");

            Assert.Null(outcome.Error);
            string cookie = outcome.Request!.Headers["Cookie"];
            Assert.Contains("; ", cookie);
            Assert.Contains("s1", cookie);
            Assert.Contains("dark", cookie);
        }

        [Fact]
        public async Task CancellingWithoutASynchronizationContextStillAbortsAndDisposesOnce()
        {
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);
            using var source = new CancellationTokenSource();

            // Task.Run: no SynchronizationContext, so Abort() runs on the thread that cancels.
            Task<TransportOutcome> running = Task.Run(async () =>
            {
                Assert.Null(SynchronizationContext.Current);
                return await Call(
                    bodies, true, "getNote", NeverAnswers(), new object?[] { source.Token });
            });
            await Task.Delay(300);
            source.Cancel();
            TransportOutcome outcome = await running.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);
            Assert.Single(outcome.ThreadsOf("abort"));
            Assert.Single(outcome.ThreadsOf("dispose"));
        }

        // ---- base address --------------------------------------------------------------------------------

        private const string RelativeServerSpec =
            "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"servers\":[{\"url\":\"/v1\"}]," +
            "\"paths\":{\"/pets\":{\"get\":{\"operationId\":\"listPets\",\"responses\":{\"204\":{\"description\":\"ok\"}}}}}}";

        [Fact]
        public async Task ARelativeServerUrlNeedsAnAbsoluteBaseAddress()
        {
            Assembly api = CompileInline("relative", RelativeServerSpec, NewtonsoftJson);
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> noContent =
                Respond(() => new HttpResponseMessage(HttpStatusCode.NoContent));

            TransportOutcome without = await Call(api, true, "listPets", noContent, Array.Empty<object?>());
            TransportOutcome with = await Call(
                api, true, "listPets", noContent, Array.Empty<object?>(), baseAddress: new Uri("https://host.test/app/"));

            Assert.IsType<InvalidOperationException>(without.Error);
            Assert.Contains("base address", without.Error!.Message);
            Assert.Null(without.Request);
            Assert.Null(with.Error);
            Assert.Equal("https://host.test/v1/pets", with.Request!.Uri!.ToString());
        }

        [Fact]
        public async Task AnAbsoluteServerUrlNeedsNoBaseAddressAndTheBaseUrlOverloadWins()
        {
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);
            var answer = Respond(() => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "x", "text/plain"));

            TransportOutcome declared = await Call(bodies, true, "getNote", answer, Array.Empty<object?>());
            TransportOutcome overridden = await Call(
                bodies, true, "getNote", answer, Array.Empty<object?>(), baseUrl: "https://other.test/api");

            Assert.Null(declared.Error);
            Assert.Equal("https://api.example.test/v1/note", declared.Request!.Uri!.ToString());
            Assert.Null(overridden.Error);
            Assert.Equal("https://other.test/api/note", overridden.Request!.Uri!.ToString());
        }

        [Fact]
        public void ARelativeBaseAddressIsRejected()
        {
            Assembly bodies = UnityWebRequestHarness.Compile("bodies.json", NewtonsoftJson);

            TargetInvocationException failure = Assert.Throws<TargetInvocationException>(
                () => CreateClient(bodies, new Uri("/relative", UriKind.Relative)));

            Assert.IsType<ArgumentException>(failure.InnerException);
        }

        // ---- the same requests and results as HttpClient -----------------------------------------------

        private sealed class Scenario
        {
            internal Scenario(
                string name,
                string spec,
                string method,
                Func<Assembly, object?[]> arguments,
                Func<HttpResponseMessage> response,
                Func<Action<object>?>? configure = null,
                string? baseUrl = null)
            {
                Name = name;
                Spec = spec;
                Method = method;
                Arguments = arguments;
                Response = response;
                Configure = configure;
                BaseUrl = baseUrl;
            }

            internal string Name { get; }

            internal string Spec { get; }

            internal string Method { get; }

            internal Func<Assembly, object?[]> Arguments { get; }

            internal Func<HttpResponseMessage> Response { get; }

            internal Func<Action<object>?>? Configure { get; }

            internal string? BaseUrl { get; }
        }

        private static object NewFile(Assembly api, byte[] data, string? fileName, string? contentType)
        {
            return Activator.CreateInstance(api.GetType(Ns + "Phase4ApiFileParameter", throwOnError: true)!, data, fileName, contentType)!;
        }

        private static Action<object> SetProperties(params (string Property, object? Value)[] values)
        {
            return client =>
            {
                foreach ((string property, object? value) in values)
                {
                    client.GetType().GetProperty(property)!.SetValue(client, value);
                }
            };
        }

        private static readonly Scenario[] Scenarios =
        {
            new Scenario(
                "json body", "bodies.json", "createPet",
                api => new[] { New(api, "Pet", ("Name", "Rex")) },
                () => Phase4BodyWireHarness.Respond(HttpStatusCode.Created, "{\"id\":1,\"name\":\"Rex\"}")),
            new Scenario(
                "text body", "bodies.json", "createPetAsText",
                _ => new object?[] { "héllo" },
                () => Phase4BodyWireHarness.Respond(HttpStatusCode.Created, "{\"id\":1,\"name\":\"Rex\"}")),
            new Scenario(
                "binary body", "bodies.json", "createPetAsBinary",
                _ => new object?[] { new byte[] { 0, 1, 2, 255, 254 } },
                () => Phase4BodyWireHarness.Respond(HttpStatusCode.Created, "{\"id\":1,\"name\":\"Rex\"}")),
            new Scenario(
                "wildcard binary body with a content type", "bodies.json", "uploadAvatar",
                _ => new object?[] { new byte[] { 7, 8, 9 }, CancellationToken.None, "image/png" },
                () => new HttpResponseMessage(HttpStatusCode.NoContent)),
            new Scenario(
                "optional body omitted", "bodies.json", "postOptional",
                _ => new object?[] { null },
                () => new HttpResponseMessage(HttpStatusCode.NoContent)),
            new Scenario(
                "multipart body", "bodies.json", "createPetAsMultipart",
                api =>
                {
                    Type fileType = api.GetType(Ns + "Phase4ApiFileParameter", throwOnError: true)!;
                    var attachments = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(fileType))!;
                    attachments.Add(NewFile(api, Encoding.ASCII.GetBytes("AAA"), "a.txt", "text/plain"));
                    return new[]
                    {
                        New(
                            api,
                            "PetForm",
                            ("Name", "Rex"),
                            ("Count", 3),
                            ("Tags", new List<string> { "a", "b" }),
                            ("Photo", NewFile(api, Encoding.ASCII.GetBytes("PNG!"), "p.png", null)),
                            ("Attachments", attachments),
                            ("Meta", New(api, "Meta", ("Origin", "x"))))
                    };
                },
                () => Phase4BodyWireHarness.Respond(HttpStatusCode.Created, "{\"id\":1,\"name\":\"Rex\"}")),
            new Scenario(
                "form-urlencoded body", "bodies.json", "createPetAsFormUrlEncoded",
                api => new[]
                {
                    New(
                        api,
                        "PetFields",
                        ("Name", "R x&y"),
                        ("Tags", new List<string> { "a", "b" }),
                        ("Attrs", new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }),
                        ("Note", "a/b c"),
                        ("Meta", New(api, "Meta", ("Origin", "é"))))
                },
                () => Phase4BodyWireHarness.Respond(HttpStatusCode.Created, "{\"id\":1,\"name\":\"Rex\"}")),
            new Scenario(
                "binary response", "bodies.json", "downloadFile",
                _ => Array.Empty<object?>(),
                () => Phase4BodyWireHarness.RespondBytes(HttpStatusCode.OK, new byte[] { 0, 255, 10, 13, 1 }, "application/octet-stream")),
            new Scenario(
                "text response", "bodies.json", "getNote",
                _ => Array.Empty<object?>(),
                () => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "héllo", "text/plain")),
            new Scenario(
                "wrong response media type", "bodies.json", "getNote",
                _ => Array.Empty<object?>(),
                () => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "{}", "application/json")),
            new Scenario(
                "typed error", "bodies.json", "getStatus",
                _ => Array.Empty<object?>(),
                () => Phase4BodyWireHarness.Respond(HttpStatusCode.NotFound, "{\"title\":\"gone\"}")),
            new Scenario(
                "untyped error", "bodies.json", "downloadFile",
                _ => Array.Empty<object?>(),
                () => Phase4BodyWireHarness.Respond(HttpStatusCode.NotFound, "{\"title\":\"x\"}")),
            new Scenario(
                "response headers", "bodies.json", "getWithHeadersWithHttpInfo",
                _ => Array.Empty<object?>(),
                () => Phase4BodyWireHarness.Respond(
                    HttpStatusCode.OK,
                    "{\"name\":\"Rex\"}",
                    "application/json",
                    ("X-Rate-Limit", "42"),
                    ("X-Tags", "a, b"),
                    ("X-Trace", "t-1"))),
            new Scenario(
                "full widget body", "serializer-core.json", "createWidget",
                api => new[] { SerializerMatrixTests.FullWidget(api, SerializerOf(api)) },
                () => Phase4BodyWireHarness.Respond(
                    HttpStatusCode.Created, "{\"id\":1,\"name\":\"w\",\"created\":\"2024-03-01T10:20:30+00:00\"}")),
            new Scenario(
                "path and query parameters", "serializer-core.json", "getWidget",
                api => new object?[]
                {
                    5L,
                    new DateTimeOffset(2024, 3, 1, 10, 20, 30, TimeSpan.Zero),
                    EnumValue(api, "Color", "DarkBlue"),
                    EnumValue(api, "Level", "Value2"),
                    1.5,
                    new DateTime(2024, 2, 29),
                    Guid.Parse("123e4567-e89b-12d3-a456-426614174000")
                },
                () => Phase4BodyWireHarness.Respond(
                    HttpStatusCode.OK, "{\"id\":1,\"name\":\"w\",\"created\":\"2024-03-01T10:20:30+00:00\"}")),
            new Scenario(
                "list response", "serializer-core.json", "listWidgets",
                _ => Array.Empty<object?>(),
                () => Phase4BodyWireHarness.Respond(
                    HttpStatusCode.OK,
                    "[{\"id\":1,\"name\":\"w\",\"created\":\"2024-03-01T10:20:30+00:00\"},{\"id\":2,\"name\":\"v\",\"created\":\"2024-03-01T10:20:30+00:00\"}]")),
            new Scenario(
                "invalid response body", "serializer-core.json", "getInts",
                _ => Array.Empty<object?>(),
                () => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, "[1, \"x\"]")),
            new Scenario(
                "header api key", "security.json", "getHeaderKey",
                _ => Array.Empty<object?>(),
                () => new HttpResponseMessage(HttpStatusCode.NoContent),
                () => SetProperties(("HeaderKeyApiKey", "k-123")),
                "https://api.example.com"),
            new Scenario(
                "bearer token", "security.json", "getBearer",
                _ => Array.Empty<object?>(),
                () => new HttpResponseMessage(HttpStatusCode.NoContent),
                () => SetProperties(("BearerAuthAccessToken", "tok")),
                "https://api.example.com"),
            new Scenario(
                "cookie api key", "security.json", "getCookieKey",
                _ => new object?[] { "dark" },
                () => new HttpResponseMessage(HttpStatusCode.NoContent),
                () => SetProperties(("CookieKeyApiKey", "s1")),
                "https://api.example.com")
        };

        // The widget builder needs to know the serializer; the generated assembly says it through its DTO attributes.
        private static string SerializerOf(Assembly api)
        {
            return api.GetType(Ns + "Widget")!.GetCustomAttributes().Any(
                static attribute => attribute.GetType().Namespace == "Newtonsoft.Json")
                ? NewtonsoftJson
                : SystemTextJson;
        }

        public static IEnumerable<object[]> ScenarioNames()
        {
            foreach (Scenario scenario in Scenarios)
            {
                foreach (string serializer in new[] { NewtonsoftJson, SystemTextJson })
                {
                    yield return new object[] { scenario.Name, serializer };
                }
            }
        }

        [Theory]
        [MemberData(nameof(ScenarioNames))]
        public async Task SendsTheSameRequestAndHandlesTheAnswerLikeHttpClient(string name, string serializer)
        {
            Scenario scenario = Scenarios.Single(item => item.Name == name);
            Assembly viaHttpClient = SerializerMatrixHarness.Compile(scenario.Spec, serializer);
            Assembly viaUnityWebRequest = UnityWebRequestHarness.Compile(scenario.Spec, serializer);

            TransportOutcome reference = await Call(
                viaHttpClient,
                false,
                scenario.Method,
                Respond(scenario.Response),
                scenario.Arguments(viaHttpClient),
                scenario.Configure?.Invoke(),
                baseUrl: scenario.BaseUrl);
            TransportOutcome adapted = await Call(
                viaUnityWebRequest,
                true,
                scenario.Method,
                Respond(scenario.Response),
                scenario.Arguments(viaUnityWebRequest),
                scenario.Configure?.Invoke(),
                baseUrl: scenario.BaseUrl);

            Assert.NotNull(reference.Request);
            Assert.NotNull(adapted.Request);
            Assert.Equal(DescribeRequest(reference.Request!), DescribeRequest(adapted.Request!));
            Assert.Equal(Describe(reference.Error), Describe(adapted.Error));
            Assert.Equal(Describe(reference.Result), Describe(adapted.Result));
            if (reference.Error is not null)
            {
                Assert.Equal(reference.Error.GetType().Name, adapted.Error!.GetType().Name);
            }
        }

        [Fact]
        public async Task ScenariosCoverSuccessAndFailure()
        {
            // Guards the parity theory against comparing two failures that happen to look alike.
            Scenario ok = Scenarios.Single(static item => item.Name == "json body");
            Assembly api = UnityWebRequestHarness.Compile(ok.Spec, NewtonsoftJson);

            TransportOutcome outcome = await Call(api, true, ok.Method, Respond(ok.Response), ok.Arguments(api));

            Assert.Null(outcome.Error);
            Assert.Contains("Rex", Describe(outcome.Result));
            Assert.Equal("application/json; charset=utf-8", outcome.Request!.ContentType);
            Assert.Contains(Scenarios, static item => item.Name == "typed error");
        }
    }
}
