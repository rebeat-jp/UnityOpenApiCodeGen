using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// A single thread with a SynchronizationContext, standing in for the Unity main thread: everything posted
    /// to it runs on that one thread, one item at a time (like UnitySynchronizationContext).
    /// </summary>
    internal sealed class MainThreadPump : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue =
            new BlockingCollection<(SendOrPostCallback, object?)>();

        private readonly Thread _thread;

        internal MainThreadPump()
        {
            var started = new ManualResetEventSlim();
            int id = 0;
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                id = Environment.CurrentManagedThreadId;
                started.Set();
                foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            })
            {
                IsBackground = true,
                Name = "TestMainThread"
            };
            _thread.Start();
            started.Wait();
            ThreadId = id;
        }

        internal int ThreadId { get; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Add((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Environment.CurrentManagedThreadId == ThreadId)
            {
                d(state);
                return;
            }

            using var done = new ManualResetEventSlim();
            Exception? failure = null;
            Post(_ =>
            {
                try
                {
                    d(state);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                done.Set();
            }, null);
            done.Wait();
            if (failure is not null)
            {
                throw failure;
            }
        }

        /// <summary>Starts the work on the pump thread; its continuations stay there.</summary>
        internal Task<T> RunAsync<T>(Func<Task<T>> work)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(
                async _ =>
                {
                    try
                    {
                        completion.SetResult(await work());
                    }
                    catch (Exception exception)
                    {
                        completion.SetException(exception);
                    }
                },
                null);
            return completion.Task;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
        }
    }

    /// <summary>What one call of a generated client through the UnityWebRequest stub (or an HttpClient) produced.</summary>
    internal sealed class TransportOutcome
    {
        internal object? Result { get; set; }

        internal Exception? Error { get; set; }

        internal Phase4BodyWireHarness.Exchange? Request { get; set; }

        /// <summary>"event:managed thread id" of the UnityWebRequest calls, in order.</summary>
        internal List<string> Log { get; } = new List<string>();

        internal IReadOnlyList<int> ThreadsOf(string eventName)
        {
            lock (Log)
            {
                return Log.Where(entry => entry.StartsWith(eventName + ":", StringComparison.Ordinal))
                    .Select(static entry => int.Parse(entry.Substring(entry.IndexOf(':') + 1), CultureInfo.InvariantCulture))
                    .ToArray();
            }
        }

        internal bool Has(string eventName)
        {
            return ThreadsOf(eventName).Count > 0;
        }
    }

    /// <summary>A scripted, recording HttpMessageHandler: the "network" behind both transports.</summary>
    internal sealed class RecordingTransportHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        internal RecordingTransportHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        internal Phase4BodyWireHarness.Exchange? Last { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var exchange = new Phase4BodyWireHarness.Exchange { Method = request.Method.Method, Uri = request.RequestUri };
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
            return await _respond(request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Compiles clients for the UnityWebRequest transport against the UnityEngine stub and calls them through
    /// reflection; the same call through an HttpClient client is the reference of the parity tests.
    /// </summary>
    internal static class UnityWebRequestHarness
    {
        private static readonly ConcurrentDictionary<string, Lazy<Assembly>> Compiled =
            new ConcurrentDictionary<string, Lazy<Assembly>>();

        /// <summary>The client compiled from ApiSurfaceSpecs/<paramref name="specFile"/> for UnityWebRequest (cached).</summary>
        internal static Assembly Compile(string specFile, string serializer)
        {
            return Compiled.GetOrAdd(
                specFile + "|" + serializer,
                _ => new Lazy<Assembly>(() => CompileText(
                    System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", specFile)),
                    serializer))).Value;
        }

        internal static Assembly CompileInline(string key, string spec, string serializer)
        {
            return Compiled.GetOrAdd(
                "inline:" + key + "|" + serializer,
                _ => new Lazy<Assembly>(() => CompileText(spec, serializer))).Value;
        }

        private static Assembly CompileText(string spec, string serializer)
        {
            // The stub assembly has to be loaded before the generated assembly that references it.
            UnityEngineStub.Load();
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec,
                options: new Phase4CompilationOptions
                {
                    UnityWebRequestStub = true,
                    HttpTransport = "UnityWebRequest",
                    JsonSerializer = serializer,
                    Newtonsoft = !SerializerMatrixHarness.IsStj(serializer)
                });
            if (execution.RunResult.Diagnostics.Length > 0 || execution.CompilationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "UnityWebRequest (" + serializer + "): " +
                    string.Join("; ", execution.RunResult.Diagnostics.Select(static value => value.ToString())) +
                    string.Join("; ", execution.CompilationErrors.Select(static value => value.ToString())));
            }

            return execution.EmitAssembly();
        }

        internal static object CreateClient(Assembly assembly, Uri? baseAddress, string? baseUrl = null)
        {
            Type type = Phase4WireHarness.ClientType(assembly);
            return baseUrl is null
                ? Activator.CreateInstance(type, new object?[] { baseAddress })!
                : Activator.CreateInstance(type, new object?[] { baseAddress, baseUrl })!;
        }

        /// <summary>A delegate of the Action&lt;UnityWebRequest&gt; type of the stub that runs <paramref name="action"/>.</summary>
        internal static Delegate MakeConfigure(Action<object> action)
        {
            Type requestType = UnityEngineStub.RequestType;
            Type delegateType = typeof(Action<>).MakeGenericType(requestType);
            ParameterExpression parameter = Expression.Parameter(requestType, "request");
            return Expression.Lambda(
                delegateType,
                Expression.Invoke(Expression.Constant(action), Expression.Convert(parameter, typeof(object))),
                parameter).Compile();
        }

        internal static object? Read(object stubRequest, string property)
        {
            return stubRequest.GetType().GetProperty(property)!.GetValue(stubRequest);
        }

        private static void InstallStub(RecordingTransportHandler handler, List<string> log)
        {
            Type requestType = UnityEngineStub.RequestType;
            requestType.GetProperty("Handler")!.SetValue(null, handler);
            requestType.GetProperty("Log")!.SetValue(null, log);
        }

        /// <summary>
        /// Calls a method of a generated client. With <paramref name="unityWebRequest"/> the client is a UnityWebRequest
        /// client that sends through the stub (and the handler); otherwise it is an HttpClient client. An exception of
        /// the call is returned in the outcome. With a pump the call (and so every UnityWebRequest call) starts on
        /// the pump thread.
        /// </summary>
        internal static async Task<TransportOutcome> Call(
            Assembly assembly,
            bool unityWebRequest,
            string methodName,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
            object?[] arguments,
            Action<object>? configure = null,
            Uri? baseAddress = null,
            string? baseUrl = null,
            Action<Type>? stubSetup = null,
            MainThreadPump? pump = null)
        {
            var handler = new RecordingTransportHandler(respond);
            var outcome = new TransportOutcome();
            using var httpClient = unityWebRequest ? null : new HttpClient(handler);
            object client = unityWebRequest
                ? CreateClient(assembly, baseAddress, baseUrl)
                : Phase4WireHarness.CreateClient(assembly, httpClient!, baseUrl);
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

            async Task<bool> Invoke()
            {
                if (unityWebRequest)
                {
                    InstallStub(handler, outcome.Log);
                    stubSetup?.Invoke(UnityEngineStub.RequestType);
                }

                configure?.Invoke(client);
                try
                {
                    var task = (Task)method.Invoke(client, full)!;
                    await task;
                    PropertyInfo? resultProperty = task.GetType().GetProperty("Result");
                    if (resultProperty is not null && resultProperty.PropertyType.Name != "VoidTaskResult")
                    {
                        outcome.Result = resultProperty.GetValue(task);
                    }
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    outcome.Error = exception.InnerException;
                }
                catch (Exception exception)
                {
                    outcome.Error = exception;
                }

                return true;
            }

            await (pump is null ? Invoke() : pump.RunAsync(Invoke));
            outcome.Request = handler.Last;
            return outcome;
        }

        internal static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(
            Func<HttpResponseMessage> response)
        {
            return (_, _) => Task.FromResult(response());
        }

        // ---- comparing outcomes --------------------------------------------------------------------------

        /// <summary>A text that two equal values of the two transports share (the assemblies differ, so types are named).</summary>
        internal static string Describe(object? value, int depth = 0)
        {
            switch (value)
            {
                case null:
                    return "null";
                case string text:
                    return "\"" + text + "\"";
                case bool flag:
                    return flag ? "true" : "false";
                case byte[] bytes:
                    return "bytes:" + Convert.ToBase64String(bytes);
                case Exception exception:
                    return exception.GetType().Name + ":" + exception.Message;
                case DateTime dateTime:
                    return dateTime.ToString("O", CultureInfo.InvariantCulture);
                case DateTimeOffset dateTimeOffset:
                    return dateTimeOffset.ToString("O", CultureInfo.InvariantCulture);
                case Guid guid:
                    return guid.ToString();
                case Enum enumValue:
                    return enumValue.GetType().Name + "." + enumValue;
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                case Newtonsoft.Json.Linq.JToken:
                case System.Text.Json.JsonElement:
                case System.Text.Json.Nodes.JsonNode:
                    return SerializerMatrixHarness.JsonText(value);
            }

            Type type = value.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                return Describe(type.GetProperty("Key")!.GetValue(value), depth + 1) + "=" +
                       Describe(type.GetProperty("Value")!.GetValue(value), depth + 1);
            }

            if (value is IEnumerable enumerable)
            {
                var items = new List<string>();
                bool sort = false;
                foreach (object? item in enumerable)
                {
                    if (item is not null && item.GetType().IsGenericType &&
                        item.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                    {
                        // A dictionary (response headers): order-free, names are case-insensitive, and the length of
                        // the body is not part of the contract.
                        sort = true;
                        string key = Convert.ToString(item.GetType().GetProperty("Key")!.GetValue(item), CultureInfo.InvariantCulture)!;
                        if (string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        object? entry = item.GetType().GetProperty("Value")!.GetValue(item);
                        items.Add(key.ToLowerInvariant() + "=" + NormalizeHeaderValue(Describe(entry, depth + 1)));
                    }
                    else
                    {
                        items.Add(Describe(item, depth + 1));
                    }
                }

                if (sort)
                {
                    items.Sort(StringComparer.Ordinal);
                }

                return "[" + string.Join(",", items) + "]";
            }

            if (depth > 4)
            {
                return type.Name;
            }

            var members = new List<string>();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(static property => property.GetIndexParameters().Length == 0)
                         .OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                object? member;
                try
                {
                    member = property.GetValue(value);
                }
                catch (TargetInvocationException)
                {
                    continue;
                }

                members.Add(property.Name + "=" + Describe(member, depth + 1));
            }

            return type.Name + "{" + string.Join(";", members) + "}";
        }

        /// <summary>HttpClient joins the values of a header with "," and UnityWebRequest with ", " (or "; " for Cookie).</summary>
        internal static string NormalizeHeaderValue(string value)
        {
            return string.Join(
                "|",
                value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(static part => part.Trim()));
        }

        internal static string DescribeHeaders(Dictionary<string, string> headers, bool dropContentLength)
        {
            return string.Join(
                "\n",
                headers
                    .Where(pair => !(dropContentLength && string.Equals(pair.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)))
                    .Select(static pair => pair.Key.ToLowerInvariant() + ": " + NormalizeHeaderValue(
                        System.Text.RegularExpressions.Regex.Replace(pair.Value, "boundary=\"?[^\";]+\"?", "boundary=B")))
                    .OrderBy(static line => line, StringComparer.Ordinal));
        }

        internal static string DescribeRequest(Phase4BodyWireHarness.Exchange request)
        {
            var text = new StringBuilder();
            text.Append(request.Method).Append(' ').Append(request.Uri).Append('\n');
            text.Append(DescribeHeaders(request.Headers, dropContentLength: true)).Append('\n');
            text.Append("--content headers\n");
            text.Append(DescribeHeaders(request.ContentHeaders, dropContentLength: true)).Append('\n');
            text.Append("--body\n");
            if (request.ContentType != null &&
                request.ContentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Phase4BodyWireHarness.Part part in Phase4BodyWireHarness.ReadMultipart(request, out _))
                {
                    text.Append("part ").Append(DescribeHeaders(part.Headers, dropContentLength: false).Replace("\n", " | "))
                        .Append(" => ").Append(part.Body).Append('\n');
                }
            }
            else
            {
                text.Append(Convert.ToBase64String(request.Body));
            }

            return text.ToString();
        }
    }
}
