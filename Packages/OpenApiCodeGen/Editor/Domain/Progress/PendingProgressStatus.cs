#nullable enable

using System;

namespace ReBeat.OpenApiCodeGen.UI
{
    internal class PendingProgressStatus : IProgressStatus
    {
        public double Progress { get; }

        public PendingProgressStatus(double progress)
        {
            Progress = progress switch
            {
                < 0.0 => 0.0,
                > 1.0 => 1.0,
                _ => progress
            };
        }
    }
}