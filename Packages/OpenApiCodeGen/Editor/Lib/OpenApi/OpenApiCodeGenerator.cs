#nullable enable
using System;
using System.Diagnostics;

using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.Lib
{
    class OpenApiCodeGenerator : IGenerable
    {

        public static readonly string JarFilePath = $"{Environment.CurrentDirectory}/Packages/SwaggerCodeGen/Editor/Lib/OpenApi/openapi-generator-cli-7.3.0.jar";
        readonly GeneralConfigSchema _generalConfigSchema;
        public OpenApiCodeGenerator(GeneralConfigSchema config)
        {
            _generalConfigSchema = config;
        }

        public ProcessResponse Generate(string documentFilePath, string outputFolderPath)
        {
            if (string.IsNullOrEmpty(outputFolderPath))
            {
                throw new ArgumentException("invalid output folder path");
            }


            var arguments = $"-jar \"{JarFilePath}\" generate -i \"{documentFilePath}\" -g csharp -c \"{OpenApiConfigSchema.ConfigFilePath}\" -o \"{outputFolderPath}\" ";

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
            return new ProcessResponse(process.ExitCode, arguments);
        }
    }
}
