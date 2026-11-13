using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Invokes a generated client method through reflection against a fake HttpMessageHandler (204
    /// response) and returns what was sent: the request URI and the request headers.
    /// </summary>
    internal static class Phase4WireHarness
    {
        internal const string Ns = "Generated.Phase4.";

        internal static Type ClientType(Assembly assembly)
        {
            return assembly.GetType(Ns + "Phase4Api", throwOnError: true)!;
        }

        internal static object CreateClient(Assembly assembly, HttpClient httpClient, string? baseUrl = null)
        {
            Type type = ClientType(assembly);
            return baseUrl is null
                ? Activator.CreateInstance(type, httpClient)!
                : Activator.CreateInstance(type, httpClient, baseUrl)!;
        }

        internal static Task<WireRequest> Send(Assembly assembly, string methodName, params object?[] arguments)
        {
            return SendWith(assembly, methodName, null, null, arguments);
        }

        /// <summary>
        /// Calls a method. Arguments not supplied are filled with default values (the cancellation
        /// token with None). configure runs on the client before the call.
        /// </summary>
        internal static async Task<WireRequest> SendWith(
            Assembly assembly,
            string methodName,
            string? baseUrl,
            Action<object>? configure,
            params object?[] arguments)
        {
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient, baseUrl);
            configure?.Invoke(client);
            MethodInfo method = ClientType(assembly).GetMethod(methodName)!;
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

            try
            {
                await ((Task)method.Invoke(client, full)!).ConfigureAwait(false);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }

            return handler.Last!;
        }

        internal sealed class WireRequest
        {
            internal WireRequest(Uri uri, Dictionary<string, string> headers, List<string> rawCookieHeaders)
            {
                Uri = uri;
                Headers = headers;
                RawCookieHeaders = rawCookieHeaders;
            }

            internal Uri Uri { get; }

            internal Dictionary<string, string> Headers { get; }

            internal List<string> RawCookieHeaders { get; }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            internal WireRequest? Last { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in request.Headers)
                {
                    headers[header.Key] = string.Join(",", header.Value);
                }

                List<string> cookies = request.Headers.TryGetValues("Cookie", out IEnumerable<string>? values)
                    ? values.ToList()
                    : new List<string>();
                Last = new WireRequest(request.RequestUri!, headers, cookies);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        }
    }
}
