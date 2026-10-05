using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class UnsupportedNormalizedSpecVersionException : Exception
    {
        internal UnsupportedNormalizedSpecVersionException(string version, int line, int column)
            : base("Unsupported normalized spec bundle format version '" + version + "'.")
        {
            Version = version;
            Line = line;
            Column = column;
        }

        internal string Version { get; }

        internal int Line { get; }

        internal int Column { get; }
    }
}
