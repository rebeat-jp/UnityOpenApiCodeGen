using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal readonly struct GeneratorOptions : IEquatable<GeneratorOptions>
    {
        internal GeneratorOptions(string apiName, string generatedNamespace)
        {
            ApiName = apiName;
            GeneratedNamespace = generatedNamespace;
        }

        internal string ApiName { get; }

        internal string GeneratedNamespace { get; }

        internal ApiClientGenerateOption ToDomainOptions()
        {
            return new ApiClientGenerateOption
            {
                ApiName = ApiName,
                Namespace = GeneratedNamespace
            };
        }

        public bool Equals(GeneratorOptions other)
        {
            return string.Equals(ApiName, other.ApiName, StringComparison.Ordinal) &&
                   string.Equals(GeneratedNamespace, other.GeneratedNamespace, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is GeneratorOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ApiName == null ? 0 : StringComparer.Ordinal.GetHashCode(ApiName)) * 397) ^
                       (GeneratedNamespace == null
                           ? 0
                           : StringComparer.Ordinal.GetHashCode(GeneratedNamespace));
            }
        }
    }
}
