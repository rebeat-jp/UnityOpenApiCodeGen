using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen.UnityWebRequestVerification;

using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Tests
{
    /// <summary>
    /// Runs the generated UnityWebRequest client (UnityWebRequestClient/Generated/UwrApi.g.cs, the snapshot of the
    /// Source Generator output for HttpTransport = UnityWebRequest) against a local HttpListener, with the real
    /// UnityWebRequest of the editor: a GET, a POST, an HTTP error status, a network failure, the main thread,
    /// cancellation and the timeout.
    /// </summary>
    internal sealed class UnityWebRequestTransportUnityTests
    {
        private const double WaitSeconds = 30;
        private const string DelayHeader = "X-Test-Delay-Ms";

        private LocalApiServer _server;

        [SetUp]
        public void StartServer()
        {
            _server = new LocalApiServer();
        }

        [TearDown]
        public void StopServer()
        {
            _server.Dispose();
        }

        // ---- GET and POST ------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator GetListsItemsThroughUnityWebRequest()
        {
            var client = new UwrApi(_server.BaseAddress);

            Task<List<Item>> task = client.listItems();
            yield return Wait(task);

            Assert.That(task.IsCompleted && !task.IsFaulted, Is.True, Describe(task));
            List<Item> items = task.Result;
            Assert.That(items.Count, Is.EqualTo(2));
            Assert.That(items[0].Id, Is.EqualTo(1));
            Assert.That(items[0].Label, Is.EqualTo("one"));
            Assert.That(items[1].Label, Is.EqualTo("日本語"));
            RecordedRequest request = _server.Single();
            Assert.That(request.Method, Is.EqualTo("GET"));
            // The relative server URL "/api" resolved against the absolute base address.
            Assert.That(request.Path, Is.EqualTo("/api/items"));
        }

        [UnityTest]
        public IEnumerator PostSendsTheJsonBodyAndReturnsTheCreatedItem()
        {
            var client = new UwrApi(_server.BaseAddress);

            Task<Item> task = client.createItem(new Item { Id = 0, Label = "néw" });
            yield return Wait(task);

            Assert.That(task.IsCompleted && !task.IsFaulted, Is.True, Describe(task));
            Assert.That(task.Result.Id, Is.EqualTo(7));
            Assert.That(task.Result.Label, Is.EqualTo("néw"));
            RecordedRequest request = _server.Single();
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo("/api/items"));
            Assert.That(request.Header("Content-Type"), Does.StartWith("application/json"));
            Assert.That(request.Header("Content-Type").ToLowerInvariant(), Does.Contain("utf-8"));
            Assert.That(ExtractLabel(request.Body), Is.EqualTo("néw"));
            Assert.That(request.Body, Does.Contain("\"id\":0"));
        }

        [UnityTest]
        public IEnumerator AnHttpErrorStatusIsATypedApiExceptionNotANetworkFailure()
        {
            var client = new UwrApi(_server.BaseAddress);

            Task<Item> task = client.createItem(new Item { Id = 0, Label = "bad" });
            yield return Wait(task);

            Assert.That(task.IsFaulted, Is.True, Describe(task));
            var failure = Capture(task) as UwrApiException;
            Assert.That(failure, Is.Not.Null, Describe(task));
            Assert.That(failure.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(failure.Error, Is.InstanceOf<Problem>());
            Assert.That(((Problem)failure.Error).Title, Is.EqualTo("rejected"));
        }

        [UnityTest]
        public IEnumerator ANetworkFailureIsAnHttpRequestException()
        {
            var client = new UwrApi(new Uri("http://127.0.0.1:" + FindUnusedPort() + "/"));

            Task<List<Item>> task = client.listItems();
            yield return Wait(task);

            Assert.That(task.IsFaulted, Is.True, Describe(task));
            Assert.That(Capture(task), Is.InstanceOf<HttpRequestException>(), Describe(task));
        }

        // ---- the main thread ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheRequestIsCreatedSentAndAnsweredOnTheMainThread()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            Assert.That(
                SynchronizationContext.Current,
                Is.Not.Null,
                "The EditMode test runs without the Unity SynchronizationContext, so this test would prove nothing.");
            var threads = new List<int>();
            var client = new UwrApi(_server.BaseAddress);
            // Runs when the UnityWebRequest is about to be sent: the thread that created it.
            client.ConfigureUnityWebRequest = _ => { lock (threads) { threads.Add(Thread.CurrentThread.ManagedThreadId); } };

            async Task Run()
            {
                lock (threads) { threads.Add(Thread.CurrentThread.ManagedThreadId); }
                await client.listItems();
                lock (threads) { threads.Add(Thread.CurrentThread.ManagedThreadId); }
                await client.createItem(new Item { Id = 0, Label = "x" });
                lock (threads) { threads.Add(Thread.CurrentThread.ManagedThreadId); }
            }

            Task task = Run();
            yield return Wait(task);

            Assert.That(task.IsFaulted, Is.False, Describe(task));
            Assert.That(threads.Count, Is.EqualTo(5), string.Join(",", threads));
            Assert.That(threads, Is.All.EqualTo(mainThread), string.Join(",", threads));
        }

        // ---- cancellation and timeout ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator CancellingTheTokenAbortsTheRequest()
        {
            var client = new UwrApi(_server.BaseAddress);
            client.ConfigureUnityWebRequest = request => request.SetRequestHeader(DelayHeader, "20000");
            using (var cancellation = new CancellationTokenSource())
            {
                var watch = Stopwatch.StartNew();
                Task<List<Item>> task = client.listItems(cancellation.Token);
                yield return WaitUntil(() => _server.Requests.Count >= 1, "the server did not receive the request");
                Assert.That(task.IsCompleted, Is.False);

                cancellation.Cancel();
                yield return Wait(task);

                Assert.That(
                    Capture(task),
                    Is.InstanceOf<OperationCanceledException>(),
                    Describe(task));
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(15), "The request was not aborted.");
            }
        }

        [UnityTest]
        public IEnumerator ATimeoutThrowsTaskCanceledExceptionWithATimeoutExceptionInside()
        {
            var client = new UwrApi(_server.BaseAddress);
            client.RequestTimeout = TimeSpan.FromSeconds(1);
            client.ConfigureUnityWebRequest = request => request.SetRequestHeader(DelayHeader, "20000");

            Task<List<Item>> task = client.listItems();
            yield return Wait(task);

            Exception failure = Capture(task);
            Assert.That(failure, Is.InstanceOf<TaskCanceledException>(), Describe(task));
            Assert.That(failure.InnerException, Is.InstanceOf<TimeoutException>());
        }

        // ---- helpers -----------------------------------------------------------------------------------

        /// <summary>The exception that awaiting the finished task throws (null when it succeeded); a cancelled task throws OperationCanceledException.</summary>
        private static Exception Capture(Task task)
        {
            try
            {
                task.GetAwaiter().GetResult();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static string Describe(Task task)
        {
            return task.IsFaulted ? task.Exception.ToString() : "status " + task.Status;
        }

        /// <summary>Lets the editor loop run (so the UnityWebRequest and the main-thread continuations progress) until the task ends.</summary>
        private static IEnumerator Wait(Task task)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (watch.Elapsed.TotalSeconds > WaitSeconds)
                {
                    Assert.Fail("The task did not finish within " + WaitSeconds + " seconds.");
                }

                yield return null;
            }
        }

        private static IEnumerator WaitUntil(Func<bool> condition, string failure)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed.TotalSeconds > WaitSeconds)
                {
                    Assert.Fail(failure);
                }

                yield return null;
            }
        }

        private static string ExtractLabel(string json)
        {
            Match match = Regex.Match(json, "\"label\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static int FindUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private sealed class RecordedRequest
        {
            internal string Method;
            internal string Path;
            internal string Body;
            internal Dictionary<string, string> Headers;

            internal string Header(string name)
            {
                string value;
                return Headers.TryGetValue(name, out value) ? value : string.Empty;
            }
        }

        /// <summary>A local HTTP server for /api/items; X-Test-Delay-Ms delays the answer.</summary>
        private sealed class LocalApiServer : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();

            internal LocalApiServer()
            {
                int port = FindUnusedPort();
                _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                _listener.Start();
                BaseAddress = new Uri("http://127.0.0.1:" + port + "/");
                Task.Run(Accept);
            }

            internal Uri BaseAddress { get; }

            internal ConcurrentQueue<RecordedRequest> Requests { get; } = new ConcurrentQueue<RecordedRequest>();

            internal RecordedRequest Single()
            {
                Assert.That(Requests.Count, Is.EqualTo(1));
                RecordedRequest request;
                Assert.That(Requests.TryPeek(out request), Is.True);
                return request;
            }

            private async Task Accept()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    var ignored = Task.Run(() => Handle(context));
                }
            }

            private async Task Handle(HttpListenerContext context)
            {
                try
                {
                    byte[] bodyBytes;
                    using (var buffer = new System.IO.MemoryStream())
                    {
                        context.Request.InputStream.CopyTo(buffer);
                        bodyBytes = buffer.ToArray();
                    }

                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string name in context.Request.Headers.AllKeys)
                    {
                        headers[name] = context.Request.Headers[name];
                    }

                    var recorded = new RecordedRequest
                    {
                        Method = context.Request.HttpMethod,
                        Path = context.Request.Url.AbsolutePath,
                        Body = Encoding.UTF8.GetString(bodyBytes),
                        Headers = headers
                    };
                    Requests.Enqueue(recorded);

                    string delay = recorded.Header(DelayHeader);
                    if (delay.Length > 0)
                    {
                        await Task.Delay(int.Parse(delay), _stop.Token);
                    }

                    int status = 404;
                    string json = "{\"title\":\"not found\"}";
                    if (recorded.Path == "/api/items" && recorded.Method == "GET")
                    {
                        status = 200;
                        json = "[{\"id\":1,\"label\":\"one\"},{\"id\":2,\"label\":\"日本語\"}]";
                    }
                    else if (recorded.Path == "/api/items" && recorded.Method == "POST")
                    {
                        string label = ExtractLabel(recorded.Body);
                        if (label == "bad")
                        {
                            status = 400;
                            json = "{\"title\":\"rejected\"}";
                        }
                        else
                        {
                            status = 201;
                            json = "{\"id\":7,\"label\":\"" + label + "\"}";
                        }
                    }

                    byte[] answer = Encoding.UTF8.GetBytes(json);
                    context.Response.StatusCode = status;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    context.Response.ContentLength64 = answer.Length;
                    context.Response.OutputStream.Write(answer, 0, answer.Length);
                    context.Response.Close();
                }
                catch (Exception)
                {
                    // The client aborted the request or the server is shutting down.
                    try { context.Response.Abort(); } catch (Exception) { }
                }
            }

            public void Dispose()
            {
                _stop.Cancel();
                try { _listener.Stop(); } catch (Exception) { }
                try { _listener.Close(); } catch (Exception) { }
            }
        }
    }
}
