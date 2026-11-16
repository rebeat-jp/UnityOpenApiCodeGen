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

using Newtonsoft.Json.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P0 of the composition work. Two kinds of tests share the fixtures of
    /// <see cref="CompositionConformanceFixtures"/>:
    /// <list type="bullet">
    /// <item>The current rejection (diagnostic id and logical path) of every fixture is pinned. A later phase
    /// that accepts a shape flips the matching pin into an acceptance test.</item>
    /// <item>The target behavior (generate, compile, then validate the instance table in both directions) is
    /// written down and skipped until the phase that implements it.</item>
    /// </list>
    /// </summary>
    public class CompositionConformanceTests
    {
        private const string Ns = "Generated.Phase4.";

        public CompositionConformanceTests()
            : this(SerializerMatrixHarness.NewtonsoftJson)
        {
        }

        protected CompositionConformanceTests(string serializer)
        {
            MatrixMode.Current = serializer;
        }

        public static IEnumerable<object[]> AllNames() => CompositionConformanceFixtures.Names();

        public static IEnumerable<object[]> P0Names() => CompositionConformanceFixtures.NamesOfPhase("P0");

        public static IEnumerable<object[]> P2Names() => CompositionConformanceFixtures.NamesOfPhase("P2");

        public static IEnumerable<object[]> P3Names() => CompositionConformanceFixtures.NamesOfPhase("P3");

        public static IEnumerable<object[]> P4Names() => CompositionConformanceFixtures.NamesOfPhase("P4");

        public static IEnumerable<object[]> P5Names() => CompositionConformanceFixtures.NamesOfPhase("P5");

        public static IEnumerable<object[]> P6Names() => CompositionConformanceFixtures.NamesOfPhase("P6");

        // ---- current behavior ----------------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(AllNames))]
        public void TheCurrentGeneratorRejectsTheShapeAtItsLogicalPath(string name)
        {
            ConformanceFixture fixture = CompositionConformanceFixtures.Get(name);

            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(fixture.Document());

            if (fixture.CurrentId.Length == 0)
            {
                // Not rejected: the shape is generated without a diagnostic (see the evaluation pin below).
                Assert.Empty(execution.RunResult.Diagnostics);
                Assert.Empty(execution.CompilationErrors);
                return;
            }

            Diagnostic first = execution.RunResult.Diagnostics.First();
            Assert.Equal(fixture.CurrentId, first.Id);
            Assert.Contains("logical path '" + fixture.CurrentLogicalPath + "'", first.GetMessage());
        }

        // ---- shapes that the generator already handles ---------------------------------------------------

        [Theory]
        [MemberData(nameof(P0Names))]
        public Task AlreadySupportedShapesMeetTheirInstanceTable(string name) => AssertTargetBehavior(name);

        [Fact]
        public void EveryFixtureHasValidAndInvalidInstances()
        {
            foreach (ConformanceFixture fixture in CompositionConformanceFixtures.All)
            {
                Assert.Contains(fixture.Instances, instance => instance.Valid);
                Assert.Contains(fixture.Instances, instance => !instance.Valid);
                Assert.All(fixture.Instances, instance => Assert.NotEmpty(instance.Reason));
                Assert.Equal(fixture.Name, fixture.Name.Trim());
                JToken.Parse(fixture.Document());
                foreach (ConformanceInstance instance in fixture.Instances)
                {
                    JToken.Parse(instance.Json.Replace('\'', '"'));
                }
            }

            Assert.Equal(
                CompositionConformanceFixtures.All.Count,
                CompositionConformanceFixtures.All.Select(static fixture => fixture.Name).Distinct().Count());
        }

        // ---- target behavior (skipped until the phase that implements it) --------------------------------

        [Theory]
        [MemberData(nameof(P2Names))]
        public Task P2TargetBehavior(string name) => AssertTargetBehavior(name);

        [Theory]
        [MemberData(nameof(P3Names))]
        public Task P3TargetBehavior(string name) => AssertTargetBehavior(name);

        [Theory]
        [MemberData(nameof(P4Names))]
        public Task P4TargetBehavior(string name) => AssertTargetBehavior(name);

        [Theory]
        [MemberData(nameof(P5Names))]
        public Task P5TargetBehavior(string name) => AssertTargetBehavior(name);

        [Theory]
        [MemberData(nameof(P6Names))]
        public Task P6TargetBehavior(string name) => AssertTargetBehavior(name);

        /// <summary>
        /// The fixture generates without a diagnostic and compiles; every instance is judged in both
        /// directions. Response: GET /echo returns the instance, an invalid one raises the serialization
        /// exception of the serializer. Request: POST /echo is given the instance (deserialized into the
        /// generated body type), a valid one is sent unchanged and an invalid one fails with an exception
        /// before or while it is sent.
        /// </summary>
        private static async Task AssertTargetBehavior(string name)
        {
            ConformanceFixture fixture = CompositionConformanceFixtures.Get(name);
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(fixture.Document());
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var failures = new List<string>();

            foreach (ConformanceInstance instance in fixture.Instances)
            {
                string json = instance.Json.Replace('\'', '"');
                string label = fixture.Name + " " + json + " (" + instance.Reason + ")";

                Exception? responseError = await Capture(() => CallRead(assembly, json));
                if (instance.Valid && responseError is not null)
                {
                    failures.Add("response should be accepted: " + label + " -> " + responseError.GetType().Name + ": " + responseError.Message);
                }
                else if (!instance.Valid && (responseError is null ||
                         responseError.GetType() != MatrixMode.SerializationExceptionType))
                {
                    failures.Add("response should raise the serialization exception: " + label);
                }

                string? sent = null;
                Exception? requestError = await Capture(async () => sent = await CallWrite(assembly, json));
                if (instance.Valid)
                {
                    if (requestError is not null)
                    {
                        failures.Add("request should be accepted: " + label + " -> " + requestError.GetType().Name + ": " + requestError.Message);
                    }
                    else if (!JToken.DeepEquals(JToken.Parse(json), JToken.Parse(sent!)))
                    {
                        failures.Add("request changed on the wire: " + label + " -> " + sent);
                    }
                }
                else if (requestError is null)
                {
                    failures.Add("request should be rejected: " + label);
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }

        private static async Task<Exception?> Capture(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static async Task CallRead(Assembly assembly, string responseJson)
        {
            await Invoke(assembly, "readBody", responseJson, _ => null).ConfigureAwait(false);
        }

        private static async Task<string> CallWrite(Assembly assembly, string requestJson)
        {
            return await Invoke(
                assembly,
                "writeBody",
                requestJson,
                type => MatrixMode.Deserialize(requestJson, type)).ConfigureAwait(false);
        }

        /// <summary>Calls the operation against a handler that answers with the given JSON; returns the request body.</summary>
        private static async Task<string> Invoke(
            Assembly assembly,
            string operation,
            string responseJson,
            Func<Type, object?> createBody)
        {
            Type clientType = assembly.GetType(Ns + "Phase4Api", true)!;
            MethodInfo method = clientType.GetMethods().Single(
                candidate => string.Equals(candidate.Name, operation, StringComparison.OrdinalIgnoreCase));
            var handler = new StubHandler(responseJson);
            using var httpClient = new HttpClient(handler);
            object client = Activator.CreateInstance(clientType, httpClient)!;
            ParameterInfo[] parameters = method.GetParameters();
            var arguments = new object?[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                arguments[index] = parameters[index].ParameterType == typeof(CancellationToken)
                    ? CancellationToken.None
                    : index == 0 && operation == "writeBody"
                        ? createBody(parameters[index].ParameterType)
                        : parameters[index].HasDefaultValue ? parameters[index].DefaultValue : null;
            }

            try
            {
                await ((Task)method.Invoke(client, arguments)!).ConfigureAwait(false);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }

            return handler.Content;
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _response;

            internal StubHandler(string response)
            {
                _response = response;
            }

            internal string Content { get; private set; } = string.Empty;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Content = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_response, Encoding.UTF8, "application/json")
                };
            }
        }
    }

    /// <summary>The System.Text.Json run of every test of <see cref="CompositionConformanceTests"/>.</summary>
    public sealed class CompositionConformanceSystemTextJsonTests : CompositionConformanceTests
    {
        public CompositionConformanceSystemTextJsonTests()
            : base(SerializerMatrixHarness.SystemTextJson)
        {
        }
    }
}
