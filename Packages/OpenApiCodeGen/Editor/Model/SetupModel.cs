#nullable enable

using System;
using System.IO;

using R3;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Dto;
using ReBeat.OpenApiCodeGen.Lib;
using ReBeat.OpenApiCodeGen.UI;

namespace ReBeat.OpenApiCodeGen.Model
{
    internal class SetupModel : IDisposable
    {
        public ReactiveProperty<SetupMenuDto> SetupMenuDto { get; }
        public ReadOnlyReactiveProperty<SetupStatus> Status => _status;

        readonly ReactiveProperty<SetupStatus> _status;

        readonly JsonRepository<GeneralConfigSchema> _generalSettingsRepository;
        readonly JsonRepository<OpenApiCsharpOption> _openApiSettingsRepository;


        public SetupModel()
        {
            _generalSettingsRepository = new(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Assets",
                    "OpenApiCodeGen",
                    "general.json")
                );
            _openApiSettingsRepository =
            new(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Assets",
                    "OpenApiCodeGen",
                    "openapi.json")
                    );
            SetupMenuDto = new(
                new SetupMenuDto(
                    GenerateProvider.OpenApi,
                    "")
                );

            _status = new(new(SetupStatusType.None));
        }
        public bool CheckRunnableDockerPath()
        {
            var dockerProcess = new DockerProcess(path: SetupMenuDto.CurrentValue.DockerPath);
            var sendResult = dockerProcess.Send("--version");
            return sendResult.Status == ExitStatus.Success;
        }


        public void Setup()
        {
            _status.Value = new(SetupStatusType.Pending);

            var isRunnable = CheckRunnableDockerPath();

            if (!isRunnable)
            {
                _status.Value = new(SetupStatusType.Error, "invalid docker path.");
                return;
            }

            var generalConfigSchema = new GeneralConfigSchema(
                generateProvider: SetupMenuDto.CurrentValue.GenerateProvider,
                dockerPath: SetupMenuDto.CurrentValue.DockerPath, "", "");

            _generalSettingsRepository?.Save(
                generalConfigSchema
                );
            var openApiConfigSchema = new OpenApiCsharpOption();
            _openApiSettingsRepository?.Save(openApiConfigSchema);

            _status.Value = new(SetupStatusType.Success, "setup is success!");
        }

        public void Dispose()
        {
            SetupMenuDto.Dispose();
            _status.Dispose();
        }
    }
}