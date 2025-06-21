#nullable enable
using System;
using System.Threading.Tasks;
namespace ReBeat.OpenApiCodeGen.Core
{
    internal class SwaggerCodeGenerator : IGenerable
    {
        readonly GeneralConfigSchema _generalConfigSchema;
        public SwaggerCodeGenerator(GeneralConfigSchema config)
        {
            _generalConfigSchema = config;
        }

        public ProcessResponse Generate(SettingSchema settingSchema)
        {
            var outputFolderPath = settingSchema.GeneralSettings.ApiClientOutputFolderPath;
            var documentFilePath = settingSchema.GeneralSettings.ApiDocumentFilePathOrUrl;
            if (string.IsNullOrEmpty(outputFolderPath))
            {
                throw new ArgumentException("invalid output folder path");
            }

            var jarFilePath = $"{Environment.CurrentDirectory}/Packages/SwaggerCodeGen/Editor/Lib/swagger-codegen-cli-3.0.54.jar";
            var arguments = $"-jar \"{jarFilePath}\" generate -i \"{documentFilePath}\" -l csharp -o \"{outputFolderPath}\" ";

            var javaProcess = new JavaProcess();
            return javaProcess.Send(arguments);
        }

        public async Task<ProcessResponse> GenerateAsync(SettingSchema settingSchema)
        {
            return await Task.Run(() => Generate(settingSchema));
        }
    }

}
