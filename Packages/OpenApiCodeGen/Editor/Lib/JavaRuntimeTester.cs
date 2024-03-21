using System.Diagnostics;
using System.Text;

using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.Lib
{

    class JavaRuntimeTester
    {

        public ProcessResponse Test(string javaPath)
        {
            if (string.IsNullOrEmpty(javaPath))
            {
                return new ProcessResponse(128, "cannot begin to work process");
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = $"--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true

            };
            using var process = new Process { StartInfo = processInfo };
            var message = new StringBuilder(100);
            process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Debug.WriteLine(e.Data);
                            message.Append(e.Data);
                        }
                    };
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.WaitForExit();

            }
            catch (System.Exception e)
            {

                return new ProcessResponse(128, e.Message);
            }
            return new ProcessResponse(process.ExitCode, $"{message}");
        }

    }
}
