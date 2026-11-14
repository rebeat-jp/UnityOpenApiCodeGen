using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>How a request or response body is put on the wire (P4: G21, G22).</summary>
    internal enum OpenApiSemanticContentKind
    {
        /// <summary>application/json and application/*+json.</summary>
        Json,
        /// <summary>application/x-www-form-urlencoded (requests only).</summary>
        FormUrlEncoded,
        /// <summary>multipart/form-data (requests only).</summary>
        Multipart,
        /// <summary>text/*, application/xml and +xml with a scalar (string) schema: a raw string.</summary>
        Text,
        /// <summary>Opaque bytes: application/octet-stream, image/*, *&#47;* or a schema with format: binary.</summary>
        Binary
    }

    /// <summary>
    /// The Encoding Object of one property of a form-urlencoded or multipart body. Style, Explode
    /// and AllowReserved are the effective values (defaults applied).
    /// </summary>
    internal sealed class OpenApiSemanticEncoding
    {
        internal OpenApiSemanticEncoding(
            string contentType,
            string style,
            bool explode,
            bool allowReserved,
            IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            ContentType = contentType ?? string.Empty;
            Style = style ?? string.Empty;
            Explode = explode;
            AllowReserved = allowReserved;
            Headers = headers ?? Array.Empty<KeyValuePair<string, string>>();
        }

        /// <summary>Declared contentType of the part; empty when the default applies.</summary>
        internal string ContentType { get; }

        internal string Style { get; }

        internal bool Explode { get; }

        internal bool AllowReserved { get; }

        /// <summary>Static part headers (name, value) taken from the const, default or example of the header schema.</summary>
        internal IReadOnlyList<KeyValuePair<string, string>> Headers { get; }
    }

    /// <summary>One media type entry of a request body or response.</summary>
    internal sealed class OpenApiSemanticMediaContent
    {
        internal OpenApiSemanticMediaContent(
            string mediaType,
            OpenApiSemanticContentKind kind,
            OpenApiSemanticSchema schema,
            IReadOnlyDictionary<string, OpenApiSemanticEncoding> encodings,
            bool wildcard,
            OpenApiSourceLocation location)
        {
            MediaType = mediaType;
            Kind = kind;
            Schema = schema;
            Encodings = encodings;
            Wildcard = wildcard;
            Location = location;
        }

        /// <summary>Normalized media type as declared (parameters kept).</summary>
        internal string MediaType { get; }

        internal OpenApiSemanticContentKind Kind { get; }

        /// <summary>The schema; synthesized (String / Binary) when the media type declares none.</summary>
        internal OpenApiSemanticSchema Schema { get; }

        /// <summary>Encoding Objects by property name (form-urlencoded and multipart only).</summary>
        internal IReadOnlyDictionary<string, OpenApiSemanticEncoding> Encodings { get; }

        /// <summary>True when the media type contains a wildcard (*&#47;*, image/*, *+json).</summary>
        internal bool Wildcard { get; }

        internal OpenApiSourceLocation Location { get; }
    }

    /// <summary>A Header Object of a response (style simple).</summary>
    internal sealed class OpenApiSemanticHeader
    {
        internal OpenApiSemanticHeader(
            string name,
            bool required,
            OpenApiSemanticSchema schema,
            string description,
            bool deprecated,
            OpenApiSourceLocation location)
        {
            Name = name;
            Required = required;
            Schema = schema;
            Description = description ?? string.Empty;
            Deprecated = deprecated;
            Location = location;
        }

        internal string Name { get; }

        internal bool Required { get; }

        internal OpenApiSemanticSchema Schema { get; }

        internal string Description { get; }

        internal bool Deprecated { get; }

        internal OpenApiSourceLocation Location { get; }
    }

    internal enum OpenApiSemanticResponseClass
    {
        /// <summary>2xx, 2XX, or default when no 2xx is declared.</summary>
        Success,
        /// <summary>4xx, 5xx, 4XX, 5XX and default next to a 2xx.</summary>
        Error,
        /// <summary>3xx / 3XX: followed by HttpClient; judged on the final response.</summary>
        Redirect,
        /// <summary>1xx / 1XX.</summary>
        Informational
    }

    /// <summary>One entry of the responses map.</summary>
    internal sealed class OpenApiSemanticResponse
    {
        internal OpenApiSemanticResponse(
            string statusKey,
            OpenApiSemanticResponseClass responseClass,
            IReadOnlyList<OpenApiSemanticMediaContent> contents,
            IReadOnlyList<OpenApiSemanticHeader> headers,
            OpenApiSourceLocation location,
            string contractKey = "")
        {
            ContractKey = contractKey ?? string.Empty;
            StatusKey = statusKey;
            ResponseClass = responseClass;
            Contents = contents;
            Headers = headers;
            Location = location;
        }

        /// <summary>Upper-case key: "200", "2XX", "404", "DEFAULT".</summary>
        internal string StatusKey { get; }

        internal OpenApiSemanticResponseClass ResponseClass { get; }

        /// <summary>
        /// Media types in priority order (JSON, text, binary). For a success response the first
        /// one is the primary content. Error responses keep only the JSON entries that parsed.
        /// </summary>
        internal IReadOnlyList<OpenApiSemanticMediaContent> Contents { get; }

        internal IReadOnlyList<OpenApiSemanticHeader> Headers { get; }

        internal OpenApiSourceLocation Location { get; }

        /// <summary>
        /// Identity of the body contract of a success response (kind plus schema signature).
        /// Responses with the same key are served by one result type.
        /// </summary>
        internal string ContractKey { get; }
    }
}
