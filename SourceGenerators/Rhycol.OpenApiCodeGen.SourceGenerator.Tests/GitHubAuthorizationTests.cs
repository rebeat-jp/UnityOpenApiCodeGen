using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class GitHubAuthorizationTests
    {
        private const string SpecId = "0123456789abcdef0123456789abcdef";
        private const string Token = "github_pat_secretTokenValue123";

        private const string RootJson = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Root"", ""version"": ""1"" },
  ""paths"": {},
  ""components"": {
    ""schemas"": {
      ""Pet"": { ""$ref"": ""models.json"" }
    }
  }
}";

        private const string ModelsJson =
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}";

        [Theory]
        [InlineData("https://raw.githubusercontent.com/owner/repo/main/openapi.yaml", true)]
        [InlineData("https://RAW.githubusercontent.com/owner/repo/main/openapi.yaml", true)]
        [InlineData("https://raw.githubusercontent.com:443/owner/repo/main/openapi.yaml", true)]
        [InlineData("http://raw.githubusercontent.com/owner/repo/main/openapi.yaml", false)]
        [InlineData("https://raw.githubusercontent.com:8443/owner/repo/main/openapi.yaml", false)]
        [InlineData("https://raw.githubusercontent.com.evil.example/owner/repo/main/openapi.yaml", false)]
        [InlineData("https://evil.example/raw.githubusercontent.com/openapi.yaml", false)]
        [InlineData("https://github.com/owner/repo/raw/main/openapi.yaml", false)]
        [InlineData("https://api.github.com/repos/owner/repo/contents/openapi.yaml", false)]
        [InlineData("https://user:pass@raw.githubusercontent.com/owner/repo/main/openapi.yaml", false)]
        public void DefaultPolicyAllowsOnlyHttpsRawGitHubOrigin(string url, bool expected)
        {
            bool allowed = GitHubAuthorizationPolicy.Default.TryGetToken(new Uri(url), Token, out string token);

            Assert.Equal(expected, allowed);
            Assert.Equal(expected ? Token : string.Empty, token);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc def")]
        [InlineData("abc\r\nX-Injected: 1")]
        [InlineData("abc\tdef")]
        [InlineData("tökén")]
        public void MalformedTokensAreNeverSent(string? configuredToken)
        {
            bool allowed = GitHubAuthorizationPolicy.Default.TryGetToken(
                new Uri("https://raw.githubusercontent.com/owner/repo/main/openapi.yaml"),
                configuredToken,
                out string token);

            Assert.False(allowed);
            Assert.Equal(string.Empty, token);
        }

        [Fact]
        public void TokenIsTrimmed()
        {
            Assert.Equal(Token, GitHubAuthorizationPolicy.NormalizeToken("  " + Token + "\n"));
        }

        [Fact]
        public async Task TokenIsSentToRootAndRelativeReferencesOnAllowedOrigin()
        {
            using var server = new RecordingHttpServer();
            server.Add("/specs/openapi.json", 200, "application/json", RootJson);
            server.Add("/specs/models.json", 200, "application/json", ModelsJson);
            string projectRoot = CreateProjectRoot();
            try
            {
                ExternalSpecGraphLoader loader = CreateLoader(projectRoot, server.Origin);

                NormalizedSpecGraph graph = await loader.LoadAsync(
                    server.Url("/specs/openapi.json"),
                    SpecId,
                    CancellationToken.None,
                    null,
                    "  " + Token + "  ");

                Assert.Equal(2, graph.Documents.Count);
                Assert.Equal("Bearer " + Token, server.AuthorizationFor("/specs/openapi.json"));
                Assert.Equal("Bearer " + Token, server.AuthorizationFor("/specs/models.json"));

                string bundleText = Encoding.UTF8.GetString(CanonicalSpecBundleWriter.Write(
                    graph.SpecId,
                    graph.RawSha256,
                    graph.Documents,
                    graph.Edges));
                Assert.DoesNotContain(Token, bundleText);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task TokenIsNotSentToCrossOriginRedirectTarget()
        {
            using var allowed = new RecordingHttpServer();
            using var other = new RecordingHttpServer();
            other.Add("/openapi.json", 200, "application/json",
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"R\",\"version\":\"1\"},\"paths\":{}}");
            allowed.AddRedirect("/openapi.json", other.Url("/openapi.json"));
            string projectRoot = CreateProjectRoot();
            try
            {
                ExternalSpecGraphLoader loader = CreateLoader(projectRoot, allowed.Origin);

                await loader.LoadAsync(
                    allowed.Url("/openapi.json"),
                    SpecId,
                    CancellationToken.None,
                    null,
                    Token);

                Assert.Equal("Bearer " + Token, allowed.AuthorizationFor("/openapi.json"));
                Assert.True(other.WasRequested("/openapi.json"));
                Assert.Null(other.AuthorizationFor("/openapi.json"));
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task TokenIsNotSentWhenOriginIsNotAllowed()
        {
            using var server = new RecordingHttpServer();
            server.Add("/specs/openapi.json", 200, "application/json", RootJson);
            server.Add("/specs/models.json", 200, "application/json", ModelsJson);
            string projectRoot = CreateProjectRoot();
            try
            {
                var loader = new ExternalSpecGraphLoader(
                    projectRoot,
                    new RawJsonNormalizer(),
                    new RawYamlNormalizer());

                await loader.LoadAsync(
                    server.Url("/specs/openapi.json"),
                    SpecId,
                    CancellationToken.None,
                    null,
                    Token);

                Assert.True(server.WasRequested("/specs/models.json"));
                Assert.Null(server.AuthorizationFor("/specs/openapi.json"));
                Assert.Null(server.AuthorizationFor("/specs/models.json"));
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task NoAuthorizationHeaderWithoutToken()
        {
            using var server = new RecordingHttpServer();
            server.Add("/specs/openapi.json", 200, "application/json", RootJson);
            server.Add("/specs/models.json", 200, "application/json", ModelsJson);
            string projectRoot = CreateProjectRoot();
            try
            {
                ExternalSpecGraphLoader loader = CreateLoader(projectRoot, server.Origin);

                await loader.LoadAsync(server.Url("/specs/openapi.json"), SpecId, CancellationToken.None);

                Assert.True(server.WasRequested("/specs/openapi.json"));
                Assert.Null(server.AuthorizationFor("/specs/openapi.json"));
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Theory]
        [InlineData(404, null, ExternalSpecGraphLoader.MissingGitHubTokenHint)]
        [InlineData(401, null, ExternalSpecGraphLoader.MissingGitHubTokenHint)]
        [InlineData(404, Token, ExternalSpecGraphLoader.RejectedGitHubTokenHint)]
        [InlineData(403, Token, ExternalSpecGraphLoader.RejectedGitHubTokenHint)]
        public async Task GitHubAccessFailuresIncludeHintWithoutToken(
            int statusCode,
            string? configuredToken,
            string expectedHint)
        {
            using var server = new RecordingHttpServer();
            server.Add("/openapi.json", statusCode, "text/plain", "Not Found");
            string projectRoot = CreateProjectRoot();
            try
            {
                ExternalSpecGraphLoader loader = CreateLoader(projectRoot, server.Origin);

                Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => loader.LoadAsync(
                    server.Url("/openapi.json"),
                    SpecId,
                    CancellationToken.None,
                    null,
                    configuredToken));

                Assert.Contains("HTTP " + statusCode, exception.Message);
                Assert.EndsWith(expectedHint, exception.Message);
                Assert.DoesNotContain(Token, exception.ToString());
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task NonGitHubFailuresDoNotIncludeHint()
        {
            using var server = new RecordingHttpServer();
            server.Add("/openapi.json", 404, "text/plain", "Not Found");
            string projectRoot = CreateProjectRoot();
            try
            {
                var loader = new ExternalSpecGraphLoader(
                    projectRoot,
                    new RawJsonNormalizer(),
                    new RawYamlNormalizer());

                Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => loader.LoadAsync(
                    server.Url("/openapi.json"),
                    SpecId,
                    CancellationToken.None));

                Assert.DoesNotContain("GitHub Token", exception.Message);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        private static ExternalSpecGraphLoader CreateLoader(string projectRoot, Uri allowedOrigin)
        {
            return new ExternalSpecGraphLoader(
                projectRoot,
                new RawJsonNormalizer(),
                new RawYamlNormalizer(),
                new GitHubAuthorizationPolicy(new[] { allowedOrigin }));
        }

        private static string CreateProjectRoot()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "OpenApiCodeGenGitHubAuthorization",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private sealed class RecordingHttpServer : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly Dictionary<string, (int Status, string ContentType, string Body, string? Location)> responses =
                new Dictionary<string, (int, string, string, string?)>(StringComparer.Ordinal);
            private readonly Dictionary<string, string?> authorizationByTarget =
                new Dictionary<string, string?>(StringComparer.Ordinal);
            private readonly CancellationTokenSource stop = new CancellationTokenSource();
            private readonly Task acceptTask;

            internal RecordingHttpServer()
            {
                listener.Start();
                acceptTask = Task.Run(AcceptLoopAsync);
            }

            internal Uri Origin => new Uri("http://127.0.0.1:" + Port + "/");

            private int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

            internal string Url(string path) => "http://127.0.0.1:" + Port + path;

            internal void Add(string target, int status, string contentType, string body)
            {
                responses[target] = (status, contentType, body, null);
            }

            internal void AddRedirect(string target, string location)
            {
                responses[target] = (302, "text/plain", string.Empty, location);
            }

            internal bool WasRequested(string target)
            {
                lock (authorizationByTarget)
                {
                    return authorizationByTarget.ContainsKey(target);
                }
            }

            internal string? AuthorizationFor(string target)
            {
                lock (authorizationByTarget)
                {
                    authorizationByTarget.TryGetValue(target, out string? value);
                    return value;
                }
            }

            public void Dispose()
            {
                stop.Cancel();
                listener.Stop();
                try
                {
                    acceptTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                }
            }

            private async Task AcceptLoopAsync()
            {
                while (!stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    _ = Task.Run(() => HandleAsync(client));
                }
            }

            private async Task HandleAsync(TcpClient client)
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    string? requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (requestLine == null)
                    {
                        return;
                    }

                    string target = requestLine.Split(' ')[1];
                    string? authorization = null;
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
                    {
                        int separator = line.IndexOf(':');
                        if (separator > 0 &&
                            string.Equals(line.Substring(0, separator), "Authorization", StringComparison.OrdinalIgnoreCase))
                        {
                            authorization = line.Substring(separator + 1).Trim();
                        }
                    }

                    lock (authorizationByTarget)
                    {
                        authorizationByTarget[target] = authorization;
                    }

                    if (!responses.TryGetValue(target, out var response))
                    {
                        response = (404, "text/plain", "missing", null);
                    }

                    byte[] body = Encoding.UTF8.GetBytes(response.Body);
                    var header = new StringBuilder();
                    header.Append("HTTP/1.1 ").Append(response.Status).Append(" Status\r\n");
                    header.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
                    header.Append("Content-Length: ").Append(body.Length).Append("\r\n");
                    if (response.Location != null)
                    {
                        header.Append("Location: ").Append(response.Location).Append("\r\n");
                    }

                    header.Append("Connection: close\r\n\r\n");
                    byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
                    await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
