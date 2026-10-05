using System;
using System.Globalization;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class YamlParseException : Exception
    {
        internal YamlParseException(
            YamlDiagnosticCode code,
            string message,
            string sourcePath,
            int line,
            int column,
            int offset)
            : base(FormatMessage(code, message, sourcePath, line, column))
        {
            Code = code;
            DiagnosticId = YamlDiagnosticCodes.ToCode(code);
            DiagnosticMessage = string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1}",
                DiagnosticId,
                message);
            SourcePath = sourcePath;
            Line = line;
            Column = column;
            Offset = offset;
        }

        internal YamlDiagnosticCode Code { get; }

        internal string DiagnosticId { get; }

        /// <summary>
        /// Gets the diagnostic code and description without the source-location suffix.
        /// </summary>
        internal string DiagnosticMessage { get; }

        internal string SourcePath { get; }

        internal int Line { get; }

        internal int Column { get; }

        internal int Offset { get; }

        private static string FormatMessage(
            YamlDiagnosticCode code,
            string message,
            string sourcePath,
            int line,
            int column)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1} ({2}:{3}:{4})",
                YamlDiagnosticCodes.ToCode(code),
                message,
                sourcePath,
                line,
                column);
        }
    }
}
