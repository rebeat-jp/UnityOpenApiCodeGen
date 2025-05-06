#nullable enable
using System;
using System.IO;
using System.Text;

using Cysharp.Threading.Tasks;

using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.Lib
{
    internal class OpenApiCodeGenerator : IGenerable
    {

        public OpenApiCodeGenerator()
        {
        }

        public ProcessResponse Generate(SettingSchema settingSchema)
        {
            var generalConfigSchema = settingSchema.GeneralSettings;
            var openApiConfigSchema = settingSchema.OpenApiCsharpOption;



            if (openApiConfigSchema == null)
            {
                return new ProcessResponse(ExitStatus.FatalError, "Failed to load OpenAPI config data.");
            }
            if (!Directory.Exists(generalConfigSchema.ApiClientOutputFolderPath))
            {
                Directory.CreateDirectory(generalConfigSchema.ApiClientOutputFolderPath);
            }

            var dockerProcess = new DockerProcess
            {
                Path = generalConfigSchema.DockerPath
            };
            var openApiConfigJsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "OpenApiCodeGen", "openapi.json");

            var argumentsBuilder = new StringBuilder(1000);
            argumentsBuilder.Append("run --rm");
            argumentsBuilder.Append($"-v \"{generalConfigSchema.ApiClientOutputFolderPath}:/local\" ");
            argumentsBuilder.Append($"-v \"{openApiConfigJsonFilePath}:/config/config.json\" ");
            argumentsBuilder.Append("openapitools/openapi-generator-cli generate ");
            argumentsBuilder.Append($"-i \"{generalConfigSchema.ApiDocumentFilePathOrUrl}\" ");
            argumentsBuilder.Append("-g \"csharp\" -o \"/local\" ");
            argumentsBuilder.Append("-c /config/config.json");

            return dockerProcess.Send(argumentsBuilder.ToString());

        }

        public async UniTask<ProcessResponse> GenerateAsync(SettingSchema settingSchema)
        {
            var generalConfigSchema = settingSchema.GeneralSettings;
            var openApiConfigSchema = settingSchema.OpenApiCsharpOption;

            if (openApiConfigSchema == null)
            {
                return new ProcessResponse(ExitStatus.FatalError, "Failed to load OpenAPI config data.");
            }
            if (!Directory.Exists(generalConfigSchema.ApiClientOutputFolderPath))
            {
                Directory.CreateDirectory(generalConfigSchema.ApiClientOutputFolderPath);
            }

            var dockerProcess = new DockerProcess
            {
                Path = generalConfigSchema.DockerPath
            };
            var openApiConfigJsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "OpenApiCodeGen", "openapi.json");

            var argumentsBuilder = new StringBuilder(1000);
            argumentsBuilder.Append("run --rm");
            argumentsBuilder.Append($"-v \"{generalConfigSchema.ApiClientOutputFolderPath}:/local\" ");
            argumentsBuilder.Append($"-v \"{openApiConfigJsonFilePath}:/config/config.json\" ");
            argumentsBuilder.Append("openapitools/openapi-generator-cli generate ");
            argumentsBuilder.Append($"-i \"{generalConfigSchema.ApiDocumentFilePathOrUrl}\" ");
            argumentsBuilder.Append("-g \"csharp\" -o \"/local\" ");
            argumentsBuilder.Append("-c /config/config.json");

            return await dockerProcess.SendAsync(argumentsBuilder.ToString());

        }
    }
}
