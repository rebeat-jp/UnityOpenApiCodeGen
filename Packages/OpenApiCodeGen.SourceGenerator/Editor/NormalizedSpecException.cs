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
            : this(message, sourcePath, line, column, logicalPath, string.Empty, innerException)
        {
        }

        internal NormalizedSpecException(
            string message,
            string sourcePath,
            int line,
            int column,
            string logicalPath,
            string diagnosticCode,
            Exception innerException = null)
            : base(FormatMessage(message, sourcePath, line, column, logicalPath, diagnosticCode), innerException)
        {
            SourcePath = sourcePath;
            Line = line;
            Column = column;
            LogicalPath = logicalPath;
            DiagnosticCode = diagnosticCode ?? string.Empty;
        }

        internal string SourcePath { get; }

        internal int Line { get; }

        internal int Column { get; }

        internal string LogicalPath { get; }

        internal string DiagnosticCode { get; }

        private static string FormatMessage(
            string message,
            string sourcePath,
            int line,
            int column,
            string logicalPath,
            string diagnosticCode)
        {
            if (!string.IsNullOrEmpty(diagnosticCode) &&
                diagnosticCode.StartsWith("YAML", StringComparison.Ordinal))
            {
                string formattedMessage = message ?? string.Empty;
                string codePrefix = diagnosticCode + ":";
                if (!formattedMessage.StartsWith(codePrefix, StringComparison.Ordinal))
                {
                    formattedMessage = codePrefix + " " + formattedMessage;
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} ({1}:{2}:{3})",
                    formattedMessage,
                    sourcePath,
                    line,
                    column);
            }

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
