#nullable enable

using System;

using Cysharp.Threading.Tasks;

using R3;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;

namespace ReBeat.OpenApiCodeGen.Model
{
    internal class SettingModel : IDisposable
    {
        public ReactiveProperty<SettingSchema> Settings { get; }
        public ReadOnlyReactiveProperty<bool?> IsAvailableDockerPath => _isAvailableDockerPath;

        readonly ReactiveProperty<bool?> _isAvailableDockerPath;

        readonly IRepository<GeneralConfigSchema> _generalConfigRepository;
        readonly IRepository<OpenApiCsharpOption> _openApiConfigRepository;

        public SettingModel()
        {
            _generalConfigRepository = new GeneralSettingJsonRepository();
            _openApiConfigRepository = new OpenApiCsharpSettingJsonRepository();


            var initGeneralConfig = _generalConfigRepository.Read();
            var initOpenApiOption = _openApiConfigRepository.Read();

            Settings = new(
                new(
                    initGeneralConfig ?? new(),
                    initOpenApiOption ?? new()
                    ));
            _isAvailableDockerPath = new(null);
        }

        public async UniTask SaveSettingAsync()
        {
            await _generalConfigRepository.SaveAsync(Settings.CurrentValue.GeneralSettings);
            await _openApiConfigRepository.SaveAsync(Settings.CurrentValue.OpenApiCsharpOption);
        }

        public async UniTask CheckRunnableDockerPath()
        {
            var dockerPath = Settings.CurrentValue.GeneralSettings.DockerPath;
            var dockerProcess = new DockerProcess(path: dockerPath);

            var runResult = await dockerProcess.SendAsync("--version");
            var isAvailable = runResult.Status == ExitStatus.Success;
            _isAvailableDockerPath.Value = isAvailable;
        }

        public void Dispose()
        {
            _isAvailableDockerPath.Dispose();
            Settings.Dispose();
        }
    }
}