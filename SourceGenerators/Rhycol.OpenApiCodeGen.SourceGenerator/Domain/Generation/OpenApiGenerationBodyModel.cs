using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    // P4 (G21-G26): generation model of request body variants, success / error response contracts
    // and response headers.

    internal enum GeneratedBodyKind
    {
        Json,
        FormUrlEncoded,
        Multipart,
        Text,
        Binary
    }

    /// <summary>How one property of a form-urlencoded or multipart body is written.</summary>
    internal enum GeneratedFormFieldRole
    {
        /// <summary>A scalar, an array of scalars or a primitive object: serialized with the form style rules (urlencoded) or as text parts (multipart).</summary>
        Text,
        /// <summary>A nested value written as JSON (an urlencoded value or a JSON part).</summary>
        Json,
        /// <summary>One file part (FileParameter).</summary>
        File,
        /// <summary>One file part per item (List of FileParameter).</summary>
        FileList
    }

    internal sealed class GeneratedFormFieldModel
    {
        internal GeneratedFormFieldModel(
            string memberName,
            string wireName,
            GeneratedFormFieldRole role,
            GeneratedTypeModel type,
            bool required,
            GeneratedParameterModel? styleParameter,
            string contentType,
            bool allowReserved,
            IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            MemberName = memberName;
            WireName = wireName;
            Role = role;
            Type = type;
            Required = required;
            StyleParameter = styleParameter;
            ContentType = contentType ?? string.Empty;
            AllowReserved = allowReserved;
            Headers = headers ?? Array.Empty<KeyValuePair<string, string>>();
        }

        /// <summary>C# member name on the body DTO.</summary>
        internal string MemberName { get; }

        internal string WireName { get; }

        internal GeneratedFormFieldRole Role { get; }

        internal GeneratedTypeModel Type { get; }

        /// <summary>True when the member must be present (required and not nullable).</summary>
        internal bool Required { get; }

        /// <summary>
        /// A query-style parameter model of the member (Name is the member access expression on the
        /// body); used to serialize urlencoded fields with the parameter style rules.
        /// </summary>
        internal GeneratedParameterModel? StyleParameter { get; }

        /// <summary>Declared or default Content-Type of the part; empty when none applies.</summary>
        internal string ContentType { get; }

        internal bool AllowReserved { get; }

        /// <summary>Static part headers (multipart).</summary>
        internal IReadOnlyList<KeyValuePair<string, string>> Headers { get; }
    }

    /// <summary>One media type of a request body; each variant is sent by its own method.</summary>
    internal sealed class GeneratedBodyVariantModel
    {
        internal GeneratedBodyVariantModel(
            GeneratedBodyKind kind,
            string mediaType,
            GeneratedTypeModel type,
            string methodSuffix,
            IReadOnlyList<GeneratedFormFieldModel>? fields = null,
            string? contentTypeParameterName = null,
            string? specifiedParameterName = null)
        {
            Kind = kind;
            MediaType = mediaType;
            Type = type;
            MethodSuffix = methodSuffix ?? string.Empty;
            Fields = fields ?? Array.Empty<GeneratedFormFieldModel>();
            ContentTypeParameterName = contentTypeParameterName;
            SpecifiedParameterName = specifiedParameterName;
        }

        internal GeneratedBodyKind Kind { get; }

        /// <summary>Name of the method that sends this variant (assigned by the builder).</summary>
        internal string MethodName { get; set; } = string.Empty;

        /// <summary>Name of the WithHttpInfo method of this variant; empty when the operation has none.</summary>
        internal string HttpInfoMethodName { get; set; } = string.Empty;

        /// <summary>The media type sent as Content-Type (a wildcard binary type is sent as application/octet-stream unless the caller passes one).</summary>
        internal string MediaType { get; }

        /// <summary>Type of the body parameter (nullable when the body is optional).</summary>
        internal GeneratedTypeModel Type { get; }

        /// <summary>Appended to the operation name for an additional method ("AsMultipart"); empty for the primary method.</summary>
        internal string MethodSuffix { get; }

        internal IReadOnlyList<GeneratedFormFieldModel> Fields { get; }

        /// <summary>Optional Content-Type argument of a binary body declared with a wildcard media type.</summary>
        internal string? ContentTypeParameterName { get; }

        /// <summary>Presence flag of an optional nullable JSON body.</summary>
        internal string? SpecifiedParameterName { get; }
    }

    internal enum GeneratedResponseBodyKind
    {
        None,
        Json,
        Text,
        Binary
    }

    /// <summary>A way to decode a response body, selected by the received Content-Type.</summary>
    internal sealed class GeneratedResponseContentModel
    {
        internal GeneratedResponseContentModel(GeneratedResponseBodyKind kind, string mediaType)
        {
            Kind = kind;
            MediaType = mediaType;
        }

        internal GeneratedResponseBodyKind Kind { get; }

        internal string MediaType { get; }
    }

    /// <summary>A declared response header, exposed as a typed property of the HttpInfo result.</summary>
    internal sealed class GeneratedResponseHeaderModel
    {
        internal GeneratedResponseHeaderModel(
            string wireName,
            string propertyName,
            bool required,
            GeneratedTypeModel type,
            string description,
            bool deprecated)
        {
            WireName = wireName;
            PropertyName = propertyName;
            Required = required;
            Type = type;
            Description = description ?? string.Empty;
            Deprecated = deprecated;
        }

        internal string WireName { get; }

        internal string PropertyName { get; }

        internal bool Required { get; }

        /// <summary>Nullable when the header is optional.</summary>
        internal GeneratedTypeModel Type { get; }

        internal string Description { get; }

        internal bool Deprecated { get; }
    }

    /// <summary>One success response of the advanced response path.</summary>
    internal sealed class GeneratedSuccessResponseModel
    {
        internal GeneratedSuccessResponseModel(
            string statusKey,
            GeneratedTypeModel? type,
            IReadOnlyList<GeneratedResponseContentModel> contents,
            IReadOnlyList<GeneratedResponseHeaderModel> headers,
            string accessorName)
        {
            StatusKey = statusKey;
            Type = type;
            Contents = contents;
            Headers = headers;
            AccessorName = accessorName;
        }

        /// <summary>"200", "2XX", "DEFAULT" (the success contract when no 2xx is declared) or "ANY" (no response declared).</summary>
        internal string StatusKey { get; }

        /// <summary>Body type; null when the response has no body.</summary>
        internal GeneratedTypeModel? Type { get; }

        /// <summary>Decoders in priority order; the first is the primary media type.</summary>
        internal IReadOnlyList<GeneratedResponseContentModel> Contents { get; }

        internal IReadOnlyList<GeneratedResponseHeaderModel> Headers { get; }

        /// <summary>Member name in the result wrapper ("Status200", "Status2XX", "StatusDefault").</summary>
        internal string AccessorName { get; }
    }

    internal sealed class GeneratedErrorResponseModel
    {
        internal GeneratedErrorResponseModel(string statusKey, GeneratedTypeModel? type)
        {
            StatusKey = statusKey;
            Type = type;
        }

        /// <summary>"404", "4XX", "5XX" or "DEFAULT".</summary>
        internal string StatusKey { get; }

        /// <summary>Null for a declared status without a JSON body (no typed error, and no fallback to default).</summary>
        internal GeneratedTypeModel? Type { get; }
    }

    /// <summary>The status-keyed result type of an operation whose success responses carry different contracts.</summary>
    internal sealed class GeneratedResponseWrapperModel
    {
        internal GeneratedResponseWrapperModel(
            string typeName,
            IReadOnlyList<GeneratedSuccessResponseModel> members,
            string summary)
        {
            TypeName = typeName;
            Members = members;
            Summary = summary ?? string.Empty;
        }

        internal string TypeName { get; }

        internal IReadOnlyList<GeneratedSuccessResponseModel> Members { get; }

        internal string Summary { get; }
    }

    /// <summary>The result of the WithHttpInfo method: status, raw and typed headers and the data.</summary>
    internal sealed class GeneratedHttpInfoModel
    {
        internal GeneratedHttpInfoModel(
            string typeName,
            IReadOnlyList<GeneratedResponseHeaderModel> headers,
            string summary)
        {
            TypeName = typeName;
            Headers = headers;
            Summary = summary ?? string.Empty;
        }

        internal string TypeName { get; }

        /// <summary>Name of the WithHttpInfo method of an operation without a request body (assigned by the builder).</summary>
        internal string MethodName { get; set; } = string.Empty;

        /// <summary>Union of the headers declared by the success responses.</summary>
        internal IReadOnlyList<GeneratedResponseHeaderModel> Headers { get; }

        internal string Summary { get; }
    }

    /// <summary>
    /// The advanced response path of an operation (text / binary bodies, several media types,
    /// different contracts per status, default-only or omitted responses, headers). Operations
    /// that fit the original path (void or one JSON contract) do not have a plan.
    /// </summary>
    internal sealed class GeneratedResponsePlanModel
    {
        internal GeneratedResponsePlanModel(
            IReadOnlyList<GeneratedSuccessResponseModel> successes,
            GeneratedTypeModel? dataType,
            GeneratedResponseWrapperModel? wrapper,
            GeneratedHttpInfoModel? httpInfo,
            IReadOnlyList<string> acceptMediaTypes)
        {
            Successes = successes;
            DataType = dataType;
            Wrapper = wrapper;
            HttpInfo = httpInfo;
            AcceptMediaTypes = acceptMediaTypes;
        }

        /// <summary>In match order: exact status codes, then ranges, then DEFAULT / ANY.</summary>
        internal IReadOnlyList<GeneratedSuccessResponseModel> Successes { get; }

        /// <summary>The body type returned by the method when there is no wrapper; null for void.</summary>
        internal GeneratedTypeModel? DataType { get; }

        internal GeneratedResponseWrapperModel? Wrapper { get; }

        internal GeneratedHttpInfoModel? HttpInfo { get; }

        /// <summary>Media types sent in Accept, primary first.</summary>
        internal IReadOnlyList<string> AcceptMediaTypes { get; }

        /// <summary>The C# type returned by the data-returning method (without Task); null for void.</summary>
        internal string? ReturnTypeName => Wrapper is not null
            ? Wrapper.TypeName
            : DataType is null ? null : GeneratedSourceEmitter.TypeName(DataType);
    }
}
