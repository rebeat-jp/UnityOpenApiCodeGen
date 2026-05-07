#nullable enable
namespace Rhycol.OpenApiCodeGen.UI
{
    internal class FailedProgressStatus : IProgressStatus
    {
        public double Progress => 0.0;
        /// <summary>
        /// 失敗理由
        /// </summary>
        public string? Reason { get; }

        public FailedProgressStatus(string? reason = null)
        {
            Reason = reason;
        }
    }
}