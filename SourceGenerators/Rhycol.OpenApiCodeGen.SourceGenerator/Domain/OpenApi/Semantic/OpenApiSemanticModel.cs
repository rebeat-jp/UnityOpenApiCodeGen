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
            string logicalPath,
            string? identityLogicalPath = null)
        {
            SourcePath = sourcePath ?? string.Empty;
            DocumentId = documentId ?? "root";
            Line = line;
            Column = column;
            LogicalPath = logicalPath ?? string.Empty;
            IdentityLogicalPath = identityLogicalPath ?? LogicalPath;
        }

        internal string SourcePath { get; }

        internal string DocumentId { get; }

        internal int Line { get; }

        internal int Column { get; }

        /// <summary>
        /// The path reported in diagnostics. For a node synthesized from a Swagger 2.0 document this is
        /// the original Swagger path.
        /// </summary>
        internal string LogicalPath { get; }

        /// <summary>
        /// The path of the node in the tree the parser walked (the key of the node's identity). Equal to
        /// <see cref="LogicalPath"/> except for nodes synthesized from a Swagger 2.0 document.
        /// </summary>
        internal string IdentityLogicalPath { get; }

        internal static OpenApiSourceLocation FromNode(SpecNode node)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            return new OpenApiSourceLocation(
                string.Empty,
                "root",
                node.Line,
                node.Column,
                node.DiagnosticLogicalPath,
                node.LogicalPath);
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
                node.DiagnosticLogicalPath,
                node.LogicalPath);
        }

        public bool Equals(OpenApiSourceLocation other)
        {
            return Line == other.Line &&
                   Column == other.Column &&
                   string.Equals(SourcePath, other.SourcePath, StringComparison.Ordinal) &&
                   string.Equals(DocumentId, other.DocumentId, StringComparison.Ordinal) &&
                   string.Equals(LogicalPath, other.LogicalPath, StringComparison.Ordinal) &&
                   string.Equals(IdentityLogicalPath, other.IdentityLogicalPath, StringComparison.Ordinal);
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
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(IdentityLogicalPath);
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
            IReadOnlyList<OpenApiSemanticWarning>? warnings = null,
            IReadOnlyList<OpenApiSemanticServer>? servers = null,
            IReadOnlyList<OpenApiSemanticSecurityScheme>? securitySchemes = null)
        {
            SecuritySchemes = securitySchemes ?? Array.Empty<OpenApiSemanticSecurityScheme>();
            Servers = servers ?? Array.Empty<OpenApiSemanticServer>();
            Warnings = warnings ?? Array.Empty<OpenApiSemanticWarning>();
            MinorVersion = minorVersion;
            BaseUrl = baseUrl ?? string.Empty;
            Schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            Operations = operations ?? throw new ArgumentNullException(nameof(operations));
            Location = location;
        }

        internal int MinorVersion { get; }

        /// <summary>components.securitySchemes in key order.</summary>
        internal IReadOnlyList<OpenApiSemanticSecurityScheme> SecuritySchemes { get; }

        internal string BaseUrl { get; }

        /// <summary>Document-level servers. BaseUrl is the default URL of the first one.</summary>
        internal IReadOnlyList<OpenApiSemanticServer> Servers { get; }

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
            OpenApiSourceLocation location,
            string description = "",
            bool deprecated = false,
            IReadOnlyList<OpenApiSemanticServer>? servers = null,
            IReadOnlyList<OpenApiSemanticResponse>? responses = null,
            IReadOnlyList<OpenApiSemanticSecurityRequirement>? securityRequirements = null)
        {
            SecurityRequirements = securityRequirements ?? Array.Empty<OpenApiSemanticSecurityRequirement>();
            Responses = responses ?? Array.Empty<OpenApiSemanticResponse>();
            Description = description ?? string.Empty;
            Deprecated = deprecated;
            Servers = servers ?? Array.Empty<OpenApiSemanticServer>();
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

        internal string Description { get; }

        internal bool Deprecated { get; }

        /// <summary>
        /// The effective security requirements (OR): the operation's own, otherwise the document's.
        /// Empty means no authentication.
        /// </summary>
        internal IReadOnlyList<OpenApiSemanticSecurityRequirement> SecurityRequirements { get; }

        /// <summary>
        /// Servers declared on the operation, otherwise on its Path Item. Empty when the operation
        /// uses the document-level servers.
        /// </summary>
        internal IReadOnlyList<OpenApiSemanticServer> Servers { get; }

        internal string HttpMethod { get; }

        internal string Path { get; }

        internal IReadOnlyList<OpenApiSemanticParameter> Parameters { get; }

        internal OpenApiSemanticRequestBody? RequestBody { get; }

        internal OpenApiSemanticSchema? ResponseSchema { get; }

        internal IReadOnlyList<string> SuccessStatusCodes { get; }

        internal IReadOnlyDictionary<string, IReadOnlyList<string>> ResponseMediaTypes { get; }

        /// <summary>Every declared response (success, error, redirect) with its contents and headers.</summary>
        internal IReadOnlyList<OpenApiSemanticResponse> Responses { get; }

        internal OpenApiSourceLocation Location { get; }
    }

    internal sealed class OpenApiSemanticParameter
    {
        internal OpenApiSemanticParameter(
            string name,
            string locationName,
            bool required,
            OpenApiSemanticSchema schema,
            OpenApiSourceLocation location,
            string style = "",
            bool explode = false,
            bool allowReserved = false,
            string contentMediaType = "",
            string description = "",
            bool deprecated = false,
            IReadOnlyDictionary<string, OpenApiSemanticEncoding>? encodings = null)
        {
            Encodings = encodings;
            Name = name;
            LocationName = locationName;
            Required = required;
            Schema = schema;
            Location = location;
            Style = style ?? string.Empty;
            Explode = explode;
            AllowReserved = allowReserved;
            ContentMediaType = contentMediaType ?? string.Empty;
            Description = description ?? string.Empty;
            Deprecated = deprecated;
        }

        /// <summary>Effective serialization style (the default for the location when omitted).</summary>
        internal string Style { get; }

        /// <summary>Effective explode value (the default for the style when omitted).</summary>
        internal bool Explode { get; }

        internal bool AllowReserved { get; }

        /// <summary>Media type of the parameter content; empty when the parameter uses schema.</summary>
        internal string ContentMediaType { get; }

        /// <summary>Encoding Objects of an in: querystring form-urlencoded content; null otherwise.</summary>
        internal IReadOnlyDictionary<string, OpenApiSemanticEncoding>? Encodings { get; }

        internal string Description { get; }

        internal bool Deprecated { get; }

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
            OpenApiSourceLocation location,
            IReadOnlyList<OpenApiSemanticMediaContent>? contents = null)
        {
            Required = required;
            MediaType = mediaType;
            Schema = schema;
            Location = location;
            Contents = contents ?? Array.Empty<OpenApiSemanticMediaContent>();
        }

        /// <summary>
        /// Every supported media type in priority order (JSON, form-urlencoded, multipart, text,
        /// binary). The first one is the primary content; MediaType and Schema describe it.
        /// </summary>
        internal IReadOnlyList<OpenApiSemanticMediaContent> Contents { get; }

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
        // Any, Map, FreeObject, Union and Binary (format: binary, outside JSON) are produced by the parser.
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
            IReadOnlyList<string?>? enumMemberDescriptions = null,
            OpenApiSemanticDiscriminator? discriminator = null,
            bool unionIsOneOf = true,
            IReadOnlyList<OpenApiSemanticSchema>? notSchemas = null,
            string contentMediaType = "",
            IReadOnlyList<OpenApiSemanticConstraints>? constraintSets = null)
        {
            ConstraintSets = constraintSets ?? Array.Empty<OpenApiSemanticConstraints>();
            ContentMediaType = contentMediaType ?? string.Empty;
            Discriminator = discriminator;
            UnionIsOneOf = unionIsOneOf;
            NotSchemas = notSchemas ?? Array.Empty<OpenApiSemanticSchema>();
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

        /// <summary>Discriminator of a Union (null when the union has none).</summary>
        internal OpenApiSemanticDiscriminator? Discriminator { get; }

        /// <summary>True for oneOf (exactly one member matches), false for anyOf (at least one).</summary>
        internal bool UnionIsOneOf { get; }

        /// <summary>
        /// Schemas of the not keyword. They only take part in response validation: a value that
        /// matches one of them is rejected. They never change the generated type.
        /// </summary>
        internal IReadOnlyList<OpenApiSemanticSchema> NotSchemas { get; }

        /// <summary>contentMediaType of a string schema without contentEncoding (a file part in multipart); empty otherwise.</summary>
        internal string ContentMediaType { get; }

        /// <summary>
        /// Validation keyword sets (minLength, pattern, minimum, ...), all of which must hold. They are
        /// checked at run time (requests before sending, responses when received) and never change the
        /// generated type.
        /// </summary>
        internal IReadOnlyList<OpenApiSemanticConstraints> ConstraintSets { get; }

        internal OpenApiSemanticSchema WithNullable(bool nullable)
        {
            return nullable == Nullable ? this : Clone(nullable, Annotations, NotSchemas, ConstraintSets);
        }

        internal OpenApiSemanticSchema WithAnnotations(OpenApiSemanticAnnotations annotations)
        {
            return Clone(Nullable, annotations, NotSchemas, ConstraintSets);
        }

        internal OpenApiSemanticSchema WithNot(OpenApiSemanticSchema notSchema)
        {
            return Clone(Nullable, Annotations, NotSchemas.Concat(new[] { notSchema }).ToArray(), ConstraintSets);
        }

        internal OpenApiSemanticSchema WithConstraintSets(IReadOnlyList<OpenApiSemanticConstraints> sets)
        {
            return sets.Count == 0
                ? this
                : Clone(Nullable, Annotations, NotSchemas, ConstraintSets.Concat(sets).ToArray());
        }

        private OpenApiSemanticSchema Clone(
            bool nullable,
            OpenApiSemanticAnnotations annotations,
            IReadOnlyList<OpenApiSemanticSchema> notSchemas,
            IReadOnlyList<OpenApiSemanticConstraints> constraintSets)
        {
            return new OpenApiSemanticSchema(
                Kind,
                SuggestedName,
                Format,
                nullable,
                ReferenceName,
                Properties,
                ItemSchema,
                EnumValues,
                Location,
                Identity,
                ReferenceIdentity,
                annotations,
                TypedEnumValues,
                ValueSchema,
                UnionMembers,
                AdditionalProperties,
                EnumMemberNames,
                EnumMemberDescriptions,
                Discriminator,
                UnionIsOneOf,
                notSchemas,
                ContentMediaType,
                constraintSets);
        }

        internal OpenApiSourceLocation Location { get; }

        internal NormalizedSpecNodeIdentity Identity { get; }

        internal NormalizedSpecNodeIdentity ReferenceIdentity { get; }
    }

    /// <summary>
    /// A discriminator of a Union. MemberValues is parallel to the union members: the wire values
    /// that select the member (the first one is written for requests).
    /// </summary>
    internal sealed class OpenApiSemanticDiscriminator
    {
        internal OpenApiSemanticDiscriminator(
            string propertyName,
            IReadOnlyList<IReadOnlyList<string>> memberValues,
            int defaultMemberIndex = -1)
        {
            PropertyName = propertyName;
            MemberValues = memberValues;
            DefaultMemberIndex = defaultMemberIndex;
        }

        internal string PropertyName { get; }

        internal IReadOnlyList<IReadOnlyList<string>> MemberValues { get; }

        /// <summary>OpenAPI 3.2 defaultMapping: index of the member used when no mapping matches; -1 when absent.</summary>
        internal int DefaultMemberIndex { get; }
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
