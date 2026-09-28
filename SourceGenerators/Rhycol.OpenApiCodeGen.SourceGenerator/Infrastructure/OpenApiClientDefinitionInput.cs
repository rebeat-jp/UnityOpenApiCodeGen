using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal readonly struct OpenApiClientDefinitionInput : IEquatable<OpenApiClientDefinitionInput>
    {
        private OpenApiClientDefinitionInput(
            DefinitionStatus status,
            string specId,
            GeneratorOptions options,
            int documentFormat,
            string targetDisplayName,
            string validationMessage,
            DiagnosticLocation location)
        {
            Status = status;
            SpecId = specId;
            Options = options;
            DocumentFormat = documentFormat;
            TargetDisplayName = targetDisplayName;
            ValidationMessage = validationMessage;
            Location = location;
        }

        internal enum DefinitionStatus
        {
            Valid,
            Invalid,
            UnsupportedDocumentFormat
        }

        internal DefinitionStatus Status { get; }

        internal string SpecId { get; }

        internal GeneratorOptions Options { get; }

        internal int DocumentFormat { get; }

        internal string TargetDisplayName { get; }

        internal string ValidationMessage { get; }

        internal DiagnosticLocation Location { get; }

        internal static OpenApiClientDefinitionInput CreateValid(
            string specId,
            string apiName,
            string generatedNamespace,
            int documentFormat,
            string targetDisplayName,
            DiagnosticLocation location)
        {
            return new OpenApiClientDefinitionInput(
                DefinitionStatus.Valid,
                specId,
                new GeneratorOptions(apiName, generatedNamespace),
                documentFormat,
                targetDisplayName,
                string.Empty,
                location);
        }

        internal static OpenApiClientDefinitionInput CreateInvalid(
            string targetDisplayName,
            string validationMessage,
            DiagnosticLocation location)
        {
            return new OpenApiClientDefinitionInput(
                DefinitionStatus.Invalid,
                string.Empty,
                new GeneratorOptions(string.Empty, string.Empty),
                0,
                targetDisplayName,
                validationMessage,
                location);
        }

        internal static OpenApiClientDefinitionInput CreateUnsupportedDocumentFormat(
            string specId,
            string apiName,
            string generatedNamespace,
            int documentFormat,
            string targetDisplayName,
            DiagnosticLocation location)
        {
            return new OpenApiClientDefinitionInput(
                DefinitionStatus.UnsupportedDocumentFormat,
                specId,
                new GeneratorOptions(apiName, generatedNamespace),
                documentFormat,
                targetDisplayName,
                string.Empty,
                location);
        }

        public bool Equals(OpenApiClientDefinitionInput other)
        {
            return Status == other.Status &&
                   string.Equals(SpecId, other.SpecId, StringComparison.Ordinal) &&
                   Options.Equals(other.Options) &&
                   DocumentFormat == other.DocumentFormat &&
                   string.Equals(TargetDisplayName, other.TargetDisplayName, StringComparison.Ordinal) &&
                   string.Equals(ValidationMessage, other.ValidationMessage, StringComparison.Ordinal) &&
                   Location.Equals(other.Location);
        }

        public override bool Equals(object? obj)
        {
            return obj is OpenApiClientDefinitionInput other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Status;
                hashCode = (hashCode * 397) ^ GetOrdinalHashCode(SpecId);
                hashCode = (hashCode * 397) ^ Options.GetHashCode();
                hashCode = (hashCode * 397) ^ DocumentFormat;
                hashCode = (hashCode * 397) ^ GetOrdinalHashCode(TargetDisplayName);
                hashCode = (hashCode * 397) ^ GetOrdinalHashCode(ValidationMessage);
                hashCode = (hashCode * 397) ^ Location.GetHashCode();
                return hashCode;
            }
        }

        private static int GetOrdinalHashCode(string? value)
        {
            return value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);
        }
    }
}
