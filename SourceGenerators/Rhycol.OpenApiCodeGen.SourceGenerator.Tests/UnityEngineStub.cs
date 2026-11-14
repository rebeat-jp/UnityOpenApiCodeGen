using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// A stand-in for the UnityEngine.UnityWebRequestModule API that the generated UnityWebRequest client
    /// uses (UnityWebRequest, UploadHandlerRaw, DownloadHandlerBuffer, UnityWebRequestAsyncOperation, Result).
    /// It is compiled in memory, so a test can compile the generated client against it and, because the
    /// stub really "sends" through a System.Net.Http.HttpMessageHandler, also run the client.
    /// The stub mimics what the Unity player does that matters to the client: it completes the operation on
    /// the SynchronizationContext it was started on (the Unity main thread), merges repeated response
    /// headers, reports a STATUS pseudo header, refuses the headers that the platform owns, answers
    /// 4xx/5xx as ProtocolError and a timeout or an abort as ConnectionError. Every call is recorded with the
    /// managed thread id in UnityWebRequest.Log so a test can prove that the client stays on the main thread.
    /// </summary>
    internal static class UnityEngineStub
    {
        internal const string AssemblyName = "UnityEngine.UnityWebRequestModule";

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, byte[]> Images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private static Assembly? _loaded;

        internal const string Source = @"
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

[assembly: System.Reflection.AssemblyVersion(""0.0.0.0"")]

namespace UnityEngine
{
    public class AsyncOperation
    {
        private readonly object _gate = new object();
        private Action<AsyncOperation> _completed;

        public bool isDone { get; private set; }

        // Like the real one: a handler that is added after the operation has finished runs at once.
        public event Action<AsyncOperation> completed
        {
            add
            {
                bool runNow;
                lock (_gate)
                {
                    runNow = isDone;
                    if (!runNow) { _completed += value; }
                }

                if (runNow) { value(this); }
            }
            remove
            {
                lock (_gate) { _completed -= value; }
            }
        }

        protected void RaiseCompleted()
        {
            Action<AsyncOperation> handlers;
            lock (_gate)
            {
                isDone = true;
                handlers = _completed;
                _completed = null;
            }

            if (handlers != null) { handlers(this); }
        }
    }
}

namespace UnityEngine.Networking
{
    public abstract class DownloadHandler : IDisposable
    {
        public byte[] data { get { return GetData(); } }

        protected abstract byte[] GetData();

        public virtual void Dispose() { }
    }

    public class DownloadHandlerBuffer : DownloadHandler
    {
        internal byte[] Buffer = new byte[0];

        protected override byte[] GetData() { return Buffer; }
    }

    public abstract class UploadHandler : IDisposable
    {
        public string contentType { get; set; }

        public byte[] data { get { return GetData(); } }

        protected abstract byte[] GetData();

        public virtual void Dispose() { }
    }

    public class UploadHandlerRaw : UploadHandler
    {
        private readonly byte[] _data;

        public UploadHandlerRaw(byte[] data)
        {
            _data = data;
            contentType = ""application/octet-stream"";
        }

        protected override byte[] GetData() { return _data; }
    }

    public class UnityWebRequestAsyncOperation : AsyncOperation
    {
        public UnityWebRequest webRequest { get; internal set; }

        internal void Finish() { RaiseCompleted(); }
    }

    public class UnityWebRequest : IDisposable
    {
        public enum Result
        {
            InProgress,
            Success,
            ConnectionError,
            ProtocolError,
            DataProcessingError
        }

        private static readonly AsyncLocal<HttpMessageHandler> HandlerLocal = new AsyncLocal<HttpMessageHandler>();
        private static readonly AsyncLocal<List<string>> LogLocal = new AsyncLocal<List<string>>();
        private static readonly AsyncLocal<string> TimeoutTextLocal = new AsyncLocal<string>();
        private static readonly AsyncLocal<string> UppercaseLocal = new AsyncLocal<string>();

        private static readonly string[] OwnedHeaders =
        {
            ""Content-Length"", ""Host"", ""Connection"", ""Transfer-Encoding"", ""Expect"", ""Date"", ""Via"", ""Upgrade"", ""TE""
        };

        private readonly HttpMessageHandler _handler;
        private readonly List<string> _log;
        private readonly string _timeoutText;
        private readonly bool _uppercase;
        private readonly List<KeyValuePair<string, string>> _requestHeaders = new List<KeyValuePair<string, string>>();
        private CancellationTokenSource _cancellation;
        private bool _aborted;
        private bool _disposed;
        private Dictionary<string, string> _responseHeaders;

        /// <summary>The handler that answers the requests started in the current async flow.</summary>
        public static HttpMessageHandler Handler
        {
            get { return HandlerLocal.Value; }
            set { HandlerLocal.Value = value; }
        }

        /// <summary>Records ""event:thread id"" of the calls made by the requests created in the current async flow.</summary>
        public static List<string> Log
        {
            get { return LogLocal.Value; }
            set { LogLocal.Value = value; }
        }

        /// <summary>Replaces the error text of a timeout (default ""Request timeout"").</summary>
        public static string TimeoutText
        {
            get { return TimeoutTextLocal.Value; }
            set { TimeoutTextLocal.Value = value; }
        }

        /// <summary>""1"": response header names are reported in upper case (some platforms do).</summary>
        public static string UppercaseResponseHeaderNames
        {
            get { return UppercaseLocal.Value; }
            set { UppercaseLocal.Value = value; }
        }

        public UnityWebRequest(string url, string method)
        {
            _timeoutText = TimeoutText;
            _uppercase = UppercaseResponseHeaderNames == ""1"";
            this.url = url;
            this.method = method;
            _handler = Handler;
            _log = Log;
            result = Result.InProgress;
            Note(""ctor"");
        }

        public string url { get; set; }

        public string method { get; set; }

        public int timeout { get; set; }

        public UploadHandler uploadHandler { get; set; }

        public DownloadHandler downloadHandler { get; set; }

        public Result result { get; private set; }

        public long responseCode { get; private set; }

        public string error { get; private set; }

        public bool isDone { get; private set; }

        private void Note(string what)
        {
            if (_log != null)
            {
                lock (_log) { _log.Add(what + "":"" + Environment.CurrentManagedThreadId); }
            }
        }

        public void SetRequestHeader(string name, string value)
        {
            Note(""header"");
            foreach (string owned in OwnedHeaders)
            {
                if (string.Equals(owned, name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(""Cannot override system-specified headers"");
                }
            }

            _requestHeaders.RemoveAll(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase));
            _requestHeaders.Add(new KeyValuePair<string, string>(name, value));
        }

        public Dictionary<string, string> GetResponseHeaders() { return _responseHeaders; }

        public UnityWebRequestAsyncOperation SendWebRequest()
        {
            Note(""send"");
            if (_disposed) { throw new ObjectDisposedException(nameof(UnityWebRequest)); }

            var operation = new UnityWebRequestAsyncOperation { webRequest = this };
            var context = SynchronizationContext.Current;
            _cancellation = new CancellationTokenSource();
            if (timeout > 0) { _cancellation.CancelAfter(TimeSpan.FromSeconds(timeout)); }

            var token = _cancellation.Token;
            HttpRequestMessage request = BuildRequest();
            Task.Run(async () =>
            {
                try
                {
                    if (_handler == null) { throw new InvalidOperationException(""No test handler is installed.""); }

                    using (var invoker = new HttpMessageInvoker(_handler, false))
                    using (HttpResponseMessage response = await invoker.SendAsync(request, token).ConfigureAwait(false))
                    {
                        await ReadAsync(response).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    result = Result.ConnectionError;
                    error = _aborted ? ""Request aborted"" : (_timeoutText ?? ""Request timeout"");
                }
                catch (Exception exception)
                {
                    result = Result.ConnectionError;
                    error = exception.Message;
                }
                finally
                {
                    request.Dispose();
                }

                // The player completes on the main thread.
                if (context != null) { context.Post(_ => Complete(operation), null); }
                else { Complete(operation); }
            });
            return operation;
        }

        private void Complete(UnityWebRequestAsyncOperation operation)
        {
            isDone = true;
            operation.Finish();
        }

        private HttpRequestMessage BuildRequest()
        {
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            byte[] body = uploadHandler == null ? null : uploadHandler.data;
            HttpContent content = body == null ? null : new ByteArrayContent(body);
            string contentType = uploadHandler == null ? null : uploadHandler.contentType;
            foreach (var header in _requestHeaders)
            {
                if (string.Equals(header.Key, ""Content-Type"", StringComparison.OrdinalIgnoreCase)) { contentType = header.Value; }
            }

            if (content == null && contentType != null && _requestHeaders.Exists(h => h.Key.StartsWith(""Content-"", StringComparison.OrdinalIgnoreCase)))
            {
                content = new ByteArrayContent(new byte[0]);
            }

            if (content != null && contentType != null)
            {
                content.Headers.TryAddWithoutValidation(""Content-Type"", contentType);
            }

            foreach (var header in _requestHeaders)
            {
                if (string.Equals(header.Key, ""Content-Type"", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (header.Key.StartsWith(""Content-"", StringComparison.OrdinalIgnoreCase) && content != null)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                else if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value) && content != null)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            request.Content = content;
            return request;
        }

        private async Task ReadAsync(HttpResponseMessage response)
        {
            byte[] data = response.Content == null ? new byte[0] : await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            headers[""STATUS""] = ""HTTP/1.1 "" + (int)response.StatusCode + "" "" + response.ReasonPhrase;
            foreach (var header in response.Headers) { Merge(headers, header.Key, header.Value); }
            if (response.Content != null)
            {
                foreach (var header in response.Content.Headers) { Merge(headers, header.Key, header.Value); }
                headers[""Content-Length""] = data.Length.ToString();
            }

            var buffer = downloadHandler as DownloadHandlerBuffer;
            if (buffer != null) { buffer.Buffer = data; }

            if (_uppercase)
            {
                var upper = new Dictionary<string, string>();
                foreach (var pair in headers) { upper[pair.Key.ToUpperInvariant()] = pair.Value; }
                headers = new Dictionary<string, string>(upper, StringComparer.Ordinal);
            }

            _responseHeaders = headers;
            responseCode = (int)response.StatusCode;
            result = responseCode >= 400 ? Result.ProtocolError : Result.Success;
        }

        // The player joins the values of a repeated header into one.
        private static void Merge(Dictionary<string, string> headers, string name, IEnumerable<string> values)
        {
            string joined = string.Join("", "", values);
            headers[name] = headers.TryGetValue(name, out string existing) ? existing + "", "" + joined : joined;
        }

        public void Abort()
        {
            Note(""abort"");
            _aborted = true;
            if (_cancellation != null) { _cancellation.Cancel(); }
        }

        public void Dispose()
        {
            Note(""dispose"");
            _disposed = true;
            if (uploadHandler != null) { uploadHandler.Dispose(); }
            if (downloadHandler != null) { downloadHandler.Dispose(); }
        }
    }
}
";

        /// <summary>
        /// The stub compiled against the given reference set (null: the running runtime). The result is cached
        /// per reference set, so every compilation of one set shares the same image.
        /// </summary>
        internal static MetadataReference CreateReference(IReadOnlyList<string>? referencePaths)
        {
            return MetadataReference.CreateFromImage(GetImage(referencePaths));
        }

        private static byte[] GetImage(IReadOnlyList<string>? referencePaths)
        {
            string key = referencePaths is null ? "runtime" : string.Join("|", referencePaths);
            lock (Gate)
            {
                if (Images.TryGetValue(key, out byte[]? cached))
                {
                    return cached;
                }

                IEnumerable<string> paths = referencePaths ??
                    ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty).Split(Path.PathSeparator);
                CSharpCompilation compilation = CSharpCompilation.Create(
                    AssemblyName,
                    new[]
                    {
                        CSharpSyntaxTree.ParseText(Source)
                    },
                    paths.Where(static path => !string.IsNullOrWhiteSpace(path))
                        .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)),
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using var stream = new MemoryStream();
                Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
                if (!result.Success)
                {
                    throw new InvalidOperationException(string.Join(
                        Environment.NewLine,
                        result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
                }

                byte[] image = stream.ToArray();
                Images[key] = image;
                return image;
            }
        }

        /// <summary>
        /// The stub assembly that generated clients run against. It is compiled against the running runtime and
        /// loaded into the default context; the context resolves the stub by name for the generated assemblies.
        /// </summary>
        internal static Assembly Load()
        {
            lock (Gate)
            {
                if (_loaded is not null)
                {
                    return _loaded;
                }

                byte[] image = GetImage(null);
                _loaded = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(image));
                AssemblyLoadContext.Default.Resolving += (context, name) =>
                    string.Equals(name.Name, AssemblyName, StringComparison.Ordinal) ? _loaded : null;
                return _loaded;
            }
        }

        internal static Type RequestType => Load().GetType("UnityEngine.Networking.UnityWebRequest", throwOnError: true)!;
    }
}
