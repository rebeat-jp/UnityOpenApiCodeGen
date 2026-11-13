using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal readonly struct OpenApiSourceLocation : IEquatable<OpenApiSourceLocation>
    {
        internal OpenApiSourceLocation(int line, int column, string logicalPath)
            : this(string.Empty, "root", line, column, logicalPath)
        {
        }

        internal OpenApiSourceLocation(
            string sourcePath,
            string documentId,
            int line,
            int column,
            string logicalPath)
        {
            SourcePath = sourcePath ?? string.Empty;
            DocumentId = documentId ?? "root";
            Line = line;
            Column = column;
            LogicalPath = logicalPath ?? string.Empty;
        }

        internal string SourcePath { get; }

        internal string DocumentId { get; }

        internal int Line { get; }

        internal int Column { get; }

        internal string LogicalPath { get; }

        internal static OpenApiSourceLocation FromNode(SpecNode node)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            return new OpenApiSourceLocation(node.Line, node.Column, node.LogicalPath);
        }

        internal static OpenApiSourceLocation FromNode(
            SpecNode node,
            string sourcePath,
            string documentId)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            return new OpenApiSourceLocation(
                sourcePath,
                documentId,
                node.Line,
                node.Column,
                node.LogicalPath);
        }

        public bool Equals(OpenApiSourceLocation other)
        {
            return Line == other.Line &&
                   Column == other.Column &&
                   string.Equals(SourcePath, other.SourcePath, StringComparison.Ordinal) &&
                   string.Equals(DocumentId, other.DocumentId, StringComparison.Ordinal) &&
                   string.Equals(LogicalPath, other.LogicalPath, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is OpenApiSourceLocation other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Line;
                hashCode = (hashCode * 397) ^ Column;
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(SourcePath);
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(DocumentId);
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(LogicalPath);
                return hashCode;
            }
        }
    }

    internal sealed class OpenApiSemanticDocument
    {
        internal OpenApiSemanticDocument(
            int minorVersion,
            string baseUrl,
            IReadOnlyDictionary<NormalizedSpecNodeIdentity, OpenApiSemanticSchema> schemas,
            IReadOnlyList<OpenApiSemanticOperation> operations,
            OpenApiSourceLocation location,
            IReadOnlyList<OpenApiSemanticWarning>? warnings = null)
        {
            Warnings = warnings ?? Array.Empty<OpenApiSemanticWarning>();
            MinorVersion = minorVersion;
            BaseUrl = baseUrl ?? string.Empty;
            Schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            Operations = operations ?? throw new ArgumentNullException(nameof(operations));
            Location = location;
        }

        internal int MinorVersion { get; }

        internal string BaseUrl { get; }

        internal IReadOnlyDictionary<NormalizedSpecNodeIdentity, OpenApiSemanticSchema> Schemas { get; }

        internal IReadOnlyList<OpenApiSemanticOperation> Operations { get; }

        internal OpenApiSourceLocation Location { get; }

        /// <summary>Warnings about ignored elements (source of OACG108).</summary>
        internal IReadOnlyList<OpenApiSemanticWarning> Warnings { get; }
    }

    internal sealed class OpenApiSemanticOperation
    {
        internal OpenApiSemanticOperation(
            string operationId,
            string summary,
            string httpMethod,
            string path,
            IReadOnlyList<OpenApiSemanticParameter> parameters,
            OpenApiSemanticRequestBody? requestBody,
            OpenApiSemanticSchema? responseSchema,
            IReadOnlyList<string> successStatusCodes,
            IReadOnlyDictionary<string, IReadOnlyList<string>> responseMediaTypes,
            OpenApiSourceLocation location)
        {
            OperationId = operationId;
            Summary = summary ?? string.Empty;
            HttpMethod = httpMethod;
            Path = path;
            Parameters = parameters;
            RequestBody = requestBody;
            ResponseSchema = responseSchema;
            SuccessStatusCodes = successStatusCodes;
            ResponseMediaTypes = responseMediaTypes;
            Location = location;
        }

        internal string OperationId { get; }

        internal string Summary { get; }

        internal string HttpMethod { get; }

        internal string Path { get; }

        internal IReadOnlyList<OpenApiSemanticParameter> Parameters { get; }

        internal OpenApiSemanticRequestBody? RequestBody { get; }

        internal OpenApiSemanticSchema? ResponseSchema { get; }

        internal IReadOnlyList<string> SuccessStatusCodes { get; }

        internal IReadOnlyDictionary<string, IReadOnlyList<string>> ResponseMediaTypes { get; }

        internal OpenApiSourceLocation Location { get; }
    }

    internal sealed class OpenApiSemanticParameter
    {
        internal OpenApiSemanticParameter(
            string name,
            string locationName,
            bool required,
            OpenApiSemanticSchema schema,
            OpenApiSourceLocation location)
        {
            Name = name;
            LocationName = locationName;
            Required = required;
            Schema = schema;
            Location = location;
        }

        internal string Name { get; }

        internal string LocationName { get; }

        internal bool Required { get; }

        internal OpenApiSemanticSchema Schema { get; }

        internal OpenApiSourceLocation Location { get; }

        internal string Identity => LocationName == "header"
            ? LocationName + ":" + Name.ToUpperInvariant()
            : LocationName + ":" + Name;
    }

    internal sealed class OpenApiSemanticRequestBody
    {
        internal OpenApiSemanticRequestBody(
            bool required,
            string mediaType,
            OpenApiSemanticSchema schema,
            OpenApiSourceLocation location)
        {
            Required = required;
            MediaType = mediaType;
            Schema = schema;
            Location = location;
        }

        internal bool Required { get; }

        internal string MediaType { get; }

        internal OpenApiSemanticSchema Schema { get; }

        internal OpenApiSourceLocation Location { get; }
    }

    internal enum OpenApiSemanticSchemaKind
    {
        String,
        Integer,
        Number,
        Boolean,
        Object,
        Array,
        Reference,
        Enum,
        // Added for the spec-coverage work. The parser does not produce these yet.
        Any,
        Map,
        Union,
        Binary,
        /// <summary>An object with no declared properties whose members are not constrained (JObject).</summary>
        FreeObject
    }

    /// <summary>How an object schema treats members that are not listed in properties.</summary>
    internal enum OpenApiSemanticAdditionalProperties
    {
        /// <summary>additionalProperties: false.</summary>
        Closed,
        /// <summary>additionalProperties omitted or true: unknown members are preserved untyped.</summary>
        Open,
        /// <summary>additionalProperties is a schema (ValueSchema).</summary>
        Typed
    }

    internal sealed class OpenApiSemanticSchema
    {
        internal OpenApiSemanticSchema(
            OpenApiSemanticSchemaKind kind,
            string suggestedName,
            string format,
            bool nullable,
            string referenceName,
            IReadOnlyList<OpenApiSemanticProperty> properties,
            OpenApiSemanticSchema? itemSchema,
            IReadOnlyList<string> enumValues,
            OpenApiSourceLocation location,
            NormalizedSpecNodeIdentity identity = default,
            NormalizedSpecNodeIdentity referenceIdentity = default,
            OpenApiSemanticAnnotations? annotations = null,
            IReadOnlyList<OpenApiSemanticLiteral>? typedEnumValues = null,
            OpenApiSemanticSchema? valueSchema = null,
            IReadOnlyList<OpenApiSemanticSchema>? unionMembers = null,
            OpenApiSemanticAdditionalProperties additionalProperties = OpenApiSemanticAdditionalProperties.Closed,
            IReadOnlyList<string?>? enumMemberNames = null,
            IReadOnlyList<string?>? enumMemberDescriptions = null)
        {
            AdditionalProperties = additionalProperties;
            EnumMemberNames = enumMemberNames ?? Array.Empty<string?>();
            EnumMemberDescriptions = enumMemberDescriptions ?? Array.Empty<string?>();
            Annotations = annotations ?? OpenApiSemanticAnnotations.Empty;
            ValueSchema = valueSchema;
            UnionMembers = unionMembers ?? Array.Empty<OpenApiSemanticSchema>();
            if (typedEnumValues is not null)
            {
                TypedEnumValues = typedEnumValues;
                var rawValues = new List<string>(typedEnumValues.Count);
                foreach (OpenApiSemanticLiteral literal in typedEnumValues)
                {
                    rawValues.Add(literal.RawText);
                }

                if (enumValues is not null && enumValues.Count > 0 && !enumValues.SequenceEqual(rawValues))
                {
                    throw new ArgumentException(
                        "enumValues and typedEnumValues disagree; pass only one of them.",
                        nameof(typedEnumValues));
                }

                enumValues = rawValues;
            }
            else
            {
                var literals = new List<OpenApiSemanticLiteral>();
                foreach (string value in enumValues ?? Array.Empty<string>())
                {
                    literals.Add(OpenApiSemanticLiteral.FromString(value));
                }

                TypedEnumValues = literals;
            }

            Kind = kind;
            SuggestedName = suggestedName ?? string.Empty;
            Format = format ?? string.Empty;
            Nullable = nullable;
            ReferenceName = referenceName ?? string.Empty;
            Properties = properties ?? Array.Empty<OpenApiSemanticProperty>();
            ItemSchema = itemSchema;
            EnumValues = enumValues ?? Array.Empty<string>();
            Location = location;
            Identity = identity;
            ReferenceIdentity = referenceIdentity;
        }

        internal OpenApiSemanticSchemaKind Kind { get; }

        internal string SuggestedName { get; }

        internal string Format { get; }

        internal bool Nullable { get; }

        internal string ReferenceName { get; }

        internal IReadOnlyList<OpenApiSemanticProperty> Properties { get; }

        internal OpenApiSemanticSchema? ItemSchema { get; }

        /// <summary>Wire strings of the enum values (RawText of TypedEnumValues).</summary>
        internal IReadOnlyList<string> EnumValues { get; }

        /// <summary>Typed enum values. Existing string enums become String literals.</summary>
        internal IReadOnlyList<OpenApiSemanticLiteral> TypedEnumValues { get; }

        /// <summary>description / deprecated / readOnly / writeOnly / default / const.</summary>
        internal OpenApiSemanticAnnotations Annotations { get; }

        /// <summary>Handling of undeclared members when Kind is Object.</summary>
        internal OpenApiSemanticAdditionalProperties AdditionalProperties { get; }

        /// <summary>
        /// C# member names from x-enum-varnames / x-enumNames, parallel to TypedEnumValues.
        /// Empty when the schema declares none; an entry is null when not provided.
        /// </summary>
        internal IReadOnlyList<string?> EnumMemberNames { get; }

        /// <summary>Per-value descriptions from x-enum-descriptions, parallel to TypedEnumValues.</summary>
        internal IReadOnlyList<string?> EnumMemberDescriptions { get; }

        /// <summary>
        /// Value schema when Kind is Map, or when Kind is Object and AdditionalProperties is Typed.
        /// </summary>
        internal OpenApiSemanticSchema? ValueSchema { get; }

        /// <summary>Members when Kind is Union.</summary>
        internal IReadOnlyList<OpenApiSemanticSchema> UnionMembers { get; }

        internal OpenApiSourceLocation Location { get; }

        internal NormalizedSpecNodeIdentity Identity { get; }

        internal NormalizedSpecNodeIdentity ReferenceIdentity { get; }
    }

    internal sealed class OpenApiSemanticProperty
    {
        internal OpenApiSemanticProperty(
            string wireName,
            bool required,
            OpenApiSemanticSchema schema,
            OpenApiSourceLocation location)
        {
            WireName = wireName;
            Required = required;
            Schema = schema;
            Location = location;
        }

        internal string WireName { get; }

        internal bool Required { get; }

        internal OpenApiSemanticSchema Schema { get; }

        internal OpenApiSourceLocation Location { get; }
    }
}
