#nullable enable
namespace Rhycol.OpenApiCodeGen.UI
{
    internal class SucceedProgressStatus : IProgressStatus
    {
        public double Progress => 1.0;
        public string Message { get; }

        public SucceedProgressStatus(string? message = null)
        {
            Message = message ?? string.Empty;
        }
    }
}
