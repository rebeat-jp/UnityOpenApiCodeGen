#nullable enable
using System.Diagnostics;
using System;
namespace ReBeat.OpenApiCodeGen.Core
{
    public class SwaggerCodeGenerator : IGenerable
    {
        readonly GeneralConfigSchema _generalConfigSchema;
        public SwaggerCodeGenerator(GeneralConfigSchema config)
        {
            _generalConfigSchema = config;
        }

        public ProcessResponse Generate(string documentFilePath, string outputFolderPath)
        {
            if (string.IsNullOrEmpty(outputFolderPath))
            {
                throw new ArgumentException("invalid output folder path");
            }

            var currentDirectory = Environment.CurrentDirectory;

            var jarFilePath = $"{currentDirectory}/Packages/SwaggerCodeGen/Editor/Lib/swagger-codegen-cli-3.0.54.jar";

            var arguments = $"-jar \"{jarFilePath}\" generate -i \"{documentFilePath}\" -l csharp -o \"{outputFolderPath}\" ";
            var processInfo = new ProcessStartInfo
            {
                FileName = _generalConfigSchema.JavaPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var process = new Process { StartInfo = processInfo };
            try
            {
                process.Start();
                process.WaitForExit();

            }
            catch (Exception e)
            {

                return new ProcessResponse(128, e.Message);
            }
            var message = process.StandardOutput.ReadToEnd();
            return new ProcessResponse(process.ExitCode, message);
        }

    }
}
