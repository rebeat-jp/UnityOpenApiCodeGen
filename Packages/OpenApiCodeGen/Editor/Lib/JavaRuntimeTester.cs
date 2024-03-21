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
            try
            {
                process.Start();
                process.WaitForExit();

            }
            catch (System.Exception e)
            {

                return new ProcessResponse(128, e.Message);
            }
            var message = process.StandardOutput.ReadToEnd();
            var errorMessage = process.StandardOutput.ReadToEnd();
            return new ProcessResponse(process.ExitCode, process.ExitCode == 0 ? message : errorMessage);
        }

    }
}
