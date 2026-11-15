using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// The JSON serializer that a test class that is run once per serializer generates its clients with.
    /// The class sets it in its constructor (xunit creates the instance in the async flow of the test, so
    /// the ambient value reaches the test method and its helpers, and classes that run in parallel do not
    /// see each other's value). The existing composition / constraint / OpenAPI 3.2 / Swagger 2 test
    /// classes are the Newtonsoft.Json run; a derived class with the same tests is the System.Text.Json
    /// run, so every scenario is judged by both serializers.
    /// </summary>
    internal static class MatrixMode
    {
        private static readonly AsyncLocal<string?> Mode = new AsyncLocal<string?>();

        internal static string Current
        {
            get => Mode.Value ?? SerializerMatrixHarness.NewtonsoftJson;
            set => Mode.Value = value;
        }

        internal static bool IsStj => Current == SerializerMatrixHarness.SystemTextJson;

        /// <summary>
        /// Phase4GeneratorTestHarness.GenerateAndCompile for the current serializer. A System.Text.Json
        /// client is compiled without any reference to Newtonsoft.Json.
        /// </summary>
        internal static Phase4GeneratorExecution GenerateAndCompile(
            string openApiJson,
            string apiName = Phase4GeneratorTestHarness.ApiName,
            string generatedNamespace = Phase4GeneratorTestHarness.GeneratedNamespace,
            string specId = TestBundleFactory.SpecId,
            string? bundle = null,
            ApiClientCodeGenerator? generator = null)
        {
            return Phase4GeneratorTestHarness.GenerateAndCompile(
                openApiJson,
                apiName,
                generatedNamespace,
                specId,
                bundle,
                generator,
                IsStj
                    ? new Phase4CompilationOptions
                    {
                        JsonSerializer = SerializerMatrixHarness.SystemTextJson,
                        Newtonsoft = false
                    }
                    : null);
        }

        /// <summary>
        /// Asserts that the client was generated for the serializer of the current run (the run of a derived
        /// class must not silently be a Newtonsoft.Json run).
        /// </summary>
        internal static void AssertGeneratedFor(string generatedSource, bool systemTextJson)
        {
            Assert.Equal(systemTextJson, IsStj);
            Assert.Equal(systemTextJson, generatedSource.Contains("global::System.Text.Json.JsonSerializer"));
            Assert.Equal(!systemTextJson, generatedSource.Contains("global::Newtonsoft.Json"));
        }

        /// <summary>The exception type that a malformed or contract-violating response raises.</summary>
        internal static Type SerializationExceptionType =>
            IsStj ? typeof(System.Text.Json.JsonException) : typeof(JsonSerializationException);

        /// <summary>Asserts that the error is exactly the serialization exception of the current serializer.</summary>
        internal static Exception AssertSerialization(Exception? error)
        {
            Assert.NotNull(error);
            Assert.IsType(SerializationExceptionType, error);
            return error!;
        }

        internal static async Task<Exception> ThrowsSerializationAsync(Func<Task> code)
        {
            return await Assert.ThrowsAsync(SerializationExceptionType, code);
        }

        internal static Exception ThrowsSerialization(Action code)
        {
            return Assert.Throws(SerializationExceptionType, code);
        }

        /// <summary>Deserializes JSON text into a generated type (a request body that a test hands to the client).</summary>
        internal static object? Deserialize(string json, Type type)
        {
            return IsStj
                ? System.Text.Json.JsonSerializer.Deserialize(json, type)
                : JsonConvert.DeserializeObject(json, type);
        }

        /// <summary>Serializes a generated plain object with the default settings of the serializer.</summary>
        internal static string Serialize(object value)
        {
            return IsStj
                ? System.Text.Json.JsonSerializer.Serialize(value, value.GetType())
                : JsonConvert.SerializeObject(value);
        }

        /// <summary>Wraps raw JSON in a union without a discriminator (FromJToken / FromJsonElement).</summary>
        internal static object RawUnion(Type union, string json)
        {
            if (IsStj)
            {
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
                return union.GetMethod("FromJsonElement")!.Invoke(null, new object[] { document.RootElement })!;
            }

            return union.GetMethod("FromJToken")!.Invoke(null, new object[] { JToken.Parse(json) })!;
        }

        /// <summary>The raw JSON form of a union without a discriminator (ToJToken / ToJsonElement), as compact text.</summary>
        internal static string RawJson(object union)
        {
            object? raw = union.GetType().GetMethod(IsStj ? "ToJsonElement" : "ToJToken", Type.EmptyTypes)!.Invoke(union, null);
            return SerializerMatrixHarness.JsonText(raw);
        }

        /// <summary>The compact JSON text of a value that the generated type exposes as Any (JToken / JsonElement).</summary>
        internal static string AnyJson(object? value)
        {
            return SerializerMatrixHarness.JsonText(value);
        }

        /// <summary>An Any value parsed from JSON text.</summary>
        internal static object? Any(string json)
        {
            return SerializerMatrixHarness.MakeAny(Current, json);
        }

        /// <summary>A FreeObject value parsed from JSON text.</summary>
        internal static object FreeObject(string json)
        {
            return SerializerMatrixHarness.MakeFreeObject(Current, json);
        }

        /// <summary>A list of JSON values; it becomes a List of JToken / JsonElement (the parameter type) when the client is called.</summary>
        internal sealed class JsonList
        {
            internal JsonList(params string[] jsons)
            {
                Jsons = jsons;
            }

            internal string[] Jsons { get; }
        }

        /// <summary>Replaces the JsonList arguments by lists of the element type that the parameter declares.</summary>
        internal static object?[] ConvertArguments(MethodInfo method, object?[] arguments)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var converted = new object?[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
            {
                if (arguments[index] is JsonList list)
                {
                    Type listType = parameters[index].ParameterType;
                    var instance = (IList)Activator.CreateInstance(listType)!;
                    foreach (string json in list.Jsons)
                    {
                        instance.Add(ParseAny(json));
                    }

                    converted[index] = instance;
                }
                else
                {
                    converted[index] = arguments[index];
                }
            }

            return converted;
        }

        private static object? ParseAny(string json)
        {
            return SerializerMatrixHarness.MakeAny(Current, json);
        }

        /// <summary>An extension data dictionary (IDictionary&lt;string, JToken / JsonElement&gt;) of the given JSON members.</summary>
        internal static object ExtensionDictionary(params (string Name, string Json)[] members)
        {
            if (IsStj)
            {
                var stj = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>();
                foreach ((string name, string json) in members)
                {
                    stj[name] = (System.Text.Json.JsonElement)SerializerMatrixHarness.MakeAny(Current, json)!;
                }

                return stj;
            }

            var newtonsoft = new System.Collections.Generic.Dictionary<string, JToken>();
            foreach ((string name, string json) in members)
            {
                newtonsoft[name] = JToken.Parse(json);
            }

            return newtonsoft;
        }

        /// <summary>The members of an extension data dictionary (IDictionary&lt;string, JToken / JsonElement&gt;) as compact JSON texts.</summary>
        internal static System.Collections.Generic.Dictionary<string, string> ExtensionJson(object? dictionary)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in (IDictionary)dictionary!)
            {
                result[(string)entry.Key] = SerializerMatrixHarness.JsonText(entry.Value);
            }

            return result;
        }

    }
}
