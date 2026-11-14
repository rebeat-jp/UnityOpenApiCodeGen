using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// P4 (G21-G26): request body variants (json / form-urlencoded / multipart / text / binary),
    /// success response contracts (result wrappers, text and binary bodies, headers) and typed errors.
    /// </summary>
    internal sealed partial class OpenApiGenerationModelBuilder
    {
        private string _fileParameterTypeName = string.Empty;

        // Locals of the generated request and response code; operation parameters never take these names.
        private static readonly string[] ReservedBodyLocalNames =
        {
            "oacgContent", "oacgForm", "oacgPart", "oacgMultipart", "oacgText", "oacgFile",
            "oacgStatus", "oacgBody", "oacgBytes", "oacgData", "oacgMediaType", "oacgHeaders",
            "oacgHeader", "oacgValues", "oacgError", "oacgErrorBody", "oacgErrorFailure", "oacgItem",
            "oacgItems", "oacgSecurity", "oacgAccept", "oacgResult", "oacgValue", "oacgHeader0", "oacgHeader1", "oacgHeader2", "oacgHeader3", "oacgHeader4", "oacgHeader5", "oacgHeader6", "oacgHeader7", "oacgHeader8", "oacgHeader9"
        };
        private bool _usesFileParameter;

        // ---- request bodies ------------------------------------------------------------------------

        private GeneratedRequestBodyModel BuildRequestBody(
            OpenApiSemanticOperation operation,
            HashSet<string> usedParameterNames)
        {
            OpenApiSemanticRequestBody body = operation.RequestBody!;
            string parameterName = AllocateUniqueName("body", usedParameterNames);
            IReadOnlyList<OpenApiSemanticMediaContent> contents = body.Contents.Count > 0
                ? body.Contents
                : new[]
                {
                    new OpenApiSemanticMediaContent(
                        body.MediaType,
                        OpenApiSemanticContentKind.Json,
                        body.Schema,
                        new Dictionary<string, OpenApiSemanticEncoding>(StringComparer.Ordinal),
                        false,
                        body.Location)
                };
            var variants = new List<GeneratedBodyVariantModel>();
            foreach (OpenApiSemanticMediaContent content in contents)
            {
                GeneratedBodyKind kind = ToBodyKind(content.Kind);
                if (kind == GeneratedBodyKind.Json && variants.Any(static value => value.Kind == GeneratedBodyKind.Json))
                {
                    // Several JSON media types share one schema: the first (concrete) one is sent.
                    continue;
                }

                string suffix = variants.Count == 0 ? string.Empty : CreateBodySuffix(content, variants);
                variants.Add(BuildBodyVariant(body, content, kind, suffix, parameterName, usedParameterNames));
            }

            GeneratedBodyVariantModel primary = variants[0];
            return new GeneratedRequestBodyModel(
                parameterName,
                body.Required,
                primary.MediaType,
                primary.Type,
                primary.SpecifiedParameterName,
                variants);
        }

        private static GeneratedBodyKind ToBodyKind(OpenApiSemanticContentKind kind)
        {
            switch (kind)
            {
                case OpenApiSemanticContentKind.FormUrlEncoded:
                    return GeneratedBodyKind.FormUrlEncoded;
                case OpenApiSemanticContentKind.Multipart:
                    return GeneratedBodyKind.Multipart;
                case OpenApiSemanticContentKind.Text:
                    return GeneratedBodyKind.Text;
                case OpenApiSemanticContentKind.Binary:
                    return GeneratedBodyKind.Binary;
                default:
                    return GeneratedBodyKind.Json;
            }
        }

        private static string CreateBodySuffix(
            OpenApiSemanticMediaContent content,
            IReadOnlyList<GeneratedBodyVariantModel> existing)
        {
            GeneratedBodyKind kind = ToBodyKind(content.Kind);
            if (!existing.Any(value => value.Kind == kind))
            {
                switch (kind)
                {
                    case GeneratedBodyKind.FormUrlEncoded:
                        return "AsFormUrlEncoded";
                    case GeneratedBodyKind.Multipart:
                        return "AsMultipart";
                    case GeneratedBodyKind.Text:
                        return "AsText";
                    case GeneratedBodyKind.Binary:
                        return "AsBinary";
                }
            }

            // A second media type of the same kind is named after the media type itself.
            int parameters = content.MediaType.IndexOf(';');
            string mediaType = parameters < 0 ? content.MediaType : content.MediaType.Substring(0, parameters);
            return "As" + ToIdentifier(mediaType.Replace("*", "Any"), pascalCase: true).TrimStart('@');
        }

        private GeneratedBodyVariantModel BuildBodyVariant(
            OpenApiSemanticRequestBody body,
            OpenApiSemanticMediaContent content,
            GeneratedBodyKind kind,
            string suffix,
            string parameterName,
            HashSet<string> usedParameterNames)
        {
            switch (kind)
            {
                case GeneratedBodyKind.Json:
                {
                    GeneratedTypeModel resolvedType = ResolveType(content.Schema);
                    GeneratedTypeModel type = resolvedType.WithNullable(
                        resolvedType.Nullable || content.Schema.Nullable || !body.Required);
                    string? specifiedParameterName = !body.Required &&
                                                     (resolvedType.Nullable || content.Schema.Nullable)
                        ? AllocateUniqueName(parameterName + "Specified", usedParameterNames)
                        : null;
                    return new GeneratedBodyVariantModel(
                        kind, content.MediaType, type, suffix, null, null, specifiedParameterName);
                }

                case GeneratedBodyKind.FormUrlEncoded:
                case GeneratedBodyKind.Multipart:
                {
                    GeneratedTypeModel resolvedType = ResolveType(content.Schema);
                    if (resolvedType.Kind != GeneratedTypeKind.Named)
                    {
                        throw new InvalidOperationException(
                            "A form or multipart body must resolve to a named object type.");
                    }

                    GeneratedTypeModel type = resolvedType.WithNullable(resolvedType.Nullable || !body.Required);
                    return new GeneratedBodyVariantModel(
                        kind,
                        content.MediaType,
                        type,
                        suffix,
                        BuildFormFields(content, resolvedType, kind, parameterName));
                }

                case GeneratedBodyKind.Text:
                {
                    GeneratedTypeModel resolvedType = ResolveTextType(content.Schema);
                    GeneratedTypeModel type = resolvedType.WithNullable(
                        resolvedType.Nullable || content.Schema.Nullable || !body.Required);
                    return new GeneratedBodyVariantModel(kind, content.MediaType, type, suffix);
                }

                default:
                {
                    GeneratedTypeModel type = new GeneratedTypeModel(GeneratedTypeKind.Binary, !body.Required);
                    string mediaType = content.MediaType;
                    string? contentTypeParameter = null;
                    if (content.Wildcard)
                    {
                        // A wildcard media type cannot be sent as Content-Type: the caller may pass one.
                        contentTypeParameter = AllocateUniqueName(parameterName + "ContentType", usedParameterNames);
                        mediaType = "application/octet-stream";
                    }

                    return new GeneratedBodyVariantModel(kind, mediaType, type, suffix, null, contentTypeParameter);
                }
            }
        }

        /// <summary>The C# type of a text body: a scalar; a format: byte string is plain text here.</summary>
        private GeneratedTypeModel ResolveTextType(OpenApiSemanticSchema schema)
        {
            GeneratedTypeModel type = ResolveType(schema);
            return type.Kind == GeneratedTypeKind.Binary
                ? new GeneratedTypeModel(GeneratedTypeKind.String, type.Nullable)
                : type;
        }

        private OpenApiSemanticSchema GetEffectiveSemantic(OpenApiSemanticSchema schema)
        {
            var visited = new HashSet<NormalizedSpecNodeIdentity>();
            OpenApiSemanticSchema current = schema;
            while (current.Kind == OpenApiSemanticSchemaKind.Reference &&
                   !current.ReferenceIdentity.IsEmpty &&
                   visited.Add(current.ReferenceIdentity) &&
                   _document.Schemas.TryGetValue(current.ReferenceIdentity, out OpenApiSemanticSchema? referenced))
            {
                current = referenced;
            }

            return current;
        }

        private static bool IsFormScalar(GeneratedTypeModel type)
        {
            switch (type.Kind)
            {
                case GeneratedTypeKind.String:
                case GeneratedTypeKind.Int32:
                case GeneratedTypeKind.Int64:
                case GeneratedTypeKind.Single:
                case GeneratedTypeKind.Double:
                case GeneratedTypeKind.Decimal:
                case GeneratedTypeKind.Boolean:
                case GeneratedTypeKind.DateTime:
                case GeneratedTypeKind.DateTimeOffset:
                case GeneratedTypeKind.Guid:
                case GeneratedTypeKind.NamedEnum:
                case GeneratedTypeKind.Binary:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>An object whose members are all primitives (the shape the form style rules can express).</summary>
        private bool IsPrimitiveObject(OpenApiSemanticSchema? schema)
        {
            if (schema is null)
            {
                return false;
            }

            schema = GetEffectiveSemantic(schema);
            if (schema.Kind == OpenApiSemanticSchemaKind.Map)
            {
                return schema.ValueSchema is not null && IsPrimitiveSchema(schema.ValueSchema);
            }

            if (schema.Kind != OpenApiSemanticSchemaKind.Object)
            {
                return false;
            }

            foreach (OpenApiSemanticProperty property in schema.Properties)
            {
                if (!IsPrimitiveSchema(property.Schema))
                {
                    return false;
                }
            }

            return schema.AdditionalProperties != OpenApiSemanticAdditionalProperties.Typed ||
                   (schema.ValueSchema is not null && IsPrimitiveSchema(schema.ValueSchema));
        }

        private bool IsPrimitiveSchema(OpenApiSemanticSchema schema)
        {
            switch (GetEffectiveSemantic(schema).Kind)
            {
                case OpenApiSemanticSchemaKind.String:
                case OpenApiSemanticSchemaKind.Integer:
                case OpenApiSemanticSchemaKind.Number:
                case OpenApiSemanticSchemaKind.Boolean:
                case OpenApiSemanticSchemaKind.Enum:
                    return true;
                default:
                    return false;
            }
        }

        private IReadOnlyList<GeneratedFormFieldModel> BuildFormFields(
            OpenApiSemanticMediaContent content,
            GeneratedTypeModel bodyType,
            GeneratedBodyKind kind,
            string bodyParameterName)
        {
            GeneratedDtoModel? dto = _dtos.FirstOrDefault(
                value => string.Equals(value.Name, bodyType.Name, StringComparison.Ordinal));
            if (dto is null)
            {
                throw new InvalidOperationException("The body type '" + bodyType.Name + "' has no generated DTO.");
            }

            OpenApiSemanticSchema effective = GetEffectiveSemantic(content.Schema);
            var fields = new List<GeneratedFormFieldModel>();
            foreach (GeneratedDtoPropertyModel property in dto.Properties)
            {
                if (property.Annotations.ReadOnly)
                {
                    // A readOnly property is never sent.
                    continue;
                }

                OpenApiSemanticSchema? propertySchema = effective.Properties
                    .FirstOrDefault(value => string.Equals(value.WireName, property.WireName, StringComparison.Ordinal))
                    ?.Schema;
                content.Encodings.TryGetValue(property.WireName, out OpenApiSemanticEncoding? encoding);
                GeneratedTypeModel type = property.Type;
                bool required = property.Required && !type.Nullable;

                GeneratedFormFieldRole role;
                if (type.Kind == GeneratedTypeKind.File)
                {
                    role = GeneratedFormFieldRole.File;
                }
                else if (type.Kind == GeneratedTypeKind.Array && type.ItemType!.Kind == GeneratedTypeKind.File)
                {
                    role = GeneratedFormFieldRole.FileList;
                }
                else if (IsFormScalar(type) ||
                         (type.Kind == GeneratedTypeKind.Array && IsFormScalar(type.ItemType!)) ||
                         (kind == GeneratedBodyKind.FormUrlEncoded && IsPrimitiveObject(propertySchema)))
                {
                    role = GeneratedFormFieldRole.Text;
                }
                else
                {
                    role = GeneratedFormFieldRole.Json;
                }

                string contentType = encoding?.ContentType ?? string.Empty;
                if (contentType.Length == 0)
                {
                    OpenApiSemanticSchema? typed = propertySchema is null ? null : GetEffectiveSemantic(propertySchema);
                    if (typed is not null && typed.Kind == OpenApiSemanticSchemaKind.Array && typed.ItemSchema is not null)
                    {
                        typed = GetEffectiveSemantic(typed.ItemSchema);
                    }

                    if (typed is not null && typed.ContentMediaType.Length > 0)
                    {
                        contentType = typed.ContentMediaType;
                    }
                    else if (role == GeneratedFormFieldRole.Json)
                    {
                        contentType = "application/json";
                    }
                    else if (role == GeneratedFormFieldRole.File || role == GeneratedFormFieldRole.FileList)
                    {
                        contentType = "application/octet-stream";
                    }
                }

                GeneratedParameterModel? styleParameter = null;
                if (kind == GeneratedBodyKind.FormUrlEncoded && role == GeneratedFormFieldRole.Text)
                {
                    styleParameter = new GeneratedParameterModel(
                        bodyParameterName + "." + property.Name,
                        property.WireName,
                        "query",
                        required,
                        type,
                        encoding?.Style ?? "form",
                        encoding?.Explode ?? true,
                        encoding?.AllowReserved ?? false);
                }

                fields.Add(new GeneratedFormFieldModel(
                    property.Name,
                    property.WireName,
                    role,
                    type,
                    required,
                    styleParameter,
                    contentType,
                    encoding?.AllowReserved ?? false,
                    encoding?.Headers ?? Array.Empty<KeyValuePair<string, string>>()));
            }

            return fields;
        }

        // ---- responses -----------------------------------------------------------------------------

        private static int ResponseOrder(string key)
        {
            // Exact codes first, then ranges, then the fallback.
            if (key == "DEFAULT" || key == "ANY")
            {
                return 2;
            }

            return key.IndexOf('X') >= 0 ? 1 : 0;
        }

        private static string ToAccessorName(string key)
        {
            return "Status" + (key == "DEFAULT" ? "Default" : key == "ANY" ? "Any" : key);
        }

        private GeneratedTypeModel? ResolveResponseBodyType(OpenApiSemanticMediaContent content)
        {
            switch (content.Kind)
            {
                case OpenApiSemanticContentKind.Json:
                {
                    GeneratedTypeModel resolved = ResolveType(content.Schema);
                    return resolved.WithNullable(resolved.Nullable || content.Schema.Nullable);
                }

                case OpenApiSemanticContentKind.Text:
                {
                    GeneratedTypeModel resolved = ResolveTextType(content.Schema);
                    return resolved.WithNullable(resolved.Nullable || content.Schema.Nullable);
                }

                default:
                    return new GeneratedTypeModel(GeneratedTypeKind.Binary, false);
            }
        }

        private static GeneratedResponseBodyKind ToResponseKind(OpenApiSemanticContentKind kind)
        {
            switch (kind)
            {
                case OpenApiSemanticContentKind.Json:
                    return GeneratedResponseBodyKind.Json;
                case OpenApiSemanticContentKind.Text:
                    return GeneratedResponseBodyKind.Text;
                default:
                    return GeneratedResponseBodyKind.Binary;
            }
        }

        /// <summary>
        /// Builds the response side of an operation. The original path (void or one JSON contract,
        /// legacy ResponseType) is kept whenever it can express the responses; everything else gets a
        /// GeneratedResponsePlanModel.
        /// </summary>
        private void BuildResponses(
            OpenApiSemanticOperation operation,
            string methodName,
            HashSet<string> usedMethodNames,
            out GeneratedTypeModel? legacyResponseType,
            out GeneratedResponsePlanModel? plan,
            out IReadOnlyList<GeneratedErrorResponseModel> errors)
        {
            legacyResponseType = null;
            plan = null;
            errors = BuildErrorResponses(operation);
            if (operation.Responses.Count == 0 &&
                (operation.SuccessStatusCodes.Count > 0 || operation.ResponseSchema is not null))
            {
                // A semantic model built without the Responses list: the original contract.
                if (operation.ResponseSchema is not null)
                {
                    GeneratedTypeModel resolvedType = ResolveType(operation.ResponseSchema);
                    legacyResponseType = resolvedType.WithNullable(
                        resolvedType.Nullable || operation.ResponseSchema.Nullable);
                }

                return;
            }

            OpenApiSemanticResponse[] successes = operation.Responses
                .Where(static value => value.ResponseClass == OpenApiSemanticResponseClass.Success)
                .OrderBy(static value => ResponseOrder(value.StatusKey))
                .ThenBy(static value => value.StatusKey, StringComparer.Ordinal)
                .ToArray();
            bool original = successes.Length > 0 &&
                            successes.All(static value =>
                                value.StatusKey != "DEFAULT" &&
                                value.Headers.Count == 0 &&
                                value.Contents.All(static content => content.Kind == OpenApiSemanticContentKind.Json) &&
                                (value.StatusKey != "2XX" || value.Contents.Count == 0)) &&
                            successes.Select(static value => value.ContractKey).Distinct(StringComparer.Ordinal).Count() == 1;
            if (original)
            {
                if (operation.ResponseSchema is not null)
                {
                    GeneratedTypeModel resolvedType = ResolveType(operation.ResponseSchema);
                    legacyResponseType = resolvedType.WithNullable(
                        resolvedType.Nullable || operation.ResponseSchema.Nullable);
                }

                return;
            }

            plan = BuildResponsePlan(operation, successes, methodName, usedMethodNames);
        }

        private GeneratedResponsePlanModel BuildResponsePlan(
            OpenApiSemanticOperation operation,
            IReadOnlyList<OpenApiSemanticResponse> successes,
            string methodName,
            HashSet<string> usedMethodNames)
        {
            // The wrapper takes the natural name before the inline body types are allocated.
            string? wrapperName = successes.Count > 0 &&
                                  successes.Select(static value => value.ContractKey).Distinct(StringComparer.Ordinal).Count() > 1
                ? AllocateQuietTypeName(ToIdentifier(operation.OperationId, pascalCase: true).TrimStart('@') + "Response", operation.Location)
                : null;
            var members = new List<GeneratedSuccessResponseModel>();
            var unionHeaders = new List<(string Key, GeneratedResponseHeaderModel Model, int Declared, int RequiredIn)>();
            var usedHeaderNames = new HashSet<string>(StringComparer.Ordinal) { "StatusCode", "Headers", "Data" };
            string pascalName = ToIdentifier(operation.OperationId, pascalCase: true).TrimStart('@');
            string? httpInfoTypeName = null;
            bool anyHeaders = successes.Any(static value => value.Headers.Count > 0);
            if (anyHeaders)
            {
                httpInfoTypeName = AllocateQuietTypeName(pascalName + "HttpInfo", operation.Location);
                usedHeaderNames.Add(httpInfoTypeName);
            }

            if (successes.Count == 0)
            {
                members.Add(new GeneratedSuccessResponseModel(
                    "ANY",
                    null,
                    Array.Empty<GeneratedResponseContentModel>(),
                    Array.Empty<GeneratedResponseHeaderModel>(),
                    ToAccessorName("ANY")));
            }

            foreach (OpenApiSemanticResponse response in successes)
            {
                GeneratedTypeModel? type = response.Contents.Count == 0
                    ? null
                    : ResolveResponseBodyType(response.Contents[0]);
                var headers = new List<GeneratedResponseHeaderModel>();
                foreach (OpenApiSemanticHeader header in response.Headers)
                {
                    GeneratedTypeModel resolvedType = ResolveType(header.Schema);
                    GeneratedTypeModel headerType = resolvedType.WithNullable(
                        resolvedType.Nullable || header.Schema.Nullable || !header.Required);
                    string key = header.Name.ToLowerInvariant();
                    int existing = unionHeaders.FindIndex(value => value.Key == key);
                    string propertyName;
                    if (existing < 0)
                    {
                        propertyName = AllocateUniqueName(
                            ToIdentifier(header.Name, pascalCase: true),
                            usedHeaderNames);
                        unionHeaders.Add((
                            key,
                            new GeneratedResponseHeaderModel(
                                header.Name, propertyName, header.Required, headerType, header.Description, header.Deprecated),
                            1,
                            header.Required ? 1 : 0));
                    }
                    else
                    {
                        var current = unionHeaders[existing];
                        propertyName = current.Model.PropertyName;
                        unionHeaders[existing] = (
                            current.Key,
                            current.Model,
                            current.Declared + 1,
                            current.RequiredIn + (header.Required ? 1 : 0));
                    }

                    headers.Add(new GeneratedResponseHeaderModel(
                        header.Name, propertyName, header.Required, headerType, header.Description, header.Deprecated));
                }

                members.Add(new GeneratedSuccessResponseModel(
                    response.StatusKey,
                    type,
                    response.Contents
                        .Select(static content => new GeneratedResponseContentModel(
                            ToResponseKind(content.Kind), content.MediaType))
                        .ToArray(),
                    headers,
                    ToAccessorName(response.StatusKey)));
            }

            // Responses with the same body contract share one type; different contracts get a result wrapper.
            GeneratedTypeModel? dataType = null;
            GeneratedResponseWrapperModel? wrapper = null;
            if (wrapperName is not null)
            {
                wrapper = new GeneratedResponseWrapperModel(
                    wrapperName,
                    members,
                    "Result of " + (string.IsNullOrWhiteSpace(operation.Summary) ? operation.OperationId : operation.Summary) +
                    ": the body of the declared success response that matched the status code.");
            }
            else
            {
                // The last success response with a body provides the type (as the original path does).
                dataType = members.LastOrDefault(static value => value.Type is not null)?.Type;
            }

            GeneratedHttpInfoModel? httpInfo = null;
            if (httpInfoTypeName is not null)
            {
                var union = unionHeaders
                    .Select(value => new GeneratedResponseHeaderModel(
                        value.Model.WireName,
                        value.Model.PropertyName,
                        value.RequiredIn == successes.Count,
                        value.RequiredIn == successes.Count
                            ? value.Model.Type.WithNullable(false)
                            : value.Model.Type.WithNullable(true),
                        value.Model.Description,
                        value.Model.Deprecated))
                    .ToArray();
                httpInfo = new GeneratedHttpInfoModel(
                    httpInfoTypeName,
                    union,
                    "Result of " + (string.IsNullOrWhiteSpace(operation.Summary) ? operation.OperationId : operation.Summary) +
                    ": the status code, the response headers and the data.");
            }

            var accept = new List<string>();
            foreach (GeneratedSuccessResponseModel member in members)
            {
                foreach (GeneratedResponseContentModel content in member.Contents)
                {
                    if (!accept.Contains(content.MediaType, StringComparer.OrdinalIgnoreCase))
                    {
                        accept.Add(content.MediaType);
                    }
                }
            }

            return new GeneratedResponsePlanModel(members, dataType, wrapper, httpInfo, accept);
        }

        private IReadOnlyList<GeneratedErrorResponseModel> BuildErrorResponses(OpenApiSemanticOperation operation)
        {
            var result = new List<GeneratedErrorResponseModel>();
            foreach (OpenApiSemanticResponse response in operation.Responses
                         .Where(static value => value.ResponseClass == OpenApiSemanticResponseClass.Error)
                         .OrderBy(static value => ResponseOrder(value.StatusKey))
                         .ThenBy(static value => value.StatusKey, StringComparer.Ordinal))
            {
                OpenApiSemanticMediaContent? json = response.Contents
                    .FirstOrDefault(static value => value.Kind == OpenApiSemanticContentKind.Json);
                if (json is null)
                {
                    // A declared status without a JSON body is kept untyped so that it does not fall back to default.
                    result.Add(new GeneratedErrorResponseModel(response.StatusKey, null));
                    continue;
                }

                GeneratedTypeModel resolved = ResolveType(json.Schema);
                result.Add(new GeneratedErrorResponseModel(
                    response.StatusKey,
                    resolved.WithNullable(resolved.Nullable || json.Schema.Nullable)));
            }

            return result;
        }

        /// <summary>Allocates a generated type name without a collision diagnostic (the generated type yields).</summary>
        private string AllocateQuietTypeName(string requestedName, OpenApiSourceLocation location)
        {
            string name = AllocateUniqueName(requestedName, _usedTypeNames);
            _typeNameAllocations.Add(name, new TypeNameAllocation(location, emitsDeclaration: false));
            return name;
        }
    }
}
