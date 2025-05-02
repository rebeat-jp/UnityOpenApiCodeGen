#nullable enable

namespace ReBeat.OpenApiCodeGen.Core
{
    public class ProcessResponse
    {
        public ExitStatus Status { get; }
        public string Message { get; }

        public ProcessResponse(int exitStatus, string? message)
        {
            this.Status = (ExitStatus)exitStatus;
            this.Message = message ?? "";
        }

        public ProcessResponse(ExitStatus exitStatus, string? message)
        {
            this.Status = exitStatus;
            this.Message = message ?? "";
        }
    }
}