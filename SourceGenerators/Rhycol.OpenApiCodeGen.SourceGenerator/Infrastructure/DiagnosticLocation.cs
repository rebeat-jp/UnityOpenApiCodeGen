using System;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal readonly struct DiagnosticLocation : IEquatable<DiagnosticLocation>
    {
        private DiagnosticLocation(
            string path,
            TextSpan sourceSpan,
            LinePositionSpan lineSpan,
            Location originalLocation,
            bool hasValue)
        {
            Path = path;
            SourceSpan = sourceSpan;
            LineSpan = lineSpan;
            OriginalLocation = originalLocation;
            HasValue = hasValue;
        }

        private string Path { get; }

        private TextSpan SourceSpan { get; }

        private LinePositionSpan LineSpan { get; }

        private Location? OriginalLocation { get; }

        private bool HasValue { get; }

        internal static DiagnosticLocation FromLocation(Location location)
        {
            if (location == Location.None)
            {
                return default;
            }

            FileLinePositionSpan lineSpan = location.GetLineSpan();
            return new DiagnosticLocation(
                lineSpan.Path ?? string.Empty,
                location.SourceSpan,
                lineSpan.Span,
                location,
                true);
        }

        internal Location ToLocation()
        {
            if (!HasValue)
            {
                return Location.None;
            }

            return OriginalLocation ?? Location.Create(Path, SourceSpan, LineSpan);
        }

        public bool Equals(DiagnosticLocation other)
        {
            return HasValue == other.HasValue &&
                   string.Equals(Path, other.Path, StringComparison.Ordinal) &&
                   SourceSpan.Equals(other.SourceSpan) &&
                   LineSpan.Equals(other.LineSpan) &&
                   Equals(OriginalLocation, other.OriginalLocation);
        }

        public override bool Equals(object? obj)
        {
            return obj is DiagnosticLocation other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = HasValue ? 1 : 0;
                hashCode = (hashCode * 397) ^ (Path == null ? 0 : StringComparer.Ordinal.GetHashCode(Path));
                hashCode = (hashCode * 397) ^ SourceSpan.GetHashCode();
                hashCode = (hashCode * 397) ^ LineSpan.GetHashCode();
                hashCode = (hashCode * 397) ^ (OriginalLocation == null ? 0 : OriginalLocation.GetHashCode());
                return hashCode;
            }
        }
    }
}
