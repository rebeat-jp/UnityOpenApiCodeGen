#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Lib;
using Rhycol.OpenApiCodeGen.UI;

namespace Rhycol.OpenApiCodeGen.Core
{
    internal class GenerationService
    {
        IAsyncRepository<GenerationCSharpSetting> _generationCsharpSettingJsonRepository
            => ApplicationConfig.GenerationCsharpSettingRepository;
        IAsyncRepository<ProjectSetting> _generalConfigJsonRepository
            => ApplicationConfig.ProjectSettingRepository;
        IAsyncRepository<UserSetting> _userSettingJsonRepository
            => ApplicationConfig.UserSettingsRepository;

        public async Task<GenerateApiClientDto> GetDefaultGenerateApiClientDtoAsync()
        {
            try
            {
                var projectSetting = await _generalConfigJsonRepository.ReadAsync();

                return projectSetting != null
                    ? new GenerateApiClientDto(
                        generateProvider: projectSetting.GenerateProvider,
                        apiDocumentFilePathOrUrl: projectSetting.ApiDocumentFilePathOrUrl,
                        apiClientOutputFolderPath: projectSetting.ApiClientOutputFolderPath)
                    : new GenerateApiClientDto();
            }
            catch (Exception e)
            {
                throw new ApplicationServiceException("デフォルトのAPIクライアント生成設定の取得に失敗しました。", e);
            }
        }

        public async Task GenerateApiClientAsync(
            GenerateApiClientDto generateApiClientDto,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var csharpSetting = await _generationCsharpSettingJsonRepository.ReadAsync() ?? new GenerationCSharpSetting();
                var absoluteOutputPath = Path.GetFullPath(generateApiClientDto.ApiClientOutputFolderPath);
                var projectSetting = new ProjectSetting(
                    apiClientOutputFolderPath: absoluteOutputPath,
                    apiDocumentFilePathOrUrl: generateApiClientDto.ApiDocumentFilePathOrUrl,
                    generateProvider: generateApiClientDto.GenerateProvider
                );
                var userSetting = await _userSettingJsonRepository.ReadAsync() ?? new UserSetting(
                    dockerPath: ""
                );

                IGenerator generator = projectSetting.GenerateProvider switch
                {
                    GenerateProvider.OpenApi => new OpenApiCodeGenerator(),
                    _ => new OpenApiCodeGenerator()
                };

                var response = await generator.GenerateAsync(projectSetting, csharpSetting, userSetting, cancellationToken);
                if (response.Status != ExitStatus.Success)
                {
                    throw new ApplicationServiceException($"APIクライアント生成の外部サービス実行に失敗しました。{Environment.NewLine}{response.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) when (e is not ApplicationServiceException)
            {
                throw new ApplicationServiceException("APIクライアント生成ユースケースに失敗しました。", e);
            }
        }
    }
}
