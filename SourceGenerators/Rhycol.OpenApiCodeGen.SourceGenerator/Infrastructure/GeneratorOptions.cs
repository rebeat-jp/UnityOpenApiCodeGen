using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal readonly struct GeneratorOptions : IEquatable<GeneratorOptions>
    {
        internal GeneratorOptions(string apiName, string generatedNamespace)
            : this(
                apiName,
                generatedNamespace,
                HttpTransportKind.HttpClient,
                JsonSerializerKind.NewtonsoftJson)
        {
        }

        internal GeneratorOptions(
            string apiName,
            string generatedNamespace,
            HttpTransportKind httpTransport,
            JsonSerializerKind jsonSerializer)
        {
            ApiName = apiName;
            GeneratedNamespace = generatedNamespace;
            HttpTransport = httpTransport;
            JsonSerializer = jsonSerializer;
        }

        internal string ApiName { get; }

        internal string GeneratedNamespace { get; }

        internal HttpTransportKind HttpTransport { get; }

        internal JsonSerializerKind JsonSerializer { get; }

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
                   string.Equals(GeneratedNamespace, other.GeneratedNamespace, StringComparison.Ordinal) &&
                   HttpTransport == other.HttpTransport &&
                   JsonSerializer == other.JsonSerializer;
        }

        public override bool Equals(object? obj)
        {
            return obj is GeneratorOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = ApiName == null ? 0 : StringComparer.Ordinal.GetHashCode(ApiName);
                hashCode = (hashCode * 397) ^
                           (GeneratedNamespace == null
                               ? 0
                               : StringComparer.Ordinal.GetHashCode(GeneratedNamespace));
                hashCode = (hashCode * 397) ^ (int)HttpTransport;
                hashCode = (hashCode * 397) ^ (int)JsonSerializer;
                return hashCode;
            }
        }
    }
}
