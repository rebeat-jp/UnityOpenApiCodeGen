#nullable enable
namespace Rhycol.OpenApiCodeGen.UI
{
    internal sealed class WarningProgressStatus : IProgressStatus
    {
        internal WarningProgressStatus(string message) { Message = message ?? string.Empty; }
        internal string Message { get; }
        public double Progress => 1.0;
    }
}
