#nullable enable

using System;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Dto;
using ReBeat.OpenApiCodeGen.Lib;
using ReBeat.OpenApiCodeGen.UI;

namespace ReBeat.OpenApiCodeGen.Model
{
    internal class SetupModel
    {
        public SetupMenuDto SetupMenuDto
        {
            get => _setupMenuDto;
            set
            {
                _setupMenuDto = value;
                OnChangeDto?.Invoke(_setupMenuDto);
            }
        }
        public SetupStatus Status
        {
            get => _status;
            private set
            {
                _status = value;
                OnChangeStatus?.Invoke(_status);
            }
        }

        public Action<SetupMenuDto>? OnChangeDto;
        public Action<SetupStatus>? OnChangeStatus;

        SetupMenuDto _setupMenuDto = new(GenerateProvider.OpenApi, "");
        SetupStatus _status = new(SetupStatusType.None);

        readonly IRepository<GeneralConfigSchema> _generalSettingsRepository;
        readonly IRepository<OpenApiCsharpOption> _openApiSettingsRepository;


        public SetupModel()
        {
            _generalSettingsRepository = new GeneralSettingJsonRepository();
            _openApiSettingsRepository = new OpenApiCsharpSettingJsonRepository();
            SetupMenuDto = new SetupMenuDto(GenerateProvider.OpenApi, "");

            Status = new(SetupStatusType.None);
        }
        public bool CheckRunnableDockerPath()
        {
            var dockerProcess = new DockerProcess(path: SetupMenuDto.DockerPath);
            var sendResult = dockerProcess.Send("--version");
            return sendResult.Status == ExitStatus.Success;
        }


        public void Setup()
        {
            Status = new(SetupStatusType.Pending);

            var isRunnable = CheckRunnableDockerPath();

            if (!isRunnable)
            {
                Status = new(SetupStatusType.Error, "invalid docker path.");
                return;
            }

            var generalConfigSchema = new GeneralConfigSchema(
                generateProvider: SetupMenuDto.GenerateProvider,
                dockerPath: SetupMenuDto.DockerPath, "", "");

            _generalSettingsRepository?.Save(
                generalConfigSchema
                );
            var openApiConfigSchema = new OpenApiCsharpOption();
            _openApiSettingsRepository?.Save(openApiConfigSchema);

            Status = new(SetupStatusType.Success, "setup is success!");
        }
    }
}