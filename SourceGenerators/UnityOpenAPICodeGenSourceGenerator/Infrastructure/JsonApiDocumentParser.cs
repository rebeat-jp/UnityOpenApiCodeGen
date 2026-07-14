using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// OpenAPI (JSON/YAML) をパースしてドメインモデルに変換する。
    /// Parses OpenAPI (JSON/YAML) into domain models.
    /// </summary>
    internal sealed class JsonApiDocumentParser
    {
        /// <summary>
        /// OpenAPI JSON を解析する。
        /// Parses OpenAPI JSON.
        /// </summary>
        public static OpenApiDocument Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("OpenAPI json is empty.", nameof(json));
            }

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("OpenAPI document root must be a JSON object.");
            }

            var root = document.RootElement;
            var info = ParseInfo(GetProperty(root, "info"));
            var paths = ParsePaths(GetProperty(root, "paths"));
            var components = ParseComponents(GetProperty(root, "components"));

            return new OpenApiDocument(
                GetString(root, "openapi"),
                info,
                paths,
                components);
        }

        /// <summary>
        /// Info オブジェクトを解析する。
        /// Parses the Info object.
        /// </summary>
        private static OpenApiInfo ParseInfo(JsonElement? infoElement)
        {
            if (infoElement is null || infoElement.Value.ValueKind != JsonValueKind.Object)
            {
                return new OpenApiInfo(null, null);
            }

            var info = infoElement.Value;
            return new OpenApiInfo(GetString(info, "title"), GetString(info, "version"));
        }

        /// <summary>
        /// Paths オブジェクトを解析する。
        /// Parses the Paths object.
        /// </summary>
        private static IReadOnlyDictionary<string, OpenApiPathItem> ParsePaths(JsonElement? pathsElement)
        {
            var paths = new Dictionary<string, OpenApiPathItem>(StringComparer.Ordinal);
            if (pathsElement is null || pathsElement.Value.ValueKind != JsonValueKind.Object)
            {
                return paths;
            }

            // path -> operations へ展開する。
            foreach (var pathProperty in pathsElement.Value.EnumerateObject())
            {
                var operations = new Dictionary<string, OpenApiOperation>(StringComparer.OrdinalIgnoreCase);
                if (pathProperty.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var operationProperty in pathProperty.Value.EnumerateObject())
                    {
                        if (operationProperty.Value.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        operations[operationProperty.Name] = ParseOperation(operationProperty.Value);
                    }
                }

                paths[pathProperty.Name] = new OpenApiPathItem(operations);
            }

            return paths;
        }

        /// <summary>
        /// Operation オブジェクトを解析する。
        /// Parses the Operation object.
        /// </summary>
        private static OpenApiOperation ParseOperation(JsonElement operationElement)
        {
            var parameters = new List<OpenApiParameter>();
            var parametersElement = GetProperty(operationElement, "parameters");
            if (parametersElement is not null && parametersElement.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var parameterElement in parametersElement.Value.EnumerateArray())
                {
                    if (parameterElement.ValueKind == JsonValueKind.Object)
                    {
                        parameters.Add(ParseParameter(parameterElement));
                    }
                }
            }

            var requestBody = ParseRequestBody(GetProperty(operationElement, "requestBody"));
            var responses = ParseResponses(GetProperty(operationElement, "responses"));

            return new OpenApiOperation(
                GetString(operationElement, "operationId"),
                GetString(operationElement, "summary"),
                parameters,
                requestBody,
                responses);
        }

        /// <summary>
        /// Parameter オブジェクトを解析する。
        /// Parses the Parameter object.
        /// </summary>
        private static OpenApiParameter ParseParameter(JsonElement parameterElement)
        {
            var schema = ParseSchema(GetProperty(parameterElement, "schema"));
            return new OpenApiParameter(
                GetString(parameterElement, "name"),
                GetString(parameterElement, "in"),
                GetBool(parameterElement, "required"),
                schema);
        }

        /// <summary>
        /// RequestBody オブジェクトを解析する。
        /// Parses the RequestBody object.
        /// </summary>
        private static OpenApiRequestBody? ParseRequestBody(JsonElement? requestBodyElement)
        {
            if (requestBodyElement is null || requestBodyElement.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var requestBody = requestBodyElement.Value;
            return new OpenApiRequestBody(
                GetBool(requestBody, "required"),
                ParseContent(GetProperty(requestBody, "content")));
        }

        /// <summary>
        /// Responses オブジェクトを解析する。
        /// Parses the Responses object.
        /// </summary>
        private static IReadOnlyDictionary<string, OpenApiResponse> ParseResponses(JsonElement? responsesElement)
        {
            var responses = new Dictionary<string, OpenApiResponse>(StringComparer.Ordinal);
            if (responsesElement is null || responsesElement.Value.ValueKind != JsonValueKind.Object)
            {
                return responses;
            }

            // status code -> response を展開する。
            foreach (var responseProperty in responsesElement.Value.EnumerateObject())
            {
                if (responseProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var responseValue = responseProperty.Value;
                responses[responseProperty.Name] = new OpenApiResponse(
                    GetString(responseValue, "description"),
                    ParseContent(GetProperty(responseValue, "content")));
            }

            return responses;
        }

        /// <summary>
        /// Content オブジェクトを解析する。
        /// Parses the Content object.
        /// </summary>
        private static IReadOnlyDictionary<string, OpenApiMediaType> ParseContent(JsonElement? contentElement)
        {
            var content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal);
            if (contentElement is null || contentElement.Value.ValueKind != JsonValueKind.Object)
            {
                return content;
            }

            // mediaType -> schema へ変換する。
            foreach (var contentProperty in contentElement.Value.EnumerateObject())
            {
                if (contentProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var schema = ParseSchema(GetProperty(contentProperty.Value, "schema"));
                content[contentProperty.Name] = new OpenApiMediaType(schema);
            }

            return content;
        }

        /// <summary>
        /// Components オブジェクトを解析する。
        /// Parses the Components object.
        /// </summary>
        private static OpenApiComponents ParseComponents(JsonElement? componentsElement)
        {
            if (componentsElement is null || componentsElement.Value.ValueKind != JsonValueKind.Object)
            {
                return new OpenApiComponents(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal));
            }

            var schemasElement = GetProperty(componentsElement.Value, "schemas");
            var schemas = ParseSchemas(schemasElement);
            return new OpenApiComponents(schemas);
        }

        /// <summary>
        /// Schemas オブジェクトを解析する。
        /// Parses the Schemas object.
        /// </summary>
        private static IReadOnlyDictionary<string, OpenApiSchema> ParseSchemas(JsonElement? schemasElement)
        {
            var schemas = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
            if (schemasElement is null || schemasElement.Value.ValueKind != JsonValueKind.Object)
            {
                return schemas;
            }

            foreach (var schemaProperty in schemasElement.Value.EnumerateObject())
            {
                if (schemaProperty.Value.ValueKind != JsonValueKind.Object &&
                    schemaProperty.Value.ValueKind != JsonValueKind.True &&
                    schemaProperty.Value.ValueKind != JsonValueKind.False)
                {
                    continue;
                }

                schemas[schemaProperty.Name] = ParseSchema(schemaProperty.Value);
            }

            return schemas;
        }

        /// <summary>
        /// JSON Schema を解析する。
        /// Parses JSON Schema.
        /// </summary>
        private static OpenApiSchema ParseSchema(JsonElement? schemaElement)
        {
            if (schemaElement is null)
            {
                return new OpenApiSchema();
            }

            var schemaValue = schemaElement.Value;
            if (schemaValue.ValueKind == JsonValueKind.True || schemaValue.ValueKind == JsonValueKind.False)
            {
                return new OpenApiSchema(booleanSchema: schemaValue.ValueKind == JsonValueKind.True);
            }

            if (schemaValue.ValueKind != JsonValueKind.Object)
            {
                return new OpenApiSchema();
            }

            var properties = ParseSchemaMap(GetProperty(schemaValue, "properties"));
            var patternProperties = ParseSchemaMap(GetProperty(schemaValue, "patternProperties"));
            var required = ParseStringList(GetProperty(schemaValue, "required"));

            var types = ParseStringListOrSingle(GetProperty(schemaValue, "type"));
            var type = types.Count > 0 ? types[0] : null;

            var prefixItems = ParseSchemaList(GetProperty(schemaValue, "prefixItems"));
            OpenApiSchema? items = null;
            bool? itemsAllowed = null;
            var itemsElement = GetProperty(schemaValue, "items");
            if (itemsElement is not null && itemsElement.Value.ValueKind == JsonValueKind.Array && prefixItems.Count == 0)
            {
                prefixItems = ParseSchemaList(itemsElement);
            }
            else
            {
                ParseSchemaOrBoolean(itemsElement, out items, out itemsAllowed);
            }

            ParseSchemaOrBoolean(GetProperty(schemaValue, "additionalProperties"), out var additionalPropertiesSchema, out var additionalPropertiesAllowed);
            ParseSchemaOrBoolean(GetProperty(schemaValue, "unevaluatedItems"), out var unevaluatedItems, out var unevaluatedItemsAllowed);
            ParseSchemaOrBoolean(GetProperty(schemaValue, "unevaluatedProperties"), out var unevaluatedProperties, out var unevaluatedPropertiesAllowed);

            var schemaUri = GetString(schemaValue, "$schema");
            var id = GetString(schemaValue, "$id");
            var anchor = GetString(schemaValue, "$anchor");
            var dynamicRef = GetString(schemaValue, "$dynamicRef");
            var dynamicAnchor = GetString(schemaValue, "$dynamicAnchor");
            var comment = GetString(schemaValue, "$comment");

            var enumValues = ParseAnyList(GetProperty(schemaValue, "enum"));
            var constValue = ParseAny(GetProperty(schemaValue, "const"));
            var defaultValue = ParseAny(GetProperty(schemaValue, "default"));
            var examples = ParseAnyList(GetProperty(schemaValue, "examples"));

            var allOf = ParseSchemaList(GetProperty(schemaValue, "allOf"));
            var anyOf = ParseSchemaList(GetProperty(schemaValue, "anyOf"));
            var oneOf = ParseSchemaList(GetProperty(schemaValue, "oneOf"));
            var not = ParseSchemaOrNull(GetProperty(schemaValue, "not"));

            var dependentSchemas = ParseSchemaMap(GetProperty(schemaValue, "dependentSchemas"));
            var dependentRequired = ParseDependentRequired(GetProperty(schemaValue, "dependentRequired"));

            var ifSchema = ParseSchemaOrNull(GetProperty(schemaValue, "if"));
            var thenSchema = ParseSchemaOrNull(GetProperty(schemaValue, "then"));
            var elseSchema = ParseSchemaOrNull(GetProperty(schemaValue, "else"));

            var defs = ParseSchemaMap(GetProperty(schemaValue, "$defs"));
            var vocabulary = ParseVocabulary(GetProperty(schemaValue, "$vocabulary"));

            var contentSchema = ParseSchemaOrNull(GetProperty(schemaValue, "contentSchema"));

            return new OpenApiSchema(
                booleanSchema: null,
                @ref: GetString(schemaValue, "$ref"),
                schema: schemaUri,
                id: id,
                anchor: anchor,
                dynamicRef: dynamicRef,
                dynamicAnchor: dynamicAnchor,
                title: GetString(schemaValue, "title"),
                description: GetString(schemaValue, "description"),
                type: type,
                types: types,
                format: GetString(schemaValue, "format"),
                @const: constValue,
                enumValues: enumValues,
                @default: defaultValue,
                examples: examples,
                multipleOf: GetDouble(schemaValue, "multipleOf"),
                maximum: GetDouble(schemaValue, "maximum"),
                exclusiveMaximum: GetDouble(schemaValue, "exclusiveMaximum"),
                minimum: GetDouble(schemaValue, "minimum"),
                exclusiveMinimum: GetDouble(schemaValue, "exclusiveMinimum"),
                maxLength: GetLong(schemaValue, "maxLength"),
                minLength: GetLong(schemaValue, "minLength"),
                pattern: GetString(schemaValue, "pattern"),
                maxItems: GetLong(schemaValue, "maxItems"),
                minItems: GetLong(schemaValue, "minItems"),
                uniqueItems: GetNullableBool(schemaValue, "uniqueItems"),
                maxContains: GetLong(schemaValue, "maxContains"),
                minContains: GetLong(schemaValue, "minContains"),
                maxProperties: GetLong(schemaValue, "maxProperties"),
                minProperties: GetLong(schemaValue, "minProperties"),
                properties: properties,
                required: required,
                patternProperties: patternProperties,
                additionalPropertiesSchema: additionalPropertiesSchema,
                additionalPropertiesAllowed: additionalPropertiesAllowed,
                items: items,
                itemsAllowed: itemsAllowed,
                prefixItems: prefixItems,
                contains: ParseSchemaOrNull(GetProperty(schemaValue, "contains")),
                propertyNames: ParseSchemaOrNull(GetProperty(schemaValue, "propertyNames")),
                unevaluatedItems: unevaluatedItems,
                unevaluatedItemsAllowed: unevaluatedItemsAllowed,
                unevaluatedProperties: unevaluatedProperties,
                unevaluatedPropertiesAllowed: unevaluatedPropertiesAllowed,
                dependentSchemas: dependentSchemas,
                dependentRequired: dependentRequired,
                @if: ifSchema,
                then: thenSchema,
                @else: elseSchema,
                allOf: allOf,
                anyOf: anyOf,
                oneOf: oneOf,
                not: not,
                defs: defs,
                vocabulary: vocabulary,
                comment: comment,
                readOnly: GetNullableBool(schemaValue, "readOnly"),
                writeOnly: GetNullableBool(schemaValue, "writeOnly"),
                deprecated: GetNullableBool(schemaValue, "deprecated"),
                contentEncoding: GetString(schemaValue, "contentEncoding"),
                contentMediaType: GetString(schemaValue, "contentMediaType"),
                contentSchema: contentSchema);
        }

        /// <summary>
        /// Schema or null を安全に解析する。
        /// Safely parses a schema or null.
        /// </summary>
        private static OpenApiSchema? ParseSchemaOrNull(JsonElement? schemaElement)
        {
            if (schemaElement is null)
            {
                return null;
            }

            var value = schemaElement.Value;
            if (value.ValueKind == JsonValueKind.Object ||
                value.ValueKind == JsonValueKind.True ||
                value.ValueKind == JsonValueKind.False)
            {
                return ParseSchema(schemaElement);
            }

            return null;
        }

        /// <summary>
        /// Schema マップを解析する。
        /// Parses a schema map.
        /// </summary>
        private static IReadOnlyDictionary<string, OpenApiSchema> ParseSchemaMap(JsonElement? element)
        {
            var schemas = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
            if (element is null || element.Value.ValueKind != JsonValueKind.Object)
            {
                return schemas;
            }

            foreach (var property in element.Value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object ||
                    property.Value.ValueKind == JsonValueKind.True ||
                    property.Value.ValueKind == JsonValueKind.False)
                {
                    schemas[property.Name] = ParseSchema(property.Value);
                }
            }

            return schemas;
        }

        /// <summary>
        /// Schema 配列を解析する。
        /// Parses a schema array.
        /// </summary>
        private static IReadOnlyList<OpenApiSchema> ParseSchemaList(JsonElement? element)
        {
            var schemas = new List<OpenApiSchema>();
            if (element is null || element.Value.ValueKind != JsonValueKind.Array)
            {
                return schemas;
            }

            foreach (var item in element.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object ||
                    item.ValueKind == JsonValueKind.True ||
                    item.ValueKind == JsonValueKind.False)
                {
                    schemas.Add(ParseSchema(item));
                }
            }

            return schemas;
        }

        /// <summary>
        /// 文字列配列を解析する。
        /// Parses a string array.
        /// </summary>
        private static IReadOnlyList<string> ParseStringList(JsonElement? element)
        {
            var values = new List<string>();
            if (element is null || element.Value.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in element.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    values.Add(item.GetString() ?? string.Empty);
                }
            }

            return values;
        }

        /// <summary>
        /// 文字列配列または単一文字列を解析する。
        /// Parses a string array or a single string.
        /// </summary>
        private static IReadOnlyList<string> ParseStringListOrSingle(JsonElement? element)
        {
            var values = new List<string>();
            if (element is null)
            {
                return values;
            }

            if (element.Value.ValueKind == JsonValueKind.String)
            {
                values.Add(element.Value.GetString() ?? string.Empty);
                return values;
            }

            if (element.Value.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in element.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    values.Add(item.GetString() ?? string.Empty);
                }
            }

            return values;
        }

        /// <summary>
        /// Schema または boolean を解析する。
        /// Parses a schema or boolean flag.
        /// </summary>
        private static void ParseSchemaOrBoolean(JsonElement? element, out OpenApiSchema? schema, out bool? allowed)
        {
            schema = null;
            allowed = null;
            if (element is null)
            {
                return;
            }

            var value = element.Value;
            if (value.ValueKind == JsonValueKind.True)
            {
                allowed = true;
                return;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                allowed = false;
                return;
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                schema = ParseSchema(element);
            }
        }

        /// <summary>
        /// 任意値の配列を解析する。
        /// Parses an array of arbitrary values.
        /// </summary>
        private static IReadOnlyList<OpenApiAny> ParseAnyList(JsonElement? element)
        {
            var values = new List<OpenApiAny>();
            if (element is null || element.Value.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in element.Value.EnumerateArray())
            {
                values.Add(new OpenApiAny(item.GetRawText()));
            }

            return values;
        }

        /// <summary>
        /// 任意値を解析する。
        /// Parses an arbitrary value.
        /// </summary>
        private static OpenApiAny? ParseAny(JsonElement? element)
        {
            return element is null ? null : new OpenApiAny(element.Value.GetRawText());
        }

        /// <summary>
        /// dependentRequired を解析する。
        /// Parses dependentRequired.
        /// </summary>
        private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseDependentRequired(JsonElement? element)
        {
            var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (element is null || element.Value.ValueKind != JsonValueKind.Object)
            {
                return values;
            }

            foreach (var property in element.Value.EnumerateObject())
            {
                values[property.Name] = ParseStringList(property.Value);
            }

            return values;
        }

        /// <summary>
        /// $vocabulary を解析する。
        /// Parses $vocabulary.
        /// </summary>
        private static IReadOnlyDictionary<string, bool> ParseVocabulary(JsonElement? element)
        {
            var values = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (element is null || element.Value.ValueKind != JsonValueKind.Object)
            {
                return values;
            }

            foreach (var property in element.Value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.True)
                {
                    values[property.Name] = true;
                }
                else if (property.Value.ValueKind == JsonValueKind.False)
                {
                    values[property.Name] = false;
                }
            }

            return values;
        }

        /// <summary>
        /// 数値 (double) を取得する。
        /// Gets a numeric value as double.
        /// </summary>
        private static double? GetDouble(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.Number &&
                   value.TryGetDouble(out var number)
                ? number
                : null;
        }

        /// <summary>
        /// 数値 (long) を取得する。
        /// Gets a numeric value as long.
        /// </summary>
        private static long? GetLong(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.Number &&
                   value.TryGetInt64(out var number)
                ? number
                : null;
        }

        /// <summary>
        /// 真偽値を取得する。存在しない場合は null。
        /// Gets a nullable boolean value.
        /// </summary>
        private static bool? GetNullableBool(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.True
                ? true
                : value.ValueKind == JsonValueKind.False
                    ? false
                    : null;
        }

        /// <summary>
        /// 指定プロパティを安全に取得する。
        /// Safely gets a named property.
        /// </summary>
        private static JsonElement? GetProperty(JsonElement element, string name)
        {
            return element.ValueKind != JsonValueKind.Object ? null : element.TryGetProperty(name, out var value) ? value : null;
        }

        /// <summary>
        /// 文字列プロパティを取得する。
        /// Gets a string property.
        /// </summary>
        private static string? GetString(JsonElement element, string name)
        {
            return !element.TryGetProperty(name, out var value) ? null : value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        /// <summary>
        /// bool プロパティを取得する。
        /// Gets a boolean property.
        /// </summary>
        private static bool GetBool(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        }
    }
}
