using System.Threading.Tasks;
using System;

namespace ReBeat.OpenApiCodeGen.Core
{
    internal class SetupService
    {

        IAsyncRepository<UserSetting> _userSettingJsonRepository
            => ApplicationConfig.UserSettingsRepository;
        IAsyncRepository<ProjectSetting> _projectSettingJsonRepository
            => ApplicationConfig.ProjectSettingRepository;
        IAsyncRepository<GenerationCSharpSetting> _generationCSharpSettingJsonRepository
            => ApplicationConfig.GenerationCsharpSettingRepository;

        public async Task<bool> CheckDockerInstalledAsync(CheckDockerInstalledDto checkDockerInstalledDto)
        {
            try
            {
                var dockerProcess = new DockerProcess(path: checkDockerInstalledDto.DockerPath);
                var res = await dockerProcess.SendAsync("--version");

                return res.Status == ExitStatus.Success;
            }
            catch (ExternalServiceException)
            {
                return false;
            }
            catch (Exception e)
            {
                throw new ApplicationServiceException("Dockerインストール確認ユースケースに失敗しました。", e);
            }
        }

        public async Task SetupAsync(SetupDto setupDto)
        {
            try
            {
                // 既存の設定を保持しつつ、ユーザ設定を更新して保存する
                var userSetting = await _userSettingJsonRepository.ReadAsync() ??
                 new UserSetting(
                    dockerPath: setupDto.DockerPath
                );

                var projectSetting = new ProjectSetting(
                    apiClientOutputFolderPath: "Assets/OpenAPIGenerator/Generated",
                    apiDocumentFilePathOrUrl: "http://localhost:8080",
                    generateProvider: setupDto.ProviderType
                );
                var generationCSharpSetting = new GenerationCSharpSetting();
                await _userSettingJsonRepository.SaveAsync(userSetting);
                await _projectSettingJsonRepository.SaveAsync(projectSetting);
                await _generationCSharpSettingJsonRepository.SaveAsync(generationCSharpSetting);
            }
            catch (Exception e) when (e is not ApplicationServiceException)
            {
                throw new ApplicationServiceException("セットアップユースケースに失敗しました。", e);
            }
        }
    }

}
