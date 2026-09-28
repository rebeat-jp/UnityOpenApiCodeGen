#nullable enable
namespace Rhycol.OpenApiCodeGen.UI
{
    internal sealed class CanceledProgressStatus : IProgressStatus
    {
        internal CanceledProgressStatus(string message = "Generating was canceled.") { Message = message; }
        internal string Message { get; }
        public double Progress => 0.0;
    }
}
