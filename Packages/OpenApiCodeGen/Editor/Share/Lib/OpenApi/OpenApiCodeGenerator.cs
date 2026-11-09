#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;

using UnityEngine;

namespace Rhycol.OpenApiCodeGen.Lib
{
    internal class OpenApiCodeGenerator : IGenerator
    {
        const string CachedOpenApiDocumentFileName = "openapi-document.json";
        const string OpenApiCSharpConfigJsonFileName = "openapi-csharp-config.json";
        const int MaximumRedirects = 5;

        internal const string MissingGitHubTokenHint =
            " privateリポジトリの設計書の場合は、Setting MenuのGitHub Tokenを設定してください。";
        internal const string RejectedGitHubTokenHint =
            " 設定されたGitHub Tokenが拒否されたか、このリポジトリを読む権限がありません。";

        public ProcessResponse Generate(ProjectSetting projectSetting, GenerationCSharpSetting cSharpSetting, UserSetting userSetting)
        {
            EnsureOutputDirectory(projectSetting.ApiClientOutputFolderPath);
            var cachedOpenApiDocumentFilePath = CacheOpenApiDocumentFileAsync(
                projectSetting.ApiDocumentFilePathOrUrl,
                userSetting.GitHubToken,
                CancellationToken.None).GetAwaiter().GetResult();

            var cachedOpenApiConfigFilePath = CacheCSharpConfigFileAsync(
                cSharpSetting,
                CancellationToken.None).GetAwaiter().GetResult();

            var dockerProcess = CreateDockerProcess(userSetting);

            var argumentsBuilder = new StringBuilder(1000);
            argumentsBuilder.Append("run --rm ");
            argumentsBuilder.Append($"-v \"{projectSetting.ApiClientOutputFolderPath}:/local\" ");
            argumentsBuilder.Append($"-v \"{cachedOpenApiConfigFilePath}:/config/config.json\" ");
            argumentsBuilder.Append($"-v \"{cachedOpenApiDocumentFilePath}:/input/openapi.json\" ");
            argumentsBuilder.Append("openapitools/openapi-generator-cli generate ");
            argumentsBuilder.Append("-i \"/input/openapi.json\" ");
            argumentsBuilder.Append("-g \"csharp\" -o \"/local\" ");
            argumentsBuilder.Append("-c /config/config.json");

            return dockerProcess.Send(argumentsBuilder.ToString());

        }

        public async Task<ProcessResponse> GenerateAsync(ProjectSetting projectSetting, GenerationCSharpSetting cSharpSetting, UserSetting userSetting, CancellationToken cancellationToken = default)
        {

            EnsureOutputDirectory(projectSetting.ApiClientOutputFolderPath);
            var cachedOpenApiDocumentFilePath = await CacheOpenApiDocumentFileAsync(
                projectSetting.ApiDocumentFilePathOrUrl,
                userSetting.GitHubToken,
                cancellationToken);

            var dockerProcess = CreateDockerProcess(userSetting);
            var cachedOpenApiConfigFilePath = await CacheCSharpConfigFileAsync(
                cSharpSetting,
                cancellationToken);

            var argumentsBuilder = new StringBuilder(1000);
            string containerName = "openapi-codegen-" + Guid.NewGuid().ToString("N");
            argumentsBuilder.Append("run --rm --name ").Append(containerName).Append(' ');
            argumentsBuilder.Append($"-v \"{projectSetting.ApiClientOutputFolderPath}:/local\" ");
            argumentsBuilder.Append($"-v \"{cachedOpenApiConfigFilePath}:/config/config.json\" ");
            argumentsBuilder.Append($"-v \"{cachedOpenApiDocumentFilePath}:/input/openapi.json\" ");
            argumentsBuilder.Append("openapitools/openapi-generator-cli generate ");
            argumentsBuilder.Append("-i \"/input/openapi.json\" ");
            argumentsBuilder.Append("-g \"csharp\" -o \"/local\" ");
            argumentsBuilder.Append("-c /config/config.json");

            return await dockerProcess.SendContainerAsync(argumentsBuilder.ToString(), containerName, cancellationToken);

        }

        static DockerProcess CreateDockerProcess(UserSetting userSetting)
        {
            return new DockerProcess
            {
                Path = string.IsNullOrWhiteSpace(userSetting.DockerPath) ? "docker" : userSetting.DockerPath
            };
        }

        static async Task<string> CacheOpenApiDocumentFileAsync(string documentFilePathOrUrl, string? gitHubToken, CancellationToken cancellationToken)
        {
            try
            {
                if (!Directory.Exists(ApplicationConstant.CacheFolderPath))
                {
                    Directory.CreateDirectory(ApplicationConstant.CacheFolderPath);
                }

                var cachedFilePath = Path.Combine(ApplicationConstant.CacheFolderPath, CachedOpenApiDocumentFileName);
                if (IsHttpUrl(documentFilePathOrUrl))
                {
                    await SaveRemoteDocumentAsync(documentFilePathOrUrl, gitHubToken, cachedFilePath, cancellationToken);
                    return cachedFilePath;
                }

                var sourceFilePath = Path.GetFullPath(documentFilePathOrUrl);
                if (!File.Exists(sourceFilePath))
                {
                    throw new ExternalStorageException($"OpenAPIドキュメントファイルが見つかりません。path: {sourceFilePath}");
                }

                File.Copy(sourceFilePath, cachedFilePath, true);
                return cachedFilePath;
            }
            catch (Exception e) when (e is not ExternalStorageException && e is not ExternalServiceException && e is not OperationCanceledException)
            {
                throw new ExternalStorageException($"OpenAPIドキュメントのキャッシュ保存に失敗しました。path or url: {documentFilePathOrUrl}", e);
            }
        }

