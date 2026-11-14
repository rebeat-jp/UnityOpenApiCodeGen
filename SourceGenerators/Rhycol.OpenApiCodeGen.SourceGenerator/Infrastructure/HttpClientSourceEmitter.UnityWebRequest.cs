using System;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// 生成クライアントの UnityWebRequest トランスポート（アダプタ方式）。クライアントは従来どおり
    /// System.Net.Http.HttpRequestMessage / HttpContent を組み立て、HttpResponseMessage を読むため、
    /// リクエスト・レスポンス・検証の経路は HttpClient と共有する。違うのは最後の送信だけで、
    /// リクエストを UnityWebRequest に変換し、応答を ByteArrayContent 付きの HttpResponseMessage に戻す。
    /// UnityWebRequest transport of the generated client (adapter style). The client keeps building
    /// System.Net.Http.HttpRequestMessage / HttpContent and reading HttpResponseMessage, so every
    /// request, response and validation path is shared with the HttpClient transport. Only the last step
    /// differs: the request is converted to a UnityWebRequest, and the answer is turned back into an
    /// HttpResponseMessage with a ByteArrayContent.
    /// </summary>
    internal sealed partial class HttpClientSourceEmitter
    {
        private void AppendUnityWebRequestConstructors(StringBuilder source, OpenApiGenerationModel model)
        {
            AppendTemplate(source, @"
        /// <summary>
        /// Creates a client that sends its requests with UnityWebRequest. The server URL of the OpenAPI document is used.
        /// </summary>
        /// <param name=""baseAddress"">
        /// The absolute address that a relative server URL (""/v1"", ""//host/v1"", ""v1"") resolves against; the counterpart of
        /// HttpClient.BaseAddress. It may be null only when the server URL is absolute. UnityWebRequest has no base
        /// address, so a relative server URL without an absolute base address fails when a request is made. On WebGL
        /// pass new Uri(Application.absoluteURL) to resolve against the page that hosts the player.
        /// </param>");
            source.Append("        public ").Append(model.ApiName)
                .Append("(global::System.Uri? baseAddress) : this(baseAddress, ")
                .Append(GeneratedSourceEmitter.StringLiteral(model.BaseUrl))
                .AppendLine(")");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            AppendTemplate(source, @"
        /// <summary>Creates a client that sends its requests with UnityWebRequest and uses <paramref name=""baseUrl""/> as its server URL.</summary>
        /// <param name=""baseAddress"">See the other constructor.</param>
        /// <param name=""baseUrl"">The server URL; absolute, or relative to <paramref name=""baseAddress""/>.</param>");
            source.Append("        public ").Append(model.ApiName)
                .AppendLine("(global::System.Uri? baseAddress, string baseUrl)");
            source.AppendLine("        {");
            source.AppendLine("            if (baseAddress != null && !baseAddress.IsAbsoluteUri)");
            source.AppendLine("            {");
            source.AppendLine("                throw new global::System.ArgumentException(\"The base address must be an absolute URI.\", nameof(baseAddress));");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            _baseAddress = baseAddress;");
            source.AppendLine("            _baseUrl = baseUrl ?? throw new global::System.ArgumentNullException(nameof(baseUrl));");
            source.AppendLine("        }");
        }

        /// <summary>
        /// 公開設定と private のアダプタメンバーを出力する。UnityWebRequest の生成と送信は呼び出し側のコンテキスト
        /// （Unity メインスレッド）で行うため、送信前に ConfigureAwait(false) を使ってはならない。
        /// 送信完了の TaskCompletionSource は RunContinuationsAsynchronously を付けない。コンテキストを捕捉した await
        /// ではどちらでも継続はそのコンテキストへ Post されるが、コンテキストが無い経路ではフラグ付きの継続が
        /// ThreadPool に積まれ、スレッドプールの無い WebGL では走らない恐れがあるため。
        /// The public settings and the private adapter members. Nothing here may call ConfigureAwait(false)
        /// before the UnityWebRequest is created and sent: both must happen on the context of the caller
        /// (the Unity main thread). The completion source has no RunContinuationsAsynchronously: an await that
        /// captured a context resumes there either way, but without a context the flag would queue the
        /// continuation to the thread pool, which WebGL does not have.
        /// </summary>
        private static void AppendUnityWebRequestTransport(StringBuilder source)
        {
            source.AppendLine();
            AppendTemplate(source, @"
        /// <summary>
        /// The longest time to wait for a response. null (the default) waits without a limit. UnityWebRequest takes
        /// whole seconds, so the value is rounded up. A request that runs out of time throws a TaskCanceledException
        /// whose InnerException is a TimeoutException (the exception HttpClient throws for HttpClient.Timeout).
        /// A failed request counts as timed out when the UnityWebRequest error text mentions a timeout or when the time since
        /// the send reached the rounded timeout.
        /// </summary>
        public global::System.TimeSpan? RequestTimeout { get; set; }

        /// <summary>
        /// Called with every UnityWebRequest after the generated code has set it up and right before it is sent: set a
        /// certificate handler, the redirect limit or other options here. It runs on the thread that called the client method.
        /// </summary>
        public global::System.Action<global::UnityEngine.Networking.UnityWebRequest>? ConfigureUnityWebRequest { get; set; }

        /// <summary>
        /// Sends the request with UnityWebRequest and returns the answer as an HttpResponseMessage.
        /// The UnityWebRequest is created, sent and disposed on the context of the caller, which has to be the Unity main
        /// thread: no continuation before the send uses ConfigureAwait(false). Cancelling the token aborts the request and
        /// throws an OperationCanceledException. A network failure throws an HttpRequestException. An HTTP error status
        /// (4xx, 5xx) is not an exception: it is returned like HttpClient returns it.
        /// Differences from HttpClient: UnityWebRequest merges the values of a repeated response header into one
        /// comma-separated value, the response reason phrase is not available, and the headers that the platform
        /// restricts (Host, Connection, Expect, Transfer-Encoding, Cookie / User-Agent on WebGL, ...) cannot be sent: UnityWebRequest refuses them and the
        /// call throws an InvalidOperationException that names the header (the original exception is its InnerException);
        /// the header is never dropped silently. Call the client methods from the Unity main thread (a SynchronizationContext
        /// is needed for the Abort() of a cancelled token to run there; without one it runs on the cancelling thread).
        /// </summary>
        private async global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> SendUnityWebRequestAsync(
            global::System.Net.Http.HttpRequestMessage request,
            global::System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            global::System.Uri? unityRequestUri = request.RequestUri;
            if (unityRequestUri is null || !unityRequestUri.IsAbsoluteUri)
            {
                throw new global::System.InvalidOperationException(""UnityWebRequest needs an absolute request URI: pass an absolute base address to the constructor or use an absolute server URL."");
            }

            // No ConfigureAwait(false): the code after this await still runs on the caller's context.
            byte[]? requestBody = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync();
            cancellationToken.ThrowIfCancellationRequested();

            using (var unityRequest = new global::UnityEngine.Networking.UnityWebRequest(unityRequestUri.AbsoluteUri, request.Method.Method))
            {
                unityRequest.downloadHandler = new global::UnityEngine.Networking.DownloadHandlerBuffer();
                if (requestBody != null && requestBody.Length > 0)
                {
                    unityRequest.uploadHandler = new global::UnityEngine.Networking.UploadHandlerRaw(requestBody);
                }

                foreach (var header in request.Headers)
                {
                    SetUnityRequestHeader(unityRequest, header.Key, header.Value);
                }

                if (request.Content != null)
                {
                    foreach (var header in request.Content.Headers)
                    {
                        // UnityWebRequest computes the length of the body itself.
                        if (!string.Equals(header.Key, ""Content-Length"", global::System.StringComparison.OrdinalIgnoreCase))
                        {
                            SetUnityRequestHeader(unityRequest, header.Key, header.Value);
                        }
                    }

                    if (unityRequest.uploadHandler != null && request.Content.Headers.ContentType != null)
                    {
                        unityRequest.uploadHandler.contentType = request.Content.Headers.ContentType.ToString();
                    }
                }

                int timeoutSeconds = 0;
                global::System.TimeSpan? timeout = RequestTimeout;
                if (timeout.HasValue && timeout.Value > global::System.TimeSpan.Zero)
                {
                    double seconds = global::System.Math.Ceiling(timeout.Value.TotalSeconds);
                    timeoutSeconds = seconds >= int.MaxValue ? int.MaxValue : (int)seconds;
                    unityRequest.timeout = timeoutSeconds;
                }

                ConfigureUnityWebRequest?.Invoke(unityRequest);

                var completion = new global::System.Threading.Tasks.TaskCompletionSource<bool>();
                var context = global::System.Threading.SynchronizationContext.Current;
                bool finished = false;
                var finishedGate = new object();
                var elapsed = global::System.Diagnostics.Stopwatch.StartNew();
                var operation = unityRequest.SendWebRequest();
                operation.completed += _ => completion.TrySetResult(true);
                if (operation.isDone)
                {
                    completion.TrySetResult(true);
                }

                // Abort() belongs to the main thread; the token may be cancelled on any thread. The gate keeps Abort() from
                // running while or after the request is disposed (without a context the callback runs on the cancelling thread).
                using (cancellationToken.Register(() =>
                {
                    global::System.Action abort = () =>
                    {
                        lock (finishedGate)
                        {
                            if (!finished) { unityRequest.Abort(); }
                        }
                    };
                    if (context != null) { context.Post(_ => abort(), null); } else { abort(); }
                }))
                {
                    await completion.Task;
                    lock (finishedGate) { finished = true; }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new global::System.OperationCanceledException(cancellationToken);
                }

                var unityResult = unityRequest.result;
                if (unityResult != global::UnityEngine.Networking.UnityWebRequest.Result.Success &&
                    unityResult != global::UnityEngine.Networking.UnityWebRequest.Result.ProtocolError)
                {
                    string unityError = unityRequest.error ?? unityResult.ToString();
                    if (timeoutSeconds > 0 &&
                        (unityError.IndexOf(""timeout"", global::System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         elapsed.Elapsed.TotalSeconds >= timeoutSeconds))
                    {
                        throw new global::System.Threading.Tasks.TaskCanceledException(
                            ""The request was canceled due to the configured RequestTimeout of "" + timeoutSeconds + "" seconds elapsing."",
                            new global::System.TimeoutException(unityError));
                    }

                    throw new global::System.Net.Http.HttpRequestException(""UnityWebRequest failed ("" + unityResult + ""): "" + unityError);
                }

                var response = new global::System.Net.Http.HttpResponseMessage((global::System.Net.HttpStatusCode)(int)unityRequest.responseCode);
                try
                {
                    byte[]? responseData = unityRequest.downloadHandler?.data;
                    response.Content = new global::System.Net.Http.ByteArrayContent(responseData ?? new byte[0]);
                    response.RequestMessage = request;
                    var responseHeaders = unityRequest.GetResponseHeaders();
                    if (responseHeaders != null)
                    {
                        foreach (var header in responseHeaders)
                        {
                            AddUnityResponseHeader(response, header.Key, header.Value);
                        }
                    }

                    return response;
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            }
        }

        private static void SetUnityRequestHeader(
            global::UnityEngine.Networking.UnityWebRequest unityRequest,
            string name,
            global::System.Collections.Generic.IEnumerable<string> values)
        {
            string separator = string.Equals(name, ""Cookie"", global::System.StringComparison.OrdinalIgnoreCase) ? ""; "" : "", "";
            try
            {
                unityRequest.SetRequestHeader(name, string.Join(separator, values));
            }
            catch (global::System.Exception exception) when (exception is global::System.InvalidOperationException || exception is global::System.ArgumentException)
            {
                // Not swallowed: a dropped Authorization header would go unnoticed.
                throw new global::System.InvalidOperationException(
                    ""The request header '"" + name + ""' cannot be sent with UnityWebRequest: "" + exception.Message, exception);
            }
        }

        /// <summary>Adds a response header to the response or, for the Content-* family, to its content.</summary>
        private static void AddUnityResponseHeader(global::System.Net.Http.HttpResponseMessage response, string name, string value)
        {
            // A pseudo header that some platforms report for the status line, and the length of the (decoded) body.
            if (string.IsNullOrEmpty(name) ||
                string.Equals(name, ""Content-Length"", global::System.StringComparison.OrdinalIgnoreCase) ||
                (value != null && value.StartsWith(""HTTP/"", global::System.StringComparison.Ordinal)))
            {
                return;
            }

            bool isContentHeader =
                name.StartsWith(""Content-"", global::System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ""Allow"", global::System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ""Expires"", global::System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ""Last-Modified"", global::System.StringComparison.OrdinalIgnoreCase);
            bool added = isContentHeader
                ? response.Content.Headers.TryAddWithoutValidation(name, value)
                : response.Headers.TryAddWithoutValidation(name, value);
            if (!added)
            {
                if (isContentHeader)
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }
                else
                {
                    response.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }");
        }

        /// <summary>
        /// テンプレートを 1 行ずつ追加し、出力がこのファイルの改行コードに依存しないようにする。
        /// Appends a template line by line so the output does not depend on the line endings of this file.
        /// </summary>
        private static void AppendTemplate(StringBuilder source, string template)
        {
            string[] lines = template.Replace("\r\n", "\n").Split('\n');
            // The first element is the empty remainder after the leading newline of the template.
            for (int index = 1; index < lines.Length; index++)
            {
                source.AppendLine(lines[index]);
            }
        }
    }
}
