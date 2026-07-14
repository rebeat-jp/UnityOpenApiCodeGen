using System.Threading.Tasks;
using System;

namespace Rhycol.OpenApiCodeGen.Core
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
                await EnsureDockerInstalledAsync(checkDockerInstalledDto.DockerPath);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task SetupAsync(SetupDto setupDto)
        {
            try
            {
                await EnsureDockerInstalledAsync(setupDto.DockerPath);
                var userSetting = new UserSetting(dockerPath: setupDto.DockerPath);
                var existingProjectSetting = await _projectSettingJsonRepository.ReadAsync();
                var existingGenerationCSharpSetting =
                    await _generationCSharpSettingJsonRepository.ReadAsync();

                await _userSettingJsonRepository.SaveAsync(userSetting);
                if (existingProjectSetting == null)
                {
                    await _projectSettingJsonRepository.SaveAsync(new ProjectSetting(
                        apiClientOutputFolderPath: "Assets/OpenAPIGenerator/Generated",
                        apiDocumentFilePathOrUrl: "http://localhost:8080"));
                }

                if (existingGenerationCSharpSetting == null)
                {
                    await _generationCSharpSettingJsonRepository.SaveAsync(
                        new GenerationCSharpSetting());
                }
            }
            catch (Exception e) when (e is not ApplicationServiceException)
            {
                throw new ApplicationServiceException("セットアップユースケースに失敗しました。", e);
            }
        }

        async Task EnsureDockerInstalledAsync(string dockerPath)
        {
            try
            {
                var dockerProcess = new DockerProcess(path: dockerPath);
                var res = await dockerProcess.SendAsync("--version");

                if (res.Status != ExitStatus.Success)
                {
                    throw new ApplicationServiceException("Dockerがインストールされていないか、パスが間違っています。");
                }
            }
            catch (ExternalServiceException)
            {
                throw new ApplicationServiceException("Dockerがインストールされていないか、パスが間違っています。");
            }
            catch (Exception e)
            {
                throw new ApplicationServiceException("Dockerインストール確認ユースケースに失敗しました。", e);
            }
        }
    }

}
