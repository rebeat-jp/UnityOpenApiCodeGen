#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Rhycol.OpenApiCodeGen.Core
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

                var outputTask = RedirectStandardOutput
                    ? process.StandardOutput.ReadToEndAsync()
                    : Task.FromResult("");
                var errorTask = RedirectStandardError
                    ? process.StandardError.ReadToEndAsync()
                    : Task.FromResult("");

                process.WaitForExit();
                outputText = outputTask.GetAwaiter().GetResult();
                errorText = errorTask.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                throw new ExternalServiceException("Dockerプロセスの起動または実行に失敗しました。", e);
            }

            var message = string.IsNullOrEmpty(errorText)
                ? outputText
                : string.Concat(outputText, string.IsNullOrEmpty(outputText) ? "" : Environment.NewLine, errorText);

            return new ProcessResponse(
                process.ExitCode == 0 ? ExitStatus.Success : ExitStatus.Error,
                message
            );

        }

        public async Task<ProcessResponse> SendAsync(string argumentsSeparateWithSpace, CancellationToken cancellationToken = default)
        {
            var result = await Task.Run(() => Send(argumentsSeparateWithSpace), cancellationToken);
            return result;
        }
    }
}
