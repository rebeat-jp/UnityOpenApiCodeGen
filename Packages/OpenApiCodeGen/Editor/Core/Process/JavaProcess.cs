#nullable enable

using System.Diagnostics;

namespace ReBeat.OpenApiCodeGen.Core
{
    public class JavaProcess
    {
        public string Path { get; set; }
        public bool UseShellExecute { get; set; } = false;
        public bool RedirectStandardOutput { get; set; } = true;
        public bool RedirectStandardError { get; set; } = true;

        public JavaProcess(bool? useShellExecute = null, bool? redirectStandardOutput = null, bool? redirectStandardError = null)
        {
            Path = "java";
            UseShellExecute = useShellExecute ?? UseShellExecute;
            RedirectStandardOutput = redirectStandardOutput ?? RedirectStandardOutput;
            RedirectStandardError = redirectStandardError ?? RedirectStandardError;
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
            process.StartInfo = processStartInfo;
            try
            {
                process.Start();
                process.WaitForExit();
            }
            catch (System.Exception)
            {
                return new ProcessResponse(128, "Java is not found. Please try to see environments settings or set docker full path.");
            }

            using var outputStream = process.StandardOutput;
            using var errorStream = process.StandardError;
            var outputText = outputStream.ReadToEnd();
            var errorText = errorStream.ReadToEnd();

            return new ProcessResponse(
                !string.IsNullOrEmpty(outputText) ? ExitStatus.Success : ExitStatus.Error,
                !string.IsNullOrEmpty(outputText) ? outputText : errorText
            );
        }
    }
}