#nullable enable

using System;
using System.IO;

using Cysharp.Threading.Tasks;

using R3;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;
using ReBeat.OpenApiCodeGen.UI;

namespace ReBeat.OpenApiCodeGen.Model
{
    public class GenerateModel : IDisposable
    {
        public ReactiveProperty<GenerateMenuDto> GenerateDto { get; }
        public ReadOnlyReactiveProperty<GenerateStatus> Status => _status;

        readonly ReactiveProperty<GenerateStatus> _status;
        readonly IRepository<GeneralConfigSchema> _generalSettingsRepository;
        readonly IRepository<OpenApiCsharpOption> _openApiSettingsRepository;

        public GenerateModel()
        {
            _generalSettingsRepository = new GeneralSettingJsonRepository();
            _openApiSettingsRepository = new OpenApiCsharpSettingJsonRepository();

            var generalConfig = _generalSettingsRepository.Read();
            var initialDto = generalConfig != null
                ? new GenerateMenuDto(
                    generateProvider: generalConfig.GenerateProvider,
                    apiDocumentFilePathOrUrl: generalConfig.ApiDocumentFilePathOrUrl,
                    apiClientOutputFolderPath: generalConfig.ApiClientOutputFolderPath
                    )
                : new(GenerateProvider.OpenApi, "", "");
            GenerateDto = new(initialDto);

            _status = new(
                new(GenerateStatusType.None, 0));
        }

        public GeneralConfigSchema GetGenerateConfig()
        {
            return _generalSettingsRepository.Read()
            ?? new GeneralConfigSchema();

        }

        public async UniTask GenerateAsync()
        {
            _status.Value = new(GenerateStatusType.Pending, 10);

            var generalConfig = _generalSettingsRepository.Read();
            var openApiConfig = _openApiSettingsRepository.Read();

            if (generalConfig == null || openApiConfig == null)
            {
                _status.Value = new(GenerateStatusType.Error, 1, "Config is not found. Please finish to setup.");
                return;
            }

            IGenerable? generable = generalConfig.GenerateProvider switch
            {
                GenerateProvider.OpenApi => new OpenApiCodeGenerator(),
                _ => null,
            };

            if (generable == null)
            {
                _status.Value = new(GenerateStatusType.Error, 1, "Fatal Error. not implimented function");
                return;
            }

            generalConfig.ApiDocumentFilePathOrUrl = GenerateDto.Value.ApiDocumentFilePathOrUrl;
            generalConfig.ApiClientOutputFolderPath = GenerateDto.Value.ApiClientOutputFolderPath;

            var settingSchema = new SettingSchema(generalConfig, openApiConfig);

            _status.Value = new(GenerateStatusType.Pending, 50f, "Generating...");
            var response = await generable.GenerateAsync(settingSchema);

            var isSuccess = response.Status == ExitStatus.Success;

            _status.Value = isSuccess
                ? new(GenerateStatusType.Success, 100, "Generating is Success")
                : new(GenerateStatusType.Error, 0, response.Message);
        }

        public void Dispose()
        {
            _status.Dispose();
        }

    }
}