using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Compiles a spec once per JSON serializer (Newtonsoft.Json and System.Text.Json) and calls the
    /// generated client through reflection, so that one test body runs against both serializers and
    /// the wire representation can be compared. The scripted handler and the request record come from
    /// Phase4BodyWireHarness.
    /// </summary>
    internal static class SerializerMatrixHarness
    {
        internal const string NewtonsoftJson = "NewtonsoftJson";

        internal const string SystemTextJson = "SystemTextJson";

        private static readonly ConcurrentDictionary<string, Lazy<Assembly>> Compiled =
            new ConcurrentDictionary<string, Lazy<Assembly>>();

        internal static bool IsStj(string serializer) => serializer == SystemTextJson;

        /// <summary>Compiles ApiSurfaceSpecs/<paramref name="specFile"/> with the serializer (cached).</summary>
        internal static Assembly Compile(string specFile, string serializer)
        {
            return Compiled.GetOrAdd(
                specFile + "|" + serializer,
                _ => new Lazy<Assembly>(() => CompileUncached(specFile, serializer))).Value;
        }

        /// <summary>Compiles an inline spec once per (key, serializer); the key names the spec.</summary>
        internal static Assembly CompileText(string key, string spec, string serializer)
        {
            return Compiled.GetOrAdd(
                "text:" + key + "|" + serializer,
                _ => new Lazy<Assembly>(() =>
                {
                    Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                        spec,
                        options: new Phase4CompilationOptions
                        {
                            JsonSerializer = serializer,
                            Newtonsoft = !IsStj(serializer)
                        });
                    if (execution.RunResult.Diagnostics.Length > 0 || execution.CompilationErrors.Count > 0)
                    {
                        throw new InvalidOperationException(
                            key + " (" + serializer + "): " +
                            string.Join("; ", execution.RunResult.Diagnostics.Select(static value => value.ToString())) +
                            string.Join("; ", execution.CompilationErrors.Select(static value => value.ToString())));
                    }

                    return execution.EmitAssembly();
                })).Value;
        }

        internal static Assembly Core(string serializer) => Compile("serializer-core.json", serializer);

        internal static Assembly Bodies(string serializer) => Phase4BodyWireHarness.BodiesFor(serializer);

        private static Assembly CompileUncached(string specFile, string serializer)
        {
            string spec = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", specFile));
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec,
                options: new Phase4CompilationOptions { JsonSerializer = serializer });
            if (execution.RunResult.Diagnostics.Length > 0 || execution.CompilationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    specFile + " (" + serializer + "): " +
                    string.Join("; ", execution.RunResult.Diagnostics.Select(static value => value.ToString())) +
                    string.Join("; ", execution.CompilationErrors.Select(static value => value.ToString())));
            }

            return execution.EmitAssembly();
        }

        internal static Type JsonExceptionType(string serializer)
        {
            return IsStj(serializer) ? typeof(System.Text.Json.JsonException) : typeof(Newtonsoft.Json.JsonException);
        }

        internal static Type TypeOf(Assembly assembly, string name)
        {
            return assembly.GetType(Phase4WireHarness.Ns + name, throwOnError: true)!;
        }

        /// <summary>Creates a generated DTO and sets its properties by name.</summary>
        internal static object New(Assembly assembly, string typeName, params (string Property, object? Value)[] values)
        {
            Type type = TypeOf(assembly, typeName);
            object instance = Activator.CreateInstance(type)!;
            foreach ((string property, object? value) in values)
            {
                type.GetProperty(property)!.SetValue(instance, value);
            }

            return instance;
        }

        internal static object? Get(object instance, string property)
        {
            return instance.GetType().GetProperty(property)!.GetValue(instance);
        }

        internal static object EnumValue(Assembly assembly, string typeName, string member)
        {
            return Enum.Parse(TypeOf(assembly, typeName), member);
        }

        /// <summary>An Any value (JToken or JsonElement?) parsed from JSON text.</summary>
        internal static object? MakeAny(string serializer, string json)
        {
            if (IsStj(serializer))
            {
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
                return document.RootElement.Clone();
            }

            return JToken.Parse(json);
        }

        /// <summary>A FreeObject value (JObject or JsonObject) parsed from JSON text.</summary>
        internal static object MakeFreeObject(string serializer, string json)
        {
            return IsStj(serializer)
                ? System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject()
                : JObject.Parse(json);
        }

        /// <summary>The compact JSON text of an Any / FreeObject / extension data value.</summary>
        internal static string JsonText(object? value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case JToken token:
                    return token.ToString(Newtonsoft.Json.Formatting.None);
                case System.Text.Json.JsonElement element:
                    return element.GetRawText();
                case System.Text.Json.Nodes.JsonNode node:
                    return node.ToJsonString();
                default:
                    throw new InvalidOperationException("Not a JSON value: " + value.GetType());
            }
        }

        /// <summary>The request body of an exchange, compared as JSON (member order and whitespace do not matter).</summary>
        internal static JToken Body(Phase4BodyWireHarness.Exchange exchange)
        {
            return JToken.Parse(System.Text.Encoding.UTF8.GetString(exchange.Body));
        }

        internal static Task<(object? Result, Phase4BodyWireHarness.Exchange Request)> Call(
            Assembly assembly,
            string method,
            HttpResponseMessage response,
            params object?[] arguments)
        {
            return Phase4BodyWireHarness.Call(assembly, method, _ => response, arguments);
        }

        internal static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return Phase4BodyWireHarness.Respond(status, body);
        }
    }
}
