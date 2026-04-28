#nullable enable
namespace ReBeat.OpenApiCodeGen.UI
{
    internal interface IProgressStatus
    {
        /// <summary>
        /// 0.0 ~ 100.0の進捗率
        /// </summary>
        public double Progress { get; }
    }
}