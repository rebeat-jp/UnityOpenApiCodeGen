#nullable enable
using System;
using System.IO;

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
            var arguments =
            $"run --rm -v \"{generalConfigSchema.ApiClientOutputFolderPath}:/local\" openapitools/openapi-generator-cli generate -i \"{generalConfigSchema.ApiDocumentFilePathOrUrl}\" -g \"csharp\" -o \"/local\"";

            return dockerProcess.Send(arguments);

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
            var arguments =
            $"run --rm -v \"{generalConfigSchema.ApiClientOutputFolderPath}:/local\" openapitools/openapi-generator-cli generate -i \"{generalConfigSchema.ApiDocumentFilePathOrUrl}\" -g \"csharp\" -o \"/local\"";

            return await dockerProcess.SendAsync(arguments);

        }
    }
}
