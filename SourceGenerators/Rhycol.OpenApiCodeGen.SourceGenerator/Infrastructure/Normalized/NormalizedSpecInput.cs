using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal readonly struct NormalizedSpecInput : IEquatable<NormalizedSpecInput>
    {
        internal NormalizedSpecInput(string path, string content)
        {
            Path = path;
            Content = content;
        }

        internal string Path { get; }

        internal string Content { get; }

        public bool Equals(NormalizedSpecInput other)
        {
            return string.Equals(Path, other.Path, StringComparison.Ordinal) &&
                   string.Equals(Content, other.Content, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is NormalizedSpecInput other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Path == null ? 0 : StringComparer.Ordinal.GetHashCode(Path)) * 397) ^
                       (Content == null ? 0 : StringComparer.Ordinal.GetHashCode(Content));
            }
        }
    }
}
