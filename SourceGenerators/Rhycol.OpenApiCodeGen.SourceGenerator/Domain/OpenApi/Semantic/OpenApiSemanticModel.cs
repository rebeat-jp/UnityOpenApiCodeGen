using System;
using System.Collections.Generic;

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
            OpenApiSourceLocation location)
        {
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

        internal string Identity => LocationName + ":" + Name;
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
        Enum
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
            NormalizedSpecNodeIdentity referenceIdentity = default)
        {
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

        internal IReadOnlyList<string> EnumValues { get; }

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
