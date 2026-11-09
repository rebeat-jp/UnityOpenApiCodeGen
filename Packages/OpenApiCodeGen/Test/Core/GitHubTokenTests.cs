#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.Lib;
using Rhycol.OpenApiCodeGen.UI;

internal sealed class GitHubTokenTests
{
    const string Token = "github_pat_secretTokenValue123";
    const string RawUrl = "https://raw.githubusercontent.com/owner/repo/main/openapi.json";
    const string Document = "{\"openapi\":\"3.0.0\"}";

    [Test]
    public void GenerationServicePassesUserTokenToProvider()
    {
        var provider = new RecordingProvider();
        var registry = new GenerationProviderRegistry();
        Assert.That(registry.TryRegister(provider, out string failureReason), Is.True, failureReason);
        var service = new GenerationService(
            new StubRepository<ProjectSetting>(
                new ProjectSetting(generateProvider: GenerateProvider.SourceGenerator)),
            new StubRepository<GenerationCSharpSetting>(new GenerationCSharpSetting()),
            new StubRepository<UserSetting>(new UserSetting(dockerPath: "docker", gitHubToken: Token)),
            registry);

        service.GenerateApiClientAsync(new GenerateApiClientDto(
                generateProvider: GenerateProvider.SourceGenerator,
                apiDocumentFilePathOrUrl: RawUrl,
                apiClientOutputFolderPath: "GeneratedClient"))
            .GetAwaiter()
            .GetResult();

        Assert.That(provider.LastRequest!.GitHubToken, Is.EqualTo(Token));
    }

    [Test]
    public void GenerationServiceLeavesTokenEmptyWithoutUserSetting()
    {
        var provider = new RecordingProvider();
        var registry = new GenerationProviderRegistry();
        Assert.That(registry.TryRegister(provider, out string failureReason), Is.True, failureReason);
        var service = new GenerationService(
            new StubRepository<ProjectSetting>(
                new ProjectSetting(generateProvider: GenerateProvider.SourceGenerator)),
            new StubRepository<GenerationCSharpSetting>(new GenerationCSharpSetting()),
            new StubRepository<UserSetting>(null),
            registry);

        service.GenerateApiClientAsync(new GenerateApiClientDto(
                generateProvider: GenerateProvider.SourceGenerator,
                apiDocumentFilePathOrUrl: RawUrl,
                apiClientOutputFolderPath: "GeneratedClient"))
            .GetAwaiter()
            .GetResult();

        Assert.That(provider.LastRequest!.GitHubToken, Is.Null);
    }

    [Test]
    public void DockerDownloadSendsTokenToRawGitHub()
    {
        var handler = new RecordingHandler();
        handler.Responses[RawUrl] = Ok(Document);

        string content = Download(RawUrl, Token, handler);

        Assert.That(content, Is.EqualTo(Document));
        Assert.That(handler.AuthorizationFor(RawUrl), Is.EqualTo("Bearer " + Token));
    }

    [Test]
    public void DockerDownloadDoesNotSendTokenToOtherHosts()
    {
        const string otherUrl = "https://example.com/openapi.json";
        var handler = new RecordingHandler();
        handler.Responses[otherUrl] = Ok(Document);

        Download(otherUrl, Token, handler);

        Assert.That(handler.Requested(otherUrl), Is.True);
        Assert.That(handler.AuthorizationFor(otherUrl), Is.Null);
    }

    [Test]
    public void DockerDownloadDropsTokenOnCrossOriginRedirect()
    {
        const string redirectTarget = "https://objects.example.com/openapi.json";
        var handler = new RecordingHandler();
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri(redirectTarget);
        handler.Responses[RawUrl] = redirect;
        handler.Responses[redirectTarget] = Ok(Document);

        string content = Download(RawUrl, Token, handler);

        Assert.That(content, Is.EqualTo(Document));
        Assert.That(handler.AuthorizationFor(RawUrl), Is.EqualTo("Bearer " + Token));
        Assert.That(handler.Requested(redirectTarget), Is.True);
        Assert.That(handler.AuthorizationFor(redirectTarget), Is.Null);
    }

    [TestCase(null, OpenApiCodeGenerator.MissingGitHubTokenHint)]
    [TestCase(Token, OpenApiCodeGenerator.RejectedGitHubTokenHint)]
    public void DockerDownloadExplainsGitHubNotFound(string? token, string expectedHint)
    {
        var handler = new RecordingHandler();
        handler.Responses[RawUrl] = new HttpResponseMessage(HttpStatusCode.NotFound);

        var exception = Assert.Throws<ExternalServiceException>(() => Download(RawUrl, token, handler));

        Assert.That(exception!.Message, Does.EndWith(expectedHint));
        Assert.That(exception.ToString(), Does.Not.Contain(Token));
    }

    [Test]
    public void SettingDisplayRoundTripKeepsToken()
    {
        var display = new UserSettingDisplayDto(dockerPath: "docker", gitHubToken: Token);
        var domain = new UserSetting(dockerPath: display.DockerPath, gitHubToken: display.GitHubToken);

        Assert.That(domain.GitHubToken, Is.EqualTo(Token));
        Assert.That(new UserSetting(dockerPath: "docker").GitHubToken, Is.Empty);
    }

    static string Download(string url, string? token, HttpMessageHandler handler)
    {
        return OpenApiCodeGenerator.DownloadRemoteDocumentAsync(
                url,
                token,
                GitHubAuthorizationPolicy.Default,
                handler,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    static HttpResponseMessage Ok(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        readonly Dictionary<string, string?> _authorization = new Dictionary<string, string?>();

        public Dictionary<string, HttpResponseMessage> Responses { get; } =
            new Dictionary<string, HttpResponseMessage>();

        public bool Requested(string url) => _authorization.ContainsKey(url);

        public string? AuthorizationFor(string url)
        {
            _authorization.TryGetValue(url, out string? value);
            return value;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            _authorization[url] = request.Headers.Authorization?.ToString();
            return Task.FromResult(
                Responses.TryGetValue(url, out HttpResponseMessage response)
                    ? response
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    sealed class RecordingProvider : IGenerationProvider
    {
        public GenerationProviderDescriptor Descriptor { get; } =
            new GenerationProviderDescriptor(
                GenerateProvider.SourceGenerator,
                "Test Source Generator",
                GenerationProviderAvailability.Available());

        public GenerationRequest? LastRequest { get; private set; }

        public GenerationResult Generate(GenerationRequest request)
        {
            LastRequest = request;
            return GenerationResult.Success();
        }

        public Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(GenerationResult.Success());
        }
    }

    sealed class StubRepository<T> : IAsyncRepository<T> where T : class
    {
        readonly T? _value;

        public StubRepository(T? value)
        {
            _value = value;
        }

        public Task<T?> ReadAsync() => Task.FromResult(_value);

        public Task SaveAsync(T value) => Task.CompletedTask;

        public Task DeleteAsync() => Task.CompletedTask;
    }
}
