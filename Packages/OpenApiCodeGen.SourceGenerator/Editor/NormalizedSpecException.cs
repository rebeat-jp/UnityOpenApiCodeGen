using System;
using System.Globalization;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class NormalizedSpecException : Exception
    {
        internal NormalizedSpecException(
            string message,
            string sourcePath,
            int line,
            int column,
            string logicalPath,
            Exception innerException = null)
            : base(FormatMessage(message, sourcePath, line, column, logicalPath), innerException)
        {
            SourcePath = sourcePath;
            Line = line;
            Column = column;
            LogicalPath = logicalPath;
        }

        internal string SourcePath { get; }

        internal int Line { get; }

        internal int Column { get; }

        internal string LogicalPath { get; }

        private static string FormatMessage(
            string message,
            string sourcePath,
            int line,
            int column,
            string logicalPath)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} ({1}:{2}:{3}, JSON Pointer '{4}')",
                message,
                sourcePath,
                line,
                column,
                logicalPath);
        }
    }
}
