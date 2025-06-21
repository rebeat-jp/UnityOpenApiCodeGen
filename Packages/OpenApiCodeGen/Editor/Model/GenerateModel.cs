#nullable enable

using System;
using System.Threading.Tasks;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;
using ReBeat.OpenApiCodeGen.UI;

namespace ReBeat.OpenApiCodeGen.Model
{
    public class GenerateModel
    {
        public GenerateMenuDto GenerateDto
        {
            get => _generateMenuDto;
            set
            {
                _generateMenuDto = value;
                OnChangedDto?.Invoke(_generateMenuDto);
            }
        }
        public GenerateStatus Status
        {
            get => _status;
            private set
            {
                _status = value;
                OnChangeStatus?.Invoke(_status);
            }
        }
        public Action<GenerateMenuDto>? OnChangedDto;
        public Action<GenerateStatus>? OnChangeStatus;

        GenerateStatus _status = new(GenerateStatusType.None, 0);
        GenerateMenuDto _generateMenuDto = new(GenerateProvider.OpenApi, "", "");
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
            GenerateDto = initialDto;

            Status = new(GenerateStatusType.None, 0);
        }

        public void FetchGenerateConfig()
        {
            var configSchema = _generalSettingsRepository.Read() ?? new GeneralConfigSchema();
            GenerateDto = new(
                configSchema.GenerateProvider,
                configSchema.ApiDocumentFilePathOrUrl,
                configSchema.ApiClientOutputFolderPath);

        }

        public async Task GenerateAsync()
        {
            Status = new(GenerateStatusType.Pending, 10);

            var generalConfig = _generalSettingsRepository.Read();
            var openApiConfig = _openApiSettingsRepository.Read();

            if (generalConfig == null || openApiConfig == null)
            {
                Status = new(GenerateStatusType.Error, 1, "Config is not found. Please finish to setup.");
                return;
            }

            IGenerable? generable = generalConfig.GenerateProvider switch
            {
                GenerateProvider.OpenApi => new OpenApiCodeGenerator(),
                _ => null,
            };

            if (generable == null)
            {
                Status = new(GenerateStatusType.Error, 1, "Fatal Error. not implimented function");
                return;
            }

            generalConfig.ApiDocumentFilePathOrUrl = GenerateDto.ApiDocumentFilePathOrUrl;
            generalConfig.ApiClientOutputFolderPath = GenerateDto.ApiClientOutputFolderPath;

            var settingSchema = new SettingSchema(generalConfig, openApiConfig);

            Status = new(GenerateStatusType.Pending, 50f, "Generating...");
            var response = await generable.GenerateAsync(settingSchema);

            var isSuccess = response.Status == ExitStatus.Success;

            Status = isSuccess
                ? new(GenerateStatusType.Success, 100, "Generating is Success")
                : new(GenerateStatusType.Error, 0, response.Message);
        }

    }
}