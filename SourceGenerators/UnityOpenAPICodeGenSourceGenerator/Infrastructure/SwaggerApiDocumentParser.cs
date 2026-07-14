using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Swagger 2.0 JSON をパースしてドメインモデルに変換する。
    /// Parses Swagger 2.0 JSON into domain models.
    /// </summary>
    internal sealed class SwaggerApiDocumentParser
    {
        /// <summary>
        /// Swagger 2.0 JSON を解析する。
        /// Parses Swagger 2.0 JSON.
        /// </summary>
        public SwaggerDocument Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Swagger json is empty.", nameof(json));
            }

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Swagger document root must be a JSON object.");
            }

            var root = document.RootElement;
            var info = ParseInfo(GetProperty(root, "info"));
            var paths = ParsePaths(GetProperty(root, "paths"));
            var definitions = ParseDefinitions(GetProperty(root, "definitions"));

            // トップレベルの Swagger ドキュメントを構築する。
            return new SwaggerDocument(
                GetString(root, "swagger"),
                info,
                GetString(root, "host"),
                GetString(root, "basePath"),
                ParseStringList(GetProperty(root, "schemes")),
                ParseStringList(GetProperty(root, "consumes")),
                ParseStringList(GetProperty(root, "produces")),
                paths,
                definitions);
        }

        private static SwaggerInfo ParseInfo(JsonElement? infoElement)
        {
            if (infoElement is null || infoElement.Value.ValueKind != JsonValueKind.Object)
            {
                return new SwaggerInfo(null, null, null);
            }

            var info = infoElement.Value;
            return new SwaggerInfo(GetString(info, "title"), GetString(info, "version"), GetString(info, "description"));
        }

        private static IReadOnlyDictionary<string, SwaggerPathItem> ParsePaths(JsonElement? pathsElement)
        {
            var paths = new Dictionary<string, SwaggerPathItem>(StringComparer.Ordinal);
            if (pathsElement is null || pathsElement.Value.ValueKind != JsonValueKind.Object)
            {
                return paths;
            }

            // Path item を operations に展開する。
            foreach (var pathProperty in pathsElement.Value.EnumerateObject())
            {
                var operations = new Dictionary<string, SwaggerOperation>(StringComparer.OrdinalIgnoreCase);
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

                paths[pathProperty.Name] = new SwaggerPathItem(operations);
            }

            return paths;
        }

        private static SwaggerOperation ParseOperation(JsonElement operationElement)
        {
            var parameters = new List<SwaggerParameter>();
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

            var responses = ParseResponses(GetProperty(operationElement, "responses"));

            return new SwaggerOperation(
                GetString(operationElement, "operationId"),
                GetString(operationElement, "summary"),
                GetString(operationElement, "description"),
                ParseStringList(GetProperty(operationElement, "tags")),
                ParseStringList(GetProperty(operationElement, "consumes")),
                ParseStringList(GetProperty(operationElement, "produces")),
                parameters,
                responses);
        }

        private static SwaggerParameter ParseParameter(JsonElement parameterElement)
        {
            var schema = ParseSchemaOrNull(GetProperty(parameterElement, "schema"));
            var items = ParseSchemaOrNull(GetProperty(parameterElement, "items"));
            return new SwaggerParameter(
                GetString(parameterElement, "name"),
                GetString(parameterElement, "in"),
                GetString(parameterElement, "description"),
                GetBool(parameterElement, "required"),
                GetString(parameterElement, "type"),
                GetString(parameterElement, "format"),
                schema,
                items,
                ParseAnyList(GetProperty(parameterElement, "enum")),
                ParseAny(GetProperty(parameterElement, "default")),
                GetString(parameterElement, "collectionFormat"));
        }

        private static IReadOnlyDictionary<string, SwaggerResponse> ParseResponses(JsonElement? responsesElement)
        {
            var responses = new Dictionary<string, SwaggerResponse>(StringComparer.Ordinal);
            if (responsesElement is null || responsesElement.Value.ValueKind != JsonValueKind.Object)
            {
                return responses;
            }

            // ステータスコード -> レスポンスのマップを展開する。
            foreach (var responseProperty in responsesElement.Value.EnumerateObject())
            {
                if (responseProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var responseValue = responseProperty.Value;
                responses[responseProperty.Name] = new SwaggerResponse(
                    GetString(responseValue, "description"),
                    ParseSchemaOrNull(GetProperty(responseValue, "schema")),
                    ParseHeaders(GetProperty(responseValue, "headers")));
            }

            return responses;
        }

        private static IReadOnlyDictionary<string, SwaggerHeader> ParseHeaders(JsonElement? headersElement)
        {
            var headers = new Dictionary<string, SwaggerHeader>(StringComparer.Ordinal);
            if (headersElement is null || headersElement.Value.ValueKind != JsonValueKind.Object)
            {
                return headers;
            }

            foreach (var headerProperty in headersElement.Value.EnumerateObject())
            {
                if (headerProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var headerValue = headerProperty.Value;
                headers[headerProperty.Name] = new SwaggerHeader(
                    GetString(headerValue, "description"),
                    GetString(headerValue, "type"),
                    GetString(headerValue, "format"),
                    ParseSchemaOrNull(GetProperty(headerValue, "items")),
                    ParseAny(GetProperty(headerValue, "default")));
            }

            return headers;
        }

        private static IReadOnlyDictionary<string, SwaggerSchema> ParseDefinitions(JsonElement? definitionsElement)
        {
            var definitions = new Dictionary<string, SwaggerSchema>(StringComparer.Ordinal);
            if (definitionsElement is null || definitionsElement.Value.ValueKind != JsonValueKind.Object)
            {
                return definitions;
            }

            foreach (var definitionProperty in definitionsElement.Value.EnumerateObject())
            {
                if (definitionProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                definitions[definitionProperty.Name] = ParseSchema(definitionProperty.Value);
            }

            return definitions;
        }

        private static SwaggerSchema? ParseSchemaOrNull(JsonElement? schemaElement)
        {
            if (schemaElement is null || schemaElement.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return ParseSchema(schemaElement.Value);
        }

        private static SwaggerSchema ParseSchema(JsonElement schemaValue)
        {
            var properties = ParseSchemaMap(GetProperty(schemaValue, "properties"));
            var patternProperties = ParseSchemaMap(GetProperty(schemaValue, "patternProperties"));
            var required = ParseStringList(GetProperty(schemaValue, "required"));

            var types = ParseStringListOrSingle(GetProperty(schemaValue, "type"));
            var type = types.Count > 0 ? types[0] : null;

            ParseItems(GetProperty(schemaValue, "items"), out var items, out var itemSchemas);
            ParseSchemaOrBoolean(GetProperty(schemaValue, "additionalItems"), out var additionalItemsSchema, out var additionalItemsAllowed);
            ParseSchemaOrBoolean(GetProperty(schemaValue, "additionalProperties"), out var additionalPropertiesSchema, out var additionalPropertiesAllowed);

            var enumValues = ParseAnyList(GetProperty(schemaValue, "enum"));
            var allOf = ParseSchemaList(GetProperty(schemaValue, "allOf"));
            var anyOf = ParseSchemaList(GetProperty(schemaValue, "anyOf"));
            var oneOf = ParseSchemaList(GetProperty(schemaValue, "oneOf"));
            var not = ParseSchemaOrNull(GetProperty(schemaValue, "not"));
            var definitions = ParseSchemaMap(GetProperty(schemaValue, "definitions"));

            ParseDependencies(GetProperty(schemaValue, "dependencies"), out var dependenciesSchemas, out var dependenciesRequired);

            return new SwaggerSchema(
                GetString(schemaValue, "$schema"),
                GetString(schemaValue, "id"),
                GetString(schemaValue, "$ref"),
                GetString(schemaValue, "title"),
                GetString(schemaValue, "description"),
                type,
                types,
                GetString(schemaValue, "format"),
                items,
                itemSchemas,
                additionalItemsSchema,
                additionalItemsAllowed,
                properties,
                required,
                patternProperties,
                enumValues,
                allOf,
                anyOf,
                oneOf,
                not,
                definitions,
                additionalPropertiesSchema,
                additionalPropertiesAllowed,
                dependenciesSchemas,
                dependenciesRequired,
                GetDouble(schemaValue, "multipleOf"),
                GetDouble(schemaValue, "maximum"),
                GetDouble(schemaValue, "minimum"),
                GetNullableBool(schemaValue, "exclusiveMaximum"),
                GetNullableBool(schemaValue, "exclusiveMinimum"),
                GetLong(schemaValue, "maxLength"),
                GetLong(schemaValue, "minLength"),
                GetString(schemaValue, "pattern"),
                GetLong(schemaValue, "maxItems"),
                GetLong(schemaValue, "minItems"),
                GetNullableBool(schemaValue, "uniqueItems"),
                GetLong(schemaValue, "maxProperties"),
                GetLong(schemaValue, "minProperties"),
                GetNullableBool(schemaValue, "readOnly"),
                ParseAny(GetProperty(schemaValue, "default")),
                ParseAny(GetProperty(schemaValue, "example")));
        }

        private static IReadOnlyList<SwaggerSchema> ParseSchemaList(JsonElement? element)
        {
            var schemas = new List<SwaggerSchema>();
            if (element is null || element.Value.ValueKind != JsonValueKind.Array)
            {
                return schemas;
            }

            foreach (var item in element.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    schemas.Add(ParseSchema(item));
                }
            }

            return schemas;
        }

        private static IReadOnlyDictionary<string, SwaggerSchema> ParseSchemaMap(JsonElement? element)
        {
            var schemas = new Dictionary<string, SwaggerSchema>(StringComparer.Ordinal);
            if (element is null || element.Value.ValueKind != JsonValueKind.Object)
            {
                return schemas;
            }

            foreach (var property in element.Value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    schemas[property.Name] = ParseSchema(property.Value);
                }
            }

            return schemas;
        }

        private static void ParseItems(JsonElement? element, out SwaggerSchema? items, out IReadOnlyList<SwaggerSchema> itemSchemas)
        {
            items = null;
            var schemas = new List<SwaggerSchema>();
            if (element is null)
            {
                itemSchemas = schemas;
                return;
            }

            if (element.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        schemas.Add(ParseSchema(item));
                    }
                }

                itemSchemas = schemas;
                return;
            }

            if (element.Value.ValueKind == JsonValueKind.Object)
            {
                items = ParseSchema(element.Value);
            }

            itemSchemas = schemas;
        }

        private static void ParseSchemaOrBoolean(JsonElement? element, out SwaggerSchema? schema, out bool? allowed)
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
                schema = ParseSchema(value);
            }
        }

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

        private static OpenApiAny? ParseAny(JsonElement? element)
        {
            return element is null ? null : new OpenApiAny(element.Value.GetRawText());
        }

        private static void ParseDependencies(
            JsonElement? element,
            out IReadOnlyDictionary<string, SwaggerSchema> dependencySchemas,
            out IReadOnlyDictionary<string, IReadOnlyList<string>> dependencyRequired)
        {
            var schemas = new Dictionary<string, SwaggerSchema>(StringComparer.Ordinal);
            var required = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (element is null || element.Value.ValueKind != JsonValueKind.Object)
            {
                dependencySchemas = schemas;
                dependencyRequired = required;
                return;
            }

            foreach (var property in element.Value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    schemas[property.Name] = ParseSchema(property.Value);
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    required[property.Name] = ParseStringList(property.Value);
                }
            }

            dependencySchemas = schemas;
            dependencyRequired = required;
        }

        private static JsonElement? GetProperty(JsonElement element, string name)
        {
            return element.ValueKind != JsonValueKind.Object ? null : element.TryGetProperty(name, out var value) ? value : null;
        }

        private static string? GetString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static bool GetBool(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        }

        private static double? GetDouble(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.Number &&
                   value.TryGetDouble(out var number)
                ? number
                : null;
        }

        private static long? GetLong(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.Number &&
                   value.TryGetInt64(out var number)
                ? number
                : null;
        }

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
    }
}
