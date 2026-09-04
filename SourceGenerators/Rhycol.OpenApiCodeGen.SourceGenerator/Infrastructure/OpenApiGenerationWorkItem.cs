using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal readonly struct OpenApiGenerationWorkItem : IEquatable<OpenApiGenerationWorkItem>
    {
        private OpenApiGenerationWorkItem(
            WorkItemKind kind,
            OpenApiClientDefinitionInput definition,
            NormalizedSpecCandidate bundle,
            string specId,
            string detail)
        {
            Kind = kind;
            Definition = definition;
            Bundle = bundle;
            SpecId = specId;
            Detail = detail;
        }

        internal enum WorkItemKind
        {
            Generate,
            MissingBundle,
            DuplicateBundle,
            DuplicateDefinition
        }

        internal WorkItemKind Kind { get; }

        internal OpenApiClientDefinitionInput Definition { get; }

        internal NormalizedSpecCandidate Bundle { get; }

        internal string SpecId { get; }

        internal string Detail { get; }

        internal static OpenApiGenerationWorkItem CreateGeneration(
            OpenApiClientDefinitionInput definition,
            NormalizedSpecCandidate bundle)
        {
            return new OpenApiGenerationWorkItem(
                WorkItemKind.Generate,
                definition,
                bundle,
                definition.SpecId,
                string.Empty);
        }

        internal static OpenApiGenerationWorkItem CreateMissingBundle(
            OpenApiClientDefinitionInput definition)
        {
            return new OpenApiGenerationWorkItem(
                WorkItemKind.MissingBundle,
                definition,
                default,
                definition.SpecId,
                string.Empty);
        }

        internal static OpenApiGenerationWorkItem CreateDuplicateBundle(string specId, string detail)
        {
            return new OpenApiGenerationWorkItem(
                WorkItemKind.DuplicateBundle,
                default,
                default,
                specId,
                detail);
        }

        internal static OpenApiGenerationWorkItem CreateDuplicateDefinition(
            OpenApiClientDefinitionInput definition)
        {
            return new OpenApiGenerationWorkItem(
                WorkItemKind.DuplicateDefinition,
                definition,
                default,
                definition.SpecId,
                string.Empty);
        }

        public bool Equals(OpenApiGenerationWorkItem other)
        {
            return Kind == other.Kind &&
                   Definition.Equals(other.Definition) &&
                   Bundle.Equals(other.Bundle) &&
                   string.Equals(SpecId, other.SpecId, StringComparison.Ordinal) &&
                   string.Equals(Detail, other.Detail, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is OpenApiGenerationWorkItem other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Kind;
                hashCode = (hashCode * 397) ^ Definition.GetHashCode();
                hashCode = (hashCode * 397) ^ Bundle.GetHashCode();
                hashCode = (hashCode * 397) ^ GetOrdinalHashCode(SpecId);
                hashCode = (hashCode * 397) ^ GetOrdinalHashCode(Detail);
                return hashCode;
            }
        }

        private static int GetOrdinalHashCode(string? value)
        {
            return value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);
        }
    }
}
