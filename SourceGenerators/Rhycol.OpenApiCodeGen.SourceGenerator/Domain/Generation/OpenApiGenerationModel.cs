using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class OpenApiGenerationModel
    {
        internal OpenApiGenerationModel(
            string apiName,
            string generatedNamespace,
            string baseUrl,
            IReadOnlyList<GeneratedDtoModel> dtos,
            IReadOnlyList<GeneratedEnumModel> enums,
            IReadOnlyList<GeneratedOperationModel> operations)
        {
            ApiName = apiName;
            GeneratedNamespace = generatedNamespace;
            BaseUrl = baseUrl;
            Dtos = dtos;
            Enums = enums;
            Operations = operations;
        }

        internal string ApiName { get; }

        internal string GeneratedNamespace { get; }

        internal string BaseUrl { get; }

        internal IReadOnlyList<GeneratedDtoModel> Dtos { get; }

        internal IReadOnlyList<GeneratedEnumModel> Enums { get; }

        internal IReadOnlyList<GeneratedOperationModel> Operations { get; }
    }

    internal sealed class GeneratedDtoModel
    {
        internal GeneratedDtoModel(string name, IReadOnlyList<GeneratedDtoPropertyModel> properties)
        {
            Name = name;
            Properties = properties;
        }

        internal string Name { get; }

        internal IReadOnlyList<GeneratedDtoPropertyModel> Properties { get; }
    }

    internal sealed class GeneratedDtoPropertyModel
    {
        internal GeneratedDtoPropertyModel(
            string name,
            string wireName,
            bool required,
            GeneratedTypeModel type)
        {
            Name = name;
            WireName = wireName;
            Required = required;
            Type = type;
        }

        internal string Name { get; }

        internal string WireName { get; }

        internal bool Required { get; }

        internal GeneratedTypeModel Type { get; }
    }

    internal sealed class GeneratedEnumModel
    {
        internal GeneratedEnumModel(string name, IReadOnlyList<GeneratedEnumMemberModel> members)
        {
            Name = name;
            Members = members;
        }

        internal string Name { get; }

        internal IReadOnlyList<GeneratedEnumMemberModel> Members { get; }
    }

    internal sealed class GeneratedEnumMemberModel
    {
        internal GeneratedEnumMemberModel(string name, string wireValue)
        {
            Name = name;
            WireValue = wireValue;
        }

        internal string Name { get; }

        internal string WireValue { get; }
    }

    internal sealed class GeneratedOperationModel
    {
        internal GeneratedOperationModel(
            string name,
            string summary,
            string httpMethod,
            string path,
            IReadOnlyList<GeneratedParameterModel> parameters,
            GeneratedRequestBodyModel? requestBody,
            GeneratedTypeModel? responseType,
            IReadOnlyList<string> successStatusCodes)
        {
            Name = name;
            Summary = summary;
            HttpMethod = httpMethod;
            Path = path;
            Parameters = parameters;
            RequestBody = requestBody;
            ResponseType = responseType;
            SuccessStatusCodes = successStatusCodes;
        }

        internal string Name { get; }

        internal string Summary { get; }

        internal string HttpMethod { get; }

        internal string Path { get; }

        internal IReadOnlyList<GeneratedParameterModel> Parameters { get; }

        internal GeneratedRequestBodyModel? RequestBody { get; }

        internal GeneratedTypeModel? ResponseType { get; }

        internal IReadOnlyList<string> SuccessStatusCodes { get; }
    }

    internal sealed class GeneratedParameterModel
    {
        internal GeneratedParameterModel(
            string name,
            string wireName,
            string locationName,
            bool required,
            GeneratedTypeModel type)
        {
            Name = name;
            WireName = wireName;
            LocationName = locationName;
            Required = required;
            Type = type;
        }

        internal string Name { get; }

        internal string WireName { get; }

        internal string LocationName { get; }

        internal bool Required { get; }

        internal GeneratedTypeModel Type { get; }
    }

    internal sealed class GeneratedRequestBodyModel
    {
        internal GeneratedRequestBodyModel(
            string parameterName,
            bool required,
            string mediaType,
            GeneratedTypeModel type)
        {
            ParameterName = parameterName;
            Required = required;
            MediaType = mediaType;
            Type = type;
        }

        internal string ParameterName { get; }

        internal bool Required { get; }

        internal string MediaType { get; }

        internal GeneratedTypeModel Type { get; }
    }

    internal enum GeneratedTypeKind
    {
        String,
        Int32,
        Int64,
        Single,
        Double,
        Decimal,
        Boolean,
        DateTime,
        DateTimeOffset,
        Guid,
        Named,
        NamedEnum,
        Array
    }

    internal sealed class GeneratedTypeModel : IEquatable<GeneratedTypeModel>
    {
        internal GeneratedTypeModel(
            GeneratedTypeKind kind,
            bool nullable = false,
            string name = "",
            GeneratedTypeModel? itemType = null)
        {
            Kind = kind;
            Nullable = nullable;
            Name = name ?? string.Empty;
            ItemType = itemType;
        }

        internal GeneratedTypeKind Kind { get; }

        internal bool Nullable { get; }

        internal string Name { get; }

        internal GeneratedTypeModel? ItemType { get; }

        internal GeneratedTypeModel WithNullable(bool nullable)
        {
            return nullable == Nullable
                ? this
                : new GeneratedTypeModel(Kind, nullable, Name, ItemType);
        }

        internal bool IsValueType =>
            Kind == GeneratedTypeKind.Int32 ||
            Kind == GeneratedTypeKind.Int64 ||
            Kind == GeneratedTypeKind.Single ||
            Kind == GeneratedTypeKind.Double ||
            Kind == GeneratedTypeKind.Decimal ||
            Kind == GeneratedTypeKind.Boolean ||
            Kind == GeneratedTypeKind.DateTime ||
            Kind == GeneratedTypeKind.DateTimeOffset ||
            Kind == GeneratedTypeKind.Guid ||
            Kind == GeneratedTypeKind.NamedEnum;

        public bool Equals(GeneratedTypeModel? other)
        {
            return other is not null &&
                   Kind == other.Kind &&
                   Nullable == other.Nullable &&
                   string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                   Equals(ItemType, other.ItemType);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as GeneratedTypeModel);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Kind;
                hashCode = (hashCode * 397) ^ Nullable.GetHashCode();
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Name);
                hashCode = (hashCode * 397) ^ (ItemType?.GetHashCode() ?? 0);
                return hashCode;
            }
        }
    }
}
