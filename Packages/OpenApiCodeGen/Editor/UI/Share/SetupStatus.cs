#nullable enable

namespace ReBeat.OpenApiCodeGen.UI
{
    internal class SetupStatus
    {
        public SetupStatusType Type { get; private set; }
        public string? Message { get; private set; }

        public SetupStatus(SetupStatusType type, string? message = null)
        {
            Type = type;
            Message = message;
        }
    }
}