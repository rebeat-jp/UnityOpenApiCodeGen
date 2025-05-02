#nullable enable

namespace ReBeat.OpenApiCodeGen.UI
{
    public class GenerateStatus
    {
        public GenerateStatusType Type { get; private set; }
        public float Progress { get; private set; }
        public string? Message { get; private set; }

        public GenerateStatus(GenerateStatusType type, float progress, string? message = null)
        {
            Type = type;
            Progress = progress;
            Message = message;
        }

    }
}