        static async Task<string> CacheCSharpConfigFileAsync(GenerationCSharpSetting cSharpSetting, CancellationToken cancellationToken)
        {
            try
            {
                if (!Directory.Exists(ApplicationConstant.CacheFolderPath))
                {
                    Directory.CreateDirectory(ApplicationConstant.CacheFolderPath);
                }

                var configFilePath = Path.Combine(ApplicationConstant.CacheFolderPath, OpenApiCSharpConfigJsonFileName);
                var openApiCsharpConfig = new OpenApiCsharpOption(cSharpSetting);
                var json = JsonUtility.ToJson(openApiCsharpConfig);
                await File.WriteAllTextAsync(configFilePath, json, cancellationToken);
                return configFilePath;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new ExternalStorageException("OpenAPI C#ジェネレーター設定のキャッシュ保存に失敗しました。", e);
            }
        }

        static bool IsHttpUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        static async Task SaveRemoteDocumentAsync(string documentUrl, string? gitHubToken, string savePath, CancellationToken cancellationToken)
        {
            var content = await DownloadRemoteDocumentAsync(
                documentUrl,
                gitHubToken,
                GitHubAuthorizationPolicy.Default,
                new HttpClientHandler { AllowAutoRedirect = false },
                cancellationToken);
            try
            {
                await File.WriteAllTextAsync(savePath, content, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new ExternalStorageException($"OpenAPIドキュメントのキャッシュ保存に失敗しました。url: {documentUrl}", e);
            }
        }

        /// <summary>
        /// Downloads a remote OpenAPI document. Redirects are followed manually so the GitHub token
        /// is attached only to hops whose origin <paramref name="authorizationPolicy"/> allows.
        /// </summary>
        internal static async Task<string> DownloadRemoteDocumentAsync(
            string documentUrl,
            string? gitHubToken,
            GitHubAuthorizationPolicy authorizationPolicy,
            HttpMessageHandler handler,
            CancellationToken cancellationToken)
        {
            try
            {
                using var httpClient = new HttpClient(handler, disposeHandler: true);
                var current = new Uri(documentUrl, UriKind.Absolute);
                for (int redirects = 0; ; redirects++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, current);
                    bool sentGitHubToken = authorizationPolicy.TryGetToken(current, gitHubToken, out string token);
                    if (sentGitHubToken)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue(
                            GitHubAuthorizationPolicy.AuthorizationScheme,
                            token);
                    }

                    using var response = await httpClient.SendAsync(request, cancellationToken);
                    if (IsRedirect(response.StatusCode))
                    {
                        Uri? location = response.Headers.Location;
                        if (location == null || redirects >= MaximumRedirects)
                        {
                            throw new ExternalServiceException($"OpenAPIドキュメントURLのリダイレクトを処理できませんでした。url: {documentUrl}");
                        }

                        current = location.IsAbsoluteUri ? location : new Uri(current, location);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        string hint = GetGitHubAccessHint(response.StatusCode, current, sentGitHubToken, authorizationPolicy);
                        throw new ExternalServiceException($"OpenAPIドキュメントURLの読み込みに失敗しました。status: {(int)response.StatusCode} {response.ReasonPhrase}, url: {documentUrl}{hint}");
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        throw new ExternalServiceException($"OpenAPIドキュメントURLのレスポンスが空です。url: {documentUrl}");
                    }

                    return content;
                }
            }
            catch (Exception e) when (e is not ExternalServiceException && e is not OperationCanceledException)
            {
                throw new ExternalServiceException($"OpenAPIドキュメントURLの読み込みに失敗しました。url: {documentUrl}", e);
            }
        }

        static bool IsRedirect(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
        }

        static string GetGitHubAccessHint(
            HttpStatusCode statusCode,
            Uri requestUri,
            bool sentGitHubToken,
            GitHubAuthorizationPolicy authorizationPolicy)
        {
            if ((statusCode != HttpStatusCode.Unauthorized &&
                 statusCode != HttpStatusCode.Forbidden &&
                 statusCode != HttpStatusCode.NotFound) ||
                !authorizationPolicy.IsAllowedOrigin(requestUri))
            {
                return string.Empty;
            }

            return sentGitHubToken ? RejectedGitHubTokenHint : MissingGitHubTokenHint;
        }

        static void EnsureOutputDirectory(string outputFolderPath)
        {
            try
            {
                if (!Directory.Exists(outputFolderPath))
                {
                    Directory.CreateDirectory(outputFolderPath);
                }
            }
            catch (Exception e)
            {
                throw new ExternalStorageException($"APIクライアント出力先フォルダーの作成に失敗しました。path: {outputFolderPath}", e);
            }
        }
    }
}
