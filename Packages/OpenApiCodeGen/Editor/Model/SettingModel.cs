#nullable enable

using System;
using System.Threading.Tasks;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;

namespace ReBeat.OpenApiCodeGen.Model
{
    internal class SettingModel
    {
        public SettingSchema Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                OnChangeSettings?.Invoke(_settings);
            }
        }
        public bool? IsAvailableDockerPath
        {
            get => _isAvailableDockerPath;
            private set
            {
                _isAvailableDockerPath = value;
                OnChangeIsAvailableDockerPath?.Invoke(_isAvailableDockerPath);
            }
        }

        public Action<SettingSchema>? OnChangeSettings;
        public Action<bool?>? OnChangeIsAvailableDockerPath;
        SettingSchema _settings = new(new(), new());
        bool? _isAvailableDockerPath = null;

        readonly IRepository<GeneralConfigSchema> _generalConfigRepository;
        readonly IRepository<OpenApiCsharpOption> _openApiConfigRepository;

        public SettingModel()
        {
            _generalConfigRepository = new GeneralSettingJsonRepository();
            _openApiConfigRepository = new OpenApiCsharpSettingJsonRepository();

            FetchSettings();

            IsAvailableDockerPath = null;
        }

        public async Task SaveSettingAsync()
        {
            await _generalConfigRepository.SaveAsync(Settings.GeneralSettings);
            await _openApiConfigRepository.SaveAsync(Settings.OpenApiCsharpOption);
        }

        public async Task CheckRunnableDockerPath()
        {
            var dockerPath = Settings.GeneralSettings.DockerPath;
            var dockerProcess = new DockerProcess(path: dockerPath);

            var runResult = await dockerProcess.SendAsync("--version");
            var isAvailable = runResult.Status == ExitStatus.Success;
            IsAvailableDockerPath = isAvailable;
        }

        public void FetchSettings()
        {
            var generalConfig = _generalConfigRepository.Read();
            var openApiOption = _openApiConfigRepository.Read();

            Settings = new(generalConfig ?? new(), openApiOption ?? new());

        }
    }
}