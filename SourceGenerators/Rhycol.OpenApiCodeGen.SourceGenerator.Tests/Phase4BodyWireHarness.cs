using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Wire harness for P4 (bodies, responses, headers, errors). It extends Phase4WireHarness (client
    /// creation by reflection) with a scripted response and a record of the complete request: headers,
    /// content headers and the body bytes.
    /// </summary>
    internal static class Phase4BodyWireHarness
    {
        private static readonly Lazy<Assembly> BodiesAssembly = new Lazy<Assembly>(() => CompileBodies(null));

        private static readonly Lazy<Assembly> BodiesSystemTextJsonAssembly = new Lazy<Assembly>(
            () => CompileBodies("SystemTextJson"));

        private static Assembly CompileBodies(string? serializer)
        {
            string spec = System.IO.File.ReadAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", "bodies.json"));
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec,
                options: serializer is null ? null : new Phase4CompilationOptions { JsonSerializer = serializer });
            if (execution.RunResult.Diagnostics.Length > 0 || execution.CompilationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "bodies.json: " + string.Join("; ", execution.RunResult.Diagnostics.Select(static value => value.ToString())) +
                    string.Join("; ", execution.CompilationErrors.Select(static value => value.ToString())));
            }

            return execution.EmitAssembly();
        }

        /// <summary>The client compiled from ApiSurfaceSpecs/bodies.json (compiled once).</summary>
        internal static Assembly Bodies => BodiesAssembly.Value;

        /// <summary>The same client compiled for a JSON serializer ("NewtonsoftJson" or "SystemTextJson").</summary>
        internal static Assembly BodiesFor(string serializer) =>
            serializer == "SystemTextJson" ? BodiesSystemTextJsonAssembly.Value : Bodies;

        internal sealed class Exchange
        {
            internal string Method { get; set; } = string.Empty;

            internal Uri? Uri { get; set; }

            internal Dictionary<string, string> Headers { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            internal Dictionary<string, string> ContentHeaders { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            internal byte[] Body { get; set; } = Array.Empty<byte>();

            /// <summary>The body decoded as ISO-8859-1 so that every byte survives (ASCII payloads read as text).</summary>
            internal string BodyText => Encoding.GetEncoding("ISO-8859-1").GetString(Body);

            internal string? ContentType => ContentHeaders.TryGetValue("Content-Type", out string? value) ? value : null;
        }

        internal static HttpResponseMessage Respond(
            HttpStatusCode status,
            string? body = null,
            string mediaType = "application/json",
            params (string Name, string Value)[] headers)
        {
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                response.Content = new StringContent(body, Encoding.UTF8);
                response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mediaType);
            }

            foreach ((string name, string value) in headers)
            {
                if (!response.Headers.TryAddWithoutValidation(name, value))
                {
                    response.Content ??= new ByteArrayContent(Array.Empty<byte>());
                    response.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }

            return response;
        }

        internal static HttpResponseMessage RespondBytes(
            HttpStatusCode status,
            byte[] body,
            string mediaType)
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            return response;
        }

        /// <summary>Calls a method with a scripted response and returns its result and the request that was sent.</summary>
        internal static async Task<(object? Result, Exchange Request)> Call(
            string methodName,
            HttpResponseMessage response,
            params object?[] arguments)
        {
            return await Call(Bodies, methodName, _ => response, arguments).ConfigureAwait(false);
        }

        internal static async Task<(object? Result, Exchange Request)> Call(
            Assembly assembly,
            string methodName,
            Func<HttpRequestMessage, HttpResponseMessage> respond,
            params object?[] arguments)
        {
            var handler = new ScriptedHandler(respond);
            using var httpClient = new HttpClient(handler);
            object client = Phase4WireHarness.CreateClient(assembly, httpClient);
            MethodInfo method = Phase4WireHarness.ClientType(assembly).GetMethod(methodName)
                                ?? throw new MissingMethodException(methodName);
            ParameterInfo[] parameters = method.GetParameters();
            var full = new object?[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                full[index] = index < arguments.Length
                    ? arguments[index]
                    : parameters[index].ParameterType == typeof(CancellationToken)
                        ? CancellationToken.None
                        : parameters[index].HasDefaultValue ? parameters[index].DefaultValue : null;
            }

            object? result = null;
            try
            {
                var task = (Task)method.Invoke(client, full)!;
                await task.ConfigureAwait(false);
                PropertyInfo? resultProperty = task.GetType().GetProperty("Result");
                if (resultProperty is not null && resultProperty.PropertyType.Name != "VoidTaskResult")
                {
                    result = resultProperty.GetValue(task);
                }
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }

            return (result, handler.Last!);
        }

        internal static Type TypeOf(string name)
        {
            return Bodies.GetType(Phase4WireHarness.Ns + name, throwOnError: true)!;
        }

        /// <summary>Creates a generated DTO and sets its properties by name.</summary>
        internal static object New(string typeName, params (string Property, object? Value)[] values)
        {
            Type type = TypeOf(typeName);
            object instance = Activator.CreateInstance(type)!;
            foreach ((string property, object? value) in values)
            {
                type.GetProperty(property)!.SetValue(instance, value);
            }

            return instance;
        }

        internal static T Get<T>(object instance, string property)
        {
            return (T)instance.GetType().GetProperty(property)!.GetValue(instance)!;
        }

        internal static object? GetOrNull(object instance, string property)
        {
            return instance.GetType().GetProperty(property)!.GetValue(instance);
        }

        /// <summary>Creates the generated file part type from a byte array or a stream.</summary>
        internal static object NewFile(object data, string? fileName = null, string? contentType = null)
        {
            Type type = TypeOf("Phase4ApiFileParameter");
            return Activator.CreateInstance(type, data, fileName, contentType)!;
        }

        /// <summary>One part of a multipart body.</summary>
        internal sealed class Part
        {
            internal Dictionary<string, string> Headers { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            internal string Body { get; set; } = string.Empty;

            internal string Disposition => Headers.TryGetValue("Content-Disposition", out string? value) ? value : string.Empty;

            internal string? ContentType => Headers.TryGetValue("Content-Type", out string? value) ? value : null;
        }

        /// <summary>Splits a multipart/form-data request into its parts using the boundary of its Content-Type.</summary>
        internal static List<Part> ReadMultipart(Exchange exchange, out string boundary)
        {
            string contentType = exchange.ContentType ?? throw new InvalidOperationException("No Content-Type.");
            var parsed = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
            boundary = parsed.Parameters.First(static value => value.Name == "boundary").Value!.Trim('"');
            var parts = new List<Part>();
            string text = exchange.BodyText;
            string delimiter = "--" + boundary;
            foreach (string chunk in text.Split(new[] { delimiter }, StringSplitOptions.None).Skip(1))
            {
                if (chunk.StartsWith("--", StringComparison.Ordinal))
                {
                    break;
                }

                string trimmed = chunk.StartsWith("\r\n", StringComparison.Ordinal) ? chunk.Substring(2) : chunk;
                int split = trimmed.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                var part = new Part();
                string headerText = split < 0 ? trimmed : trimmed.Substring(0, split);
                string bodyText = split < 0 ? string.Empty : trimmed.Substring(split + 4);
                if (bodyText.EndsWith("\r\n", StringComparison.Ordinal))
                {
                    bodyText = bodyText.Substring(0, bodyText.Length - 2);
                }

                foreach (string line in headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = line.IndexOf(':');
                    part.Headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                }

                part.Body = bodyText;
                parts.Add(part);
            }

            return parts;
        }

        private sealed class ScriptedHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            internal ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            internal Exchange? Last { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var exchange = new Exchange { Method = request.Method.Method, Uri = request.RequestUri };
                foreach (var header in request.Headers)
                {
                    exchange.Headers[header.Key] = string.Join(",", header.Value);
                }

                if (request.Content is not null)
                {
                    foreach (var header in request.Content.Headers)
                    {
                        exchange.ContentHeaders[header.Key] = string.Join(",", header.Value);
                    }

                    exchange.Body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }

                Last = exchange;
                return _respond(request);
            }
        }
    }
}
