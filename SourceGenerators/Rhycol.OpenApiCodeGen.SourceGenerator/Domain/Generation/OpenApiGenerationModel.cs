using System;
using System.Collections.Generic;
using System.Linq;

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
            IReadOnlyList<GeneratedOperationModel> operations,
            IReadOnlyList<GeneratedTypeNameCollision> typeNameCollisions,
            IReadOnlyList<OpenApiSemanticWarning>? warnings = null,
            IReadOnlyList<GeneratedUnionModel>? unions = null,
            IReadOnlyList<OpenApiSemanticServer>? servers = null,
            string? fileParameterTypeName = null,
            IReadOnlyList<GeneratedSecuritySchemeModel>? securitySchemes = null)
        {
            SecuritySchemes = securitySchemes ?? Array.Empty<GeneratedSecuritySchemeModel>();
            FileParameterTypeName = fileParameterTypeName;
            Unions = unions ?? Array.Empty<GeneratedUnionModel>();
            Servers = servers ?? Array.Empty<OpenApiSemanticServer>();
            Warnings = warnings ?? Array.Empty<OpenApiSemanticWarning>();
            ApiName = apiName;
            GeneratedNamespace = generatedNamespace;
            BaseUrl = baseUrl;
            Dtos = dtos;
            Enums = enums;
            Operations = operations;
            TypeNameCollisions = typeNameCollisions;
        }

        /// <summary>Security schemes of the document; each adds credential properties to the client.</summary>
        internal IReadOnlyList<GeneratedSecuritySchemeModel> SecuritySchemes { get; }

        internal string ApiName { get; }

        internal string GeneratedNamespace { get; }

        internal string BaseUrl { get; }

        /// <summary>Document-level servers (a Server Object each); BaseUrl is the default URL of the first.</summary>
        internal IReadOnlyList<OpenApiSemanticServer> Servers { get; }

        internal IReadOnlyList<GeneratedDtoModel> Dtos { get; }

        internal IReadOnlyList<GeneratedEnumModel> Enums { get; }

        /// <summary>Union wrapper types (oneOf / anyOf).</summary>
        /// <summary>Name of the generated file part type; null when no multipart body or binary property uses it.</summary>
        internal string? FileParameterTypeName { get; }

        internal IReadOnlyList<GeneratedUnionModel> Unions { get; }

        internal IReadOnlyList<GeneratedOperationModel> Operations { get; }
        internal IReadOnlyList<GeneratedTypeNameCollision> TypeNameCollisions { get; }

        /// <summary>Warnings about ignored elements, reported as OACG108.</summary>
        internal IReadOnlyList<OpenApiSemanticWarning> Warnings { get; }
    }

    internal sealed class GeneratedTypeNameCollision
    {
        internal GeneratedTypeNameCollision(
            string requestedName,
            string generatedName,
            OpenApiSourceLocation location,
            OpenApiSourceLocation existingLocation)
        {
            RequestedName = requestedName;
            GeneratedName = generatedName;
            Location = location;
            ExistingLocation = existingLocation;
        }
        internal string RequestedName { get; }
        internal string GeneratedName { get; }
        internal OpenApiSourceLocation Location { get; }
        internal OpenApiSourceLocation ExistingLocation { get; }
    }

    internal sealed class GeneratedDtoModel
    {
        internal GeneratedDtoModel(
            string name,
            IReadOnlyList<GeneratedDtoPropertyModel> properties,
            OpenApiSemanticAnnotations? annotations = null,
            string? extensionDataName = null,
            GeneratedTypeModel? extensionDataValueType = null,
            IReadOnlyList<GeneratedConstraints>? constraintSets = null)
        {
            ConstraintSets = constraintSets ?? Array.Empty<GeneratedConstraints>();
            Name = name;
            Properties = properties;
            Annotations = annotations ?? OpenApiSemanticAnnotations.Empty;
            ExtensionDataName = extensionDataName;
            ExtensionDataValueType = extensionDataValueType;
        }

        /// <summary>
        /// Member name of the IDictionary&lt;string, JToken&gt; that keeps undeclared members.
        /// Null for a closed DTO.
        /// </summary>
        internal string? ExtensionDataName { get; }

        /// <summary>
        /// Declared value type of undeclared members (additionalProperties schema), used to validate
        /// response values. Null when the members are untyped.
        /// </summary>
        internal GeneratedTypeModel? ExtensionDataValueType { get; }

        internal bool IsOpen => ExtensionDataName is not null;

        /// <summary>
        /// Object level validation keywords of the schema (minProperties, patternProperties,
        /// dependentRequired, ...). They are checked at run time and never change the class.
        /// </summary>
        internal IReadOnlyList<GeneratedConstraints> ConstraintSets { get; }

        internal OpenApiSemanticAnnotations Annotations { get; }

        internal string Name { get; }

        internal IReadOnlyList<GeneratedDtoPropertyModel> Properties { get; }
    }

    internal sealed class GeneratedDtoPropertyModel
    {
        internal GeneratedDtoPropertyModel(
            string name,
            string wireName,
            bool required,
            bool useSpecified,
            GeneratedTypeModel type,
            OpenApiSemanticAnnotations? annotations = null,
            OpenApiSemanticLiteral? initialValue = null)
        {
            InitialValue = initialValue;
            Annotations = annotations ?? OpenApiSemanticAnnotations.Empty;
            Name = name;
            WireName = wireName;
            Required = required;
            UseSpecified = useSpecified;
            Type = type;
        }

        internal string Name { get; }

        internal string WireName { get; }

        internal bool Required { get; }

        internal bool UseSpecified { get; }

        /// <summary>Initial value of a string member that selects a union member (not validated).</summary>
        internal OpenApiSemanticLiteral? InitialValue { get; }

        internal GeneratedTypeModel Type { get; }

        internal OpenApiSemanticAnnotations Annotations { get; }
    }

    internal sealed class GeneratedEnumModel
    {
        internal GeneratedEnumModel(
            string name,
            IReadOnlyList<GeneratedEnumMemberModel> members,
            OpenApiSemanticAnnotations? annotations = null)
        {
            Name = name;
            Members = members;
            Annotations = annotations ?? OpenApiSemanticAnnotations.Empty;
        }

        internal OpenApiSemanticAnnotations Annotations { get; }

        internal string Name { get; }

        internal IReadOnlyList<GeneratedEnumMemberModel> Members { get; }

        /// <summary>True when the members are integers (numeric wire values).</summary>
        internal bool IsInteger => Members.Count > 0 &&
            Members.All(static member => member.Value.Kind == OpenApiSemanticLiteralKind.Integer);
    }

    internal sealed class GeneratedEnumMemberModel
    {
        internal GeneratedEnumMemberModel(
            string name,
            string wireValue,
            OpenApiSemanticLiteral? value = null,
            string? description = null)
        {
            Description = description ?? string.Empty;
            if (value.HasValue &&
                !string.Equals(value.Value.RawText, wireValue, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "wireValue must equal value.RawText.",
                    nameof(wireValue));
            }

            Name = name;
            WireValue = wireValue;
            Value = value ?? OpenApiSemanticLiteral.FromString(wireValue);
        }

        /// <summary>Documentation from x-enum-descriptions.</summary>
        internal string Description { get; }

        /// <summary>Typed enum value. WireValue is its RawText.</summary>
        internal OpenApiSemanticLiteral Value { get; }

        internal string Name { get; }

        internal string WireValue { get; }
    }

    /// <summary>
    /// A oneOf / anyOf wrapper class. With a discriminator the members are DTOs selected by the
    /// discriminator value; without one the wrapper keeps the raw JSON.
    /// </summary>
    internal sealed class GeneratedUnionModel
    {
        internal GeneratedUnionModel(
            string name,
            bool isOneOf,
            string? discriminatorPropertyName,
            IReadOnlyList<GeneratedUnionMemberModel> members,
            OpenApiSemanticAnnotations? annotations = null,
            int defaultMemberIndex = -1)
        {
            DefaultMemberIndex = defaultMemberIndex;
            Name = name;
            IsOneOf = isOneOf;
            DiscriminatorPropertyName = discriminatorPropertyName;
            Members = members;
            Annotations = annotations ?? OpenApiSemanticAnnotations.Empty;
        }

        internal string Name { get; }

        internal bool IsOneOf { get; }

        /// <summary>Null when the union has no discriminator.</summary>
        internal string? DiscriminatorPropertyName { get; }

        internal IReadOnlyList<GeneratedUnionMemberModel> Members { get; }

        /// <summary>Index of the discriminator defaultMapping member (OpenAPI 3.2); -1 when there is none.</summary>
        internal int DefaultMemberIndex { get; }

        internal OpenApiSemanticAnnotations Annotations { get; }
    }

    internal sealed class GeneratedUnionMemberModel
    {
        internal GeneratedUnionMemberModel(
            string accessorName,
            GeneratedTypeModel type,
            IReadOnlyList<string>? discriminatorValues = null)
        {
            AccessorName = accessorName;
            Type = type;
            DiscriminatorValues = discriminatorValues ?? Array.Empty<string>();
        }

        /// <summary>Suffix of the generated FromX / TryGetX / AsX members.</summary>
        internal string AccessorName { get; }

        internal GeneratedTypeModel Type { get; }

        /// <summary>Wire values that select this member (the first is written). Empty without a discriminator.</summary>
        internal IReadOnlyList<string> DiscriminatorValues { get; }
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
            IReadOnlyList<string> successStatusCodes,
            IReadOnlyDictionary<string, IReadOnlyList<string>> responseMediaTypes,
            string description = "",
            bool deprecated = false,
            IReadOnlyList<OpenApiSemanticServer>? servers = null,
            GeneratedResponsePlanModel? responsePlan = null,
            IReadOnlyList<GeneratedErrorResponseModel>? errorResponses = null,
            IReadOnlyList<OpenApiSemanticSecurityRequirement>? securityRequirements = null)
        {
            SecurityRequirements = securityRequirements ?? Array.Empty<OpenApiSemanticSecurityRequirement>();
            ResponsePlan = responsePlan;
            ErrorResponses = errorResponses ?? Array.Empty<GeneratedErrorResponseModel>();
            Name = name;
            Summary = summary;
            HttpMethod = httpMethod;
            Path = path;
            Parameters = parameters;
            RequestBody = requestBody;
            ResponseType = responseType;
            SuccessStatusCodes = successStatusCodes;
            ResponseMediaTypes = responseMediaTypes;
            Description = description ?? string.Empty;
            Deprecated = deprecated;
            Servers = servers ?? Array.Empty<OpenApiSemanticServer>();
        }

        internal string Name { get; }

        internal string Summary { get; }

        /// <summary>Effective security requirements (OR of ANDs); empty = no authentication.</summary>
        internal IReadOnlyList<OpenApiSemanticSecurityRequirement> SecurityRequirements { get; }

        internal string Description { get; }

        internal bool Deprecated { get; }

        /// <summary>Servers of the operation or its Path Item; empty to use the document servers.</summary>
        internal IReadOnlyList<OpenApiSemanticServer> Servers { get; }

        internal string HttpMethod { get; }

        internal string Path { get; }

        internal IReadOnlyList<GeneratedParameterModel> Parameters { get; }

        internal GeneratedRequestBodyModel? RequestBody { get; }

        internal GeneratedTypeModel? ResponseType { get; }

        /// <summary>The advanced response path; null when the original void / single JSON contract path applies.</summary>
        internal GeneratedResponsePlanModel? ResponsePlan { get; }

        /// <summary>Every body type decoded as JSON on a success path (the original path or the plan).</summary>
        internal IEnumerable<GeneratedTypeModel> EnumerateJsonResponseTypes()
        {
            if (ResponseType is not null)
            {
                yield return ResponseType;
            }

            if (ResponsePlan is not null)
            {
                foreach (GeneratedSuccessResponseModel member in ResponsePlan.Successes)
                {
                    if (member.Type is not null && member.Contents.Any(static content => content.Kind == GeneratedResponseBodyKind.Json))
                    {
                        yield return member.Type;
                    }
                }
            }
        }

        /// <summary>Declared error bodies (JSON) by status, used to give the API exception a typed error.</summary>
        internal IReadOnlyList<GeneratedErrorResponseModel> ErrorResponses { get; }

        internal IReadOnlyList<string> SuccessStatusCodes { get; }

        internal IReadOnlyDictionary<string, IReadOnlyList<string>> ResponseMediaTypes { get; }
    }

    internal sealed class GeneratedParameterModel
    {
        internal GeneratedParameterModel(
            string name,
            string wireName,
            string locationName,
            bool required,
            GeneratedTypeModel type,
            string style = "",
            bool explode = false,
            bool allowReserved = false,
            string contentMediaType = "",
            string description = "",
            bool deprecated = false,
            IReadOnlyList<GeneratedFormFieldModel>? formFields = null)
        {
            FormFields = formFields;
            Name = name;
            WireName = wireName;
            LocationName = locationName;
            Required = required;
            Type = type;
            Style = style ?? string.Empty;
            Explode = explode;
            AllowReserved = allowReserved;
            ContentMediaType = contentMediaType ?? string.Empty;
            Description = description ?? string.Empty;
            Deprecated = deprecated;
        }

        internal string Name { get; }

        internal string WireName { get; }

        internal string LocationName { get; }

        internal bool Required { get; }

        internal GeneratedTypeModel Type { get; }

        /// <summary>Serialization style (empty for content parameters).</summary>
        internal string Style { get; }

        internal bool Explode { get; }

        internal bool AllowReserved { get; }

        /// <summary>JSON media type when the parameter is described by content; otherwise empty.</summary>
        internal string ContentMediaType { get; }

        internal bool IsContent => ContentMediaType.Length > 0;

        /// <summary>The fields of an in: querystring application/x-www-form-urlencoded parameter; null otherwise.</summary>
        internal IReadOnlyList<GeneratedFormFieldModel>? FormFields { get; }

        internal string Description { get; }

        internal bool Deprecated { get; }
    }

    internal sealed class GeneratedRequestBodyModel
    {
        internal GeneratedRequestBodyModel(
            string parameterName,
            bool required,
            string mediaType,
            GeneratedTypeModel type,
            string? specifiedParameterName,
            IReadOnlyList<GeneratedBodyVariantModel>? variants = null)
        {
            Variants = variants ?? new[]
            {
                new GeneratedBodyVariantModel(GeneratedBodyKind.Json, mediaType, type, string.Empty, null, null, specifiedParameterName)
            };
            ParameterName = parameterName;
            Required = required;
            MediaType = mediaType;
            Type = type;
            SpecifiedParameterName = specifiedParameterName;
        }

        internal string ParameterName { get; }

        internal bool Required { get; }

        internal string MediaType { get; }

        internal GeneratedTypeModel Type { get; }

        internal string? SpecifiedParameterName { get; }

        /// <summary>Every media type of the body; index 0 is the primary one (MediaType and Type describe it).</summary>
        internal IReadOnlyList<GeneratedBodyVariantModel> Variants { get; }
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
        Array,
        // Any, Map, FreeObject, Union (a named wrapper class) and Binary (byte[]).
        Any,
        Map,
        Union,
        Binary,
        /// <summary>An untyped JSON object (JObject).</summary>
        FreeObject,
        /// <summary>A file part: the generated FileParameter type (Name).</summary>
        File
    }

    internal sealed class GeneratedTypeModel : IEquatable<GeneratedTypeModel>
    {
        internal GeneratedTypeModel(
            GeneratedTypeKind kind,
            bool nullable = false,
            string name = "",
            GeneratedTypeModel? itemType = null,
            GeneratedTypeModel? valueType = null,
            IReadOnlyList<GeneratedTypeModel>? unionMembers = null,
            OpenApiSemanticLiteral? constValue = null,
            IReadOnlyList<GeneratedTypeModel>? notTypes = null,
            IReadOnlyList<GeneratedConstraints>? constraintSets = null)
        {
            ConstraintSets = constraintSets ?? Array.Empty<GeneratedConstraints>();
            NotTypes = notTypes ?? Array.Empty<GeneratedTypeModel>();
            ConstValue = constValue;
            Kind = kind;
            Nullable = nullable;
            Name = name ?? string.Empty;
            ItemType = itemType;
            ValueType = valueType;
            UnionMembers = unionMembers ?? Array.Empty<GeneratedTypeModel>();
        }

        /// <summary>
        /// JSON Schema const value for scalar types. Part of the type identity so that response
        /// validation can be generated per const value.
        /// </summary>
        internal OpenApiSemanticLiteral? ConstValue { get; }

        /// <summary>Value type when Kind is Map.</summary>
        internal GeneratedTypeModel? ValueType { get; }

        /// <summary>Member types when Kind is Union.</summary>
        internal IReadOnlyList<GeneratedTypeModel> UnionMembers { get; }

        /// <summary>
        /// Types of the not keyword. Part of the type identity because response validation is
        /// generated per type: a value that matches one of them is rejected.
        /// </summary>
        internal IReadOnlyList<GeneratedTypeModel> NotTypes { get; }

        /// <summary>
        /// Validation keyword sets (minLength, pattern, minimum, ...) of a non-DTO type, all of which must
        /// hold. Part of the type identity because the checks are generated per type. A DTO keeps its sets
        /// in its model (GeneratedDtoModel.ConstraintSets) so that a recursive schema stays finite.
        /// </summary>
        internal IReadOnlyList<GeneratedConstraints> ConstraintSets { get; }

        internal GeneratedTypeModel WithNot(GeneratedTypeModel notType)
        {
            return new GeneratedTypeModel(
                Kind, Nullable, Name, ItemType, ValueType, UnionMembers, ConstValue,
                NotTypes.Concat(new[] { notType }).ToArray(), ConstraintSets);
        }

        internal GeneratedTypeModel WithConstraints(IReadOnlyList<GeneratedConstraints> constraintSets)
        {
            return constraintSets.Count == 0
                ? this
                : new GeneratedTypeModel(
                    Kind, Nullable, Name, ItemType, ValueType, UnionMembers, ConstValue, NotTypes,
                    ConstraintSets.Concat(constraintSets).ToArray());
        }

        internal GeneratedTypeKind Kind { get; }

        internal bool Nullable { get; }

        internal string Name { get; }

        internal GeneratedTypeModel? ItemType { get; }

        internal GeneratedTypeModel WithNullable(bool nullable)
        {
            return nullable == Nullable
                ? this
                : new GeneratedTypeModel(Kind, nullable, Name, ItemType, ValueType, UnionMembers, ConstValue, NotTypes, ConstraintSets);
        }

        internal GeneratedTypeModel WithConst(OpenApiSemanticLiteral? constValue)
        {
            return System.Nullable.Equals(constValue, ConstValue)
                ? this
                : new GeneratedTypeModel(Kind, Nullable, Name, ItemType, ValueType, UnionMembers, constValue, NotTypes, ConstraintSets);
        }

        /// <summary>
        /// Any (JToken), Map (Dictionary), Union (named wrapper class) and Binary (byte[]) are
        /// reference types, so they are intentionally absent from <see cref="IsValueType"/>.
        /// </summary>
        internal bool IsReferenceType => !IsValueType;

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
                   Equals(ItemType, other.ItemType) &&
                   Equals(ValueType, other.ValueType) &&
                   System.Nullable.Equals(ConstValue, other.ConstValue) &&
                   NotTypes.SequenceEqual(other.NotTypes) &&
                   ConstraintSets.SequenceEqual(other.ConstraintSets) &&
                   UnionMembers.SequenceEqual(other.UnionMembers);
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
                hashCode = (hashCode * 397) ^ (ValueType?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ ConstValue.GetHashCode();
                foreach (GeneratedTypeModel notType in NotTypes)
                {
                    hashCode = (hashCode * 397) ^ notType.GetHashCode();
                }

                foreach (GeneratedTypeModel member in UnionMembers)
                {
                    hashCode = (hashCode * 397) ^ member.GetHashCode();
                }

                foreach (GeneratedConstraints set in ConstraintSets)
                {
                    hashCode = (hashCode * 397) ^ set.GetHashCode();
                }

                return hashCode;
            }
        }
    }
}
