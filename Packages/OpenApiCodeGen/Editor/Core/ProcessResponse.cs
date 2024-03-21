#nullable enable

namespace ReBeat.OpenApiCodeGen.Core
{
    public class ProcessResponse
    {
        public int ExitStatus { get; }
        public string Message { get; }

        public ProcessResponse(int exitStatus, string? message)
        {
            this.ExitStatus = exitStatus;
            this.Message = message ?? "";
        }
    }
}