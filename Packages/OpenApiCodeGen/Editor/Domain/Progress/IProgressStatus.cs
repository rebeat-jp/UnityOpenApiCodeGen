#nullable enable
namespace Rhycol.OpenApiCodeGen.UI
{
    internal interface IProgressStatus
    {
        /// <summary>
        /// 0.0 ~ 1.0の進捗率
        /// </summary>
        public double Progress { get; }
    }
}