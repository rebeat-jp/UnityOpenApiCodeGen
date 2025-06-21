#nullable enable

using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ReBeat.OpenApiCodeGen.Core
{
    public class DockerProcess
    {
        public string Path { get; set; }
        public bool UseShellExecute { get; set; }
        public bool RedirectStandardOutput { get; set; }
        public bool RedirectStandardError { get; set; }

        public DockerProcess(
            string path = "docker",
            bool useShellExecute = false,
            bool redirectStandardOutput = true,
            bool redirectStandardError = true)
        {
            Path = path;
            UseShellExecute = useShellExecute;
            RedirectStandardOutput = redirectStandardOutput;
            RedirectStandardError = redirectStandardError;
        }

        public ProcessResponse Send(string argumentsSeparateWithSpace)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = Path,
                Arguments = argumentsSeparateWithSpace,
                UseShellExecute = UseShellExecute,
                RedirectStandardOutput = RedirectStandardOutput,
                RedirectStandardError = RedirectStandardError
            };
            using var process = new Process();

            var outputText = "";
            var errorText = "";

            process.StartInfo = processStartInfo;
            try
            {
                process.Start();

                outputText = process.StandardOutput.ReadToEnd();
                errorText = process.StandardError.ReadToEnd();

                process.WaitForExit();
            }
            catch (Exception e)
            {
                return new ProcessResponse(128, e.Message);
            }

            return new ProcessResponse(
                !string.IsNullOrEmpty(outputText) ? ExitStatus.Success : ExitStatus.Error,
                !string.IsNullOrEmpty(outputText) ? outputText : errorText
            );

        }

        public async Task<ProcessResponse> SendAsync(string argumentsSeparateWithSpace)
        {
            var result = await Task.Run(() => Send(argumentsSeparateWithSpace));
            return result;
        }
    }
}