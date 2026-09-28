using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal readonly struct NormalizedSpecCandidate : IEquatable<NormalizedSpecCandidate>
    {
        private NormalizedSpecCandidate(
            CandidateStatus status,
            NormalizedSpecInput input,
            NormalizedSpecBundle? bundle,
            string detail,
            string version,
            string fileSpecId,
            int line,
            int column)
        {
            Status = status;
            Input = input;
            Bundle = bundle;
            Detail = detail;
            Version = version;
            FileSpecId = fileSpecId;
            Line = line;
            Column = column;
        }

        internal enum CandidateStatus
        {
            Valid,
            Malformed,
            UnsupportedVersion,
            SpecIdMismatch
        }

        internal CandidateStatus Status { get; }

        internal NormalizedSpecInput Input { get; }

        internal NormalizedSpecBundle? Bundle { get; }

        internal string Detail { get; }

        internal string Version { get; }

        internal string FileSpecId { get; }

        internal int Line { get; }

        internal int Column { get; }

        internal bool HasParsedBundle =>
            Status == CandidateStatus.Valid || Status == CandidateStatus.SpecIdMismatch;

        internal static NormalizedSpecCandidate CreateValid(
            NormalizedSpecInput input,
            NormalizedSpecBundle bundle,
            string fileSpecId)
        {
            return new NormalizedSpecCandidate(
                CandidateStatus.Valid,
                input,
                bundle,
                string.Empty,
                string.Empty,
                fileSpecId,
                1,
                1);
        }

        internal static NormalizedSpecCandidate CreateMalformed(
            NormalizedSpecInput input,
            string detail,
            int line,
            int column)
        {
            return new NormalizedSpecCandidate(
                CandidateStatus.Malformed,
                input,
                null,
                detail,
                string.Empty,
                string.Empty,
                line,
                column);
        }

        internal static NormalizedSpecCandidate CreateUnsupportedVersion(
            NormalizedSpecInput input,
            string version,
            int line,
            int column)
        {
            return new NormalizedSpecCandidate(
                CandidateStatus.UnsupportedVersion,
                input,
                null,
                string.Empty,
                version,
                string.Empty,
                line,
                column);
        }

        internal static NormalizedSpecCandidate CreateSpecIdMismatch(
            NormalizedSpecInput input,
            NormalizedSpecBundle bundle,
            string fileSpecId)
        {
            return new NormalizedSpecCandidate(
                CandidateStatus.SpecIdMismatch,
                input,
                bundle,
                string.Empty,
                string.Empty,
                fileSpecId,
                1,
                1);
        }

        public bool Equals(NormalizedSpecCandidate other)
        {
            return Status == other.Status &&
                   Input.Equals(other.Input) &&
                   string.Equals(Detail, other.Detail, StringComparison.Ordinal) &&
                   string.Equals(Version, other.Version, StringComparison.Ordinal) &&
                   string.Equals(FileSpecId, other.FileSpecId, StringComparison.Ordinal) &&
                   Line == other.Line &&
                   Column == other.Column;
        }

        public override bool Equals(object? obj)
        {
            return obj is NormalizedSpecCandidate other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Status;
                hashCode = (hashCode * 397) ^ Input.GetHashCode();
                hashCode = (hashCode * 397) ^ GetOrdinalHashCode(Detail);
                hashCode = (hashCode * 397) ^ GetOrdinalHashCode(Version);
                hashCode = (hashCode * 397) ^ GetOrdinalHashCode(FileSpecId);
                hashCode = (hashCode * 397) ^ Line;
                hashCode = (hashCode * 397) ^ Column;
                return hashCode;
            }
        }

        private static int GetOrdinalHashCode(string? value)
        {
            return value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);
        }
    }
}
