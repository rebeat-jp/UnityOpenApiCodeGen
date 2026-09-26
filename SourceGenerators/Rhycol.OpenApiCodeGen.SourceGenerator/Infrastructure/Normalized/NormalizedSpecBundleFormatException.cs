using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class NormalizedSpecBundleFormatException : FormatException
    {
        internal NormalizedSpecBundleFormatException(string message, int line, int column)
            : base(message)
        {
            Line = line;
            Column = column;
        }

        internal int Line { get; }

        internal int Column { get; }
    }
}
