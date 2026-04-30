#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ReBeat.OpenApiCodeGen.Core;

using UnityEngine;

namespace ReBeat.OpenApiCodeGen.Lib
{
    internal class OpenApiCodeGenerator : IGenerator
    {
        const string CachedOpenApiDocumentFileName = "openapi-document.json";
        const string OpenApiCSharpConfigJsonFileName = "openapi-csharp-config.json";

        public ProcessResponse Generate(ProjectSetting projectSetting, GenerationCSharpSetting cSharpSetting, UserSetting userSetting)
        {
            EnsureOutputDirectory(projectSetting.ApiClientOutputFolderPath);
            var cachedOpenApiDocumentFilePath = CacheOpenApiDocumentFileAsync(
                projectSetting.ApiDocumentFilePathOrUrl,
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
                cancellationToken);

            var dockerProcess = CreateDockerProcess(userSetting);
            var cachedOpenApiConfigFilePath = await CacheCSharpConfigFileAsync(
                cSharpSetting,
                cancellationToken);

            var argumentsBuilder = new StringBuilder(1000);
            argumentsBuilder.Append("run --rm ");
            argumentsBuilder.Append($"-v \"{projectSetting.ApiClientOutputFolderPath}:/local\" ");
            argumentsBuilder.Append($"-v \"{cachedOpenApiConfigFilePath}:/config/config.json\" ");
            argumentsBuilder.Append($"-v \"{cachedOpenApiDocumentFilePath}:/input/openapi.json\" ");
            argumentsBuilder.Append("openapitools/openapi-generator-cli generate ");
            argumentsBuilder.Append("-i \"/input/openapi.json\" ");
            argumentsBuilder.Append("-g \"csharp\" -o \"/local\" ");
            argumentsBuilder.Append("-c /config/config.json");

            return await dockerProcess.SendAsync(argumentsBuilder.ToString(), cancellationToken);

        }

        static DockerProcess CreateDockerProcess(UserSetting userSetting)
        {
            return new DockerProcess
            {
                Path = string.IsNullOrWhiteSpace(userSetting.DockerPath) ? "docker" : userSetting.DockerPath
            };
        }

        static async Task<string> CacheOpenApiDocumentFileAsync(string documentFilePathOrUrl, CancellationToken cancellationToken)
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
                    await SaveRemoteDocumentAsync(documentFilePathOrUrl, cachedFilePath, cancellationToken);
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
            catch (Exception e)
            {
                throw new ExternalStorageException("OpenAPI C#ジェネレーター設定のキャッシュ保存に失敗しました。", e);
            }
        }

        static bool IsHttpUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        static async Task SaveRemoteDocumentAsync(string documentUrl, string savePath, CancellationToken cancellationToken)
        {
            try
            {
                using var httpClient = new HttpClient();
                using var response = await httpClient.GetAsync(documentUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new ExternalServiceException($"OpenAPIドキュメントURLの読み込みに失敗しました。status: {(int)response.StatusCode} {response.ReasonPhrase}, url: {documentUrl}");
                }

                var content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content))
                {
                    throw new ExternalServiceException($"OpenAPIドキュメントURLのレスポンスが空です。url: {documentUrl}");
                }

                await File.WriteAllTextAsync(savePath, content, cancellationToken);
            }
            catch (Exception e) when (e is not ExternalServiceException && e is not OperationCanceledException)
            {
                throw new ExternalServiceException($"OpenAPIドキュメントURLの読み込みに失敗しました。url: {documentUrl}", e);
            }
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
