using System;
using System.Collections.Generic;

using F = Rhycol.OpenApiCodeGen.SourceGenerator.Swagger2NodeFactory;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed partial class Swagger2DocumentConverter
    {
        // The schema keywords that hold sub-schemas. They mirror the reference contexts of the normalized
        // bundle reader, so every $ref the Editor collected is one this converter rewrites.
        private static readonly HashSet<string> SchemaMapKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "properties", "patternProperties", "dependentSchemas", "$defs", "definitions"
            };

        private static readonly HashSet<string> SchemaArrayKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "prefixItems", "allOf", "anyOf", "oneOf"
            };

        private static readonly HashSet<string> SchemaKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "additionalProperties", "unevaluatedItems", "unevaluatedProperties", "items", "additionalItems",
                "contains", "propertyNames", "not", "if", "then", "else", "contentSchema"
            };

        // The Parameter / Items / Header Object fields that describe the value: they move into 'schema'.
        private static readonly HashSet<string> SimpleSchemaKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "type", "format", "items", "default", "maximum", "exclusiveMaximum", "minimum",
                "exclusiveMinimum", "maxLength", "minLength", "pattern", "maxItems", "minItems",
                "uniqueItems", "enum", "multipleOf", "x-nullable"
            };

        private enum ReferenceKind
        {
            Schema,
            Parameter,
            Response
        }

        // ---- schemas -----------------------------------------------------------------------------------

        /// <summary>
        /// Converts a Swagger 2.0 Schema Object: x-nullable becomes nullable, a string discriminator becomes
        /// { propertyName }, type: file becomes string / binary and $ref values are rewritten. allOf,
        /// readOnly, example and the other keywords (x-* extensions included) are kept as written.
        /// </summary>
        private SpecNode ConvertSchema(SpecNode node, string path)
        {
            if (node.ValueKind != SpecValueKind.Object)
            {
                return F.Clone(node, path);
            }

            bool hasNullable = node.TryGetProperty("nullable", out _);
            bool hasFormat = node.TryGetProperty("format", out _);
            SpecNode? fileType = null;
            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in node.EnumerateObject())
            {
                string childPath = F.Child(path, property.Name);
                SpecNode value = property.Value;
                if (property.Name == "$ref")
                {
                    RequireStringReference(value);
                    properties.Add(F.Property(property, ConvertReference(value, childPath, ReferenceKind.Schema)));
                }
                else if (property.Name == "x-nullable")
                {
                    if (value.ValueKind != SpecValueKind.True && value.ValueKind != SpecValueKind.False)
                    {
                        throw Invalid(value, "The x-nullable field must be a boolean.");
                    }

                    if (value.ValueKind == SpecValueKind.True && !hasNullable)
                    {
                        properties.Add(new SpecProperty(
                            "nullable",
                            property.Line,
                            property.Column,
                            F.Boolean(value, F.Child(path, "nullable"), true)));
                    }
                }
                else if (property.Name == "discriminator" && value.ValueKind == SpecValueKind.String)
                {
                    properties.Add(F.Property(
                        property,
                        F.Object(
                            value,
                            childPath,
                            new[]
                            {
                                F.Property(
                                    "propertyName",
                                    F.String(value, F.Child(childPath, "propertyName"), value.GetString() ?? string.Empty),
                                    value)
                            })));
                }
                else if (property.Name == "type" && value.ValueKind == SpecValueKind.String &&
                         string.Equals(value.GetString(), "file", StringComparison.Ordinal))
                {
                    fileType = value;
                    properties.Add(F.Property(property, F.String(value, childPath, "string")));
                }
                else if (SchemaMapKeywords.Contains(property.Name) && value.ValueKind == SpecValueKind.Object)
                {
                    properties.Add(F.Property(property, ConvertSchemaMap(value, childPath)));
                }
                else if (SchemaArrayKeywords.Contains(property.Name) && value.ValueKind == SpecValueKind.Array)
                {
                    var items = new List<SpecNode>();
                    int index = 0;
                    foreach (SpecNode item in value.EnumerateArray())
                    {
                        items.Add(ConvertSchema(item, F.Index(childPath, index++)));
                    }

                    properties.Add(F.Property(property, F.Array(value, childPath, items)));
                }
                else if (SchemaKeywords.Contains(property.Name) && value.ValueKind == SpecValueKind.Object)
                {
                    properties.Add(F.Property(property, ConvertSchema(value, childPath)));
                }
                else
                {
                    properties.Add(F.Property(property, F.Clone(value, childPath)));
                }
            }

            if (fileType is not null && !hasFormat)
            {
                properties.Add(F.Property(
                    "format",
                    F.String(fileType, F.Child(path, "format"), "binary"),
                    fileType));
            }

            return F.Object(node, path, properties);
        }

        private SpecNode ConvertSchemaMap(SpecNode mapNode, string path)
        {
            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in mapNode.EnumerateObject())
            {
                properties.Add(F.Property(
                    property,
                    ConvertSchema(property.Value, F.Child(path, property.Name))));
            }

            return F.Object(mapNode, path, properties);
        }

        /// <summary>
        /// Builds the schema of a Parameter, Items or Header Object from its inline type fields (type,
        /// format, items, enum, default and the constraints); type: file becomes string / binary.
        /// </summary>
        private SpecNode BuildSimpleSchema(SpecNode source, string path, bool allowFile, bool copyDescription)
        {
            var properties = new List<SpecProperty>();
            bool fileType = false;
            bool hasFormat = source.TryGetProperty("format", out _);
            SpecNode? typeNode = null;
            foreach (SpecProperty property in source.EnumerateObject())
            {
                string childPath = F.Child(path, property.Name);
                if (property.Name == "description" && copyDescription)
                {
                    properties.Add(F.Property(property, F.Clone(property.Value, childPath)));
                    continue;
                }

                if (!SimpleSchemaKeywords.Contains(property.Name))
                {
                    continue;
                }

                SpecNode value = property.Value;
                if (property.Name == "type")
                {
                    typeNode = value;
                    if (value.ValueKind == SpecValueKind.String &&
                        string.Equals(value.GetString(), "file", StringComparison.Ordinal))
                    {
                        if (!allowFile)
                        {
                            throw Invalid(value, "The type 'file' is only valid for formData parameters and responses.");
                        }

                        fileType = true;
                        properties.Add(F.Property(property, F.String(value, childPath, "string")));
                        continue;
                    }
                }
                else if (property.Name == "items" && value.ValueKind == SpecValueKind.Object)
                {
                    RejectNonCsvItems(value);
                    properties.Add(F.Property(property, BuildSimpleSchema(value, childPath, false, false)));
                    continue;
                }
                else if (property.Name == "x-nullable")
                {
                    if (value.ValueKind == SpecValueKind.True && !source.TryGetProperty("nullable", out _))
                    {
                        properties.Add(new SpecProperty(
                            "nullable",
                            property.Line,
                            property.Column,
                            F.Boolean(value, F.Child(path, "nullable"), true)));
                    }

                    continue;
                }

                properties.Add(F.Property(property, F.Clone(value, childPath)));
            }

            if (fileType && !hasFormat && typeNode is not null)
            {
                properties.Add(F.Property("format", F.String(typeNode, F.Child(path, "format"), "binary"), typeNode));
            }

            return F.Object(source, path, properties);
        }

        private void RejectNonCsvItems(SpecNode items)
        {
            if (items.TryGetProperty("collectionFormat", out SpecNode format) &&
                !string.Equals(format.GetString(), "csv", StringComparison.Ordinal))
            {
                throw Unsupported(
                    format,
                    "A collectionFormat on the items of a nested array has no OpenAPI 3.0 equivalent.");
            }
        }

        // ---- references --------------------------------------------------------------------------------

        /// <summary>
        /// Rewrites a $ref (#/definitions/X to #/components/schemas/X, likewise for parameters and
        /// responses; an external file#/definitions/X keeps its file) and records the reference edge of the
        /// rewritten value when the document is part of a bundle.
        /// </summary>
        private SpecNode ConvertReference(SpecNode refNode, string path, ReferenceKind kind)
        {
            string value = refNode.GetString() ?? string.Empty;
            string oldPrefix;
            string newPrefix;
            string description;
            switch (kind)
            {
                case ReferenceKind.Parameter:
                    oldPrefix = "/parameters/";
                    newPrefix = "/components/parameters/";
                    description = "parameter";
                    break;
                case ReferenceKind.Response:
                    oldPrefix = "/responses/";
                    newPrefix = "/components/responses/";
                    description = "response";
                    break;
                default:
                    oldPrefix = "/definitions/";
                    newPrefix = "/components/schemas/";
                    description = "schema";
                    break;
            }

            int hash = value.IndexOf('#');
            string fragment = hash >= 0 ? value.Substring(hash + 1) : string.Empty;
            if (!fragment.StartsWith(oldPrefix, StringComparison.Ordinal))
            {
                throw Unsupported(
                    refNode,
                    "A Swagger 2.0 " + description + " $ref must target '#" + oldPrefix + "...': '" + value + "'.");
            }

            string rewritten = value.Substring(0, hash) + "#" + newPrefix + fragment.Substring(oldPrefix.Length);
            if (_context.Bundle is not null)
            {
                ResolvedSpecReference resolved = _context.Resolver.ResolveReference(_documentId, value, refNode);
                if (!_context.IsSwaggerDocument(resolved.DocumentId))
                {
                    throw Unsupported(
                        refNode,
                        "A Swagger 2.0 document can reference only other Swagger 2.0 documents; '" + value +
                        "' targets an OpenAPI 3.x document or a bare schema file.");
                }

                if (!resolved.Pointer.StartsWith(oldPrefix, StringComparison.Ordinal))
                {
                    throw Unsupported(
                        refNode,
                        "A Swagger 2.0 " + description + " $ref must target '#" + oldPrefix + "...': '" + value + "'.");
                }

                AddEdge(path, resolved.DocumentId, newPrefix + resolved.Pointer.Substring(oldPrefix.Length));
            }

            return F.String(refNode, path, rewritten);
        }

        // ---- components --------------------------------------------------------------------------------

        private SpecNode? ConvertComponents()
        {
            var properties = new List<SpecProperty>();
            const string components = "/components";
            if (_root.TryGetProperty("definitions", out SpecNode definitions))
            {
                RequireKind(definitions, SpecValueKind.Object, "The 'definitions' field must be an object.");
                properties.Add(F.Property(
                    "schemas",
                    ConvertSchemaMap(definitions, components + "/schemas"),
                    definitions));
            }

            if (_root.TryGetProperty("parameters", out SpecNode parameters))
            {
                RequireKind(parameters, SpecValueKind.Object, "The 'parameters' field must be an object.");
                var entries = new List<SpecProperty>();
                foreach (SpecProperty entry in parameters.EnumerateObject())
                {
                    RequireKind(entry.Value, SpecValueKind.Object, "A parameter definition must be an object.");
                    if (IsBodyOrFormData(entry.Value))
                    {
                        // A body or formData parameter becomes a requestBody where it is used (the media types
                        // depend on the operation), so it has no component of its own.
                        continue;
                    }

                    entries.Add(F.Property(
                        entry,
                        ConvertParameter(entry.Value, F.Child(components + "/parameters", entry.Name))));
                }

                if (entries.Count > 0)
                {
                    properties.Add(F.Property(
                        "parameters",
                        F.Object(parameters, components + "/parameters", entries),
                        parameters));
                }
            }

            if (_root.TryGetProperty("responses", out SpecNode responses))
            {
                RequireKind(responses, SpecValueKind.Object, "The 'responses' field must be an object.");
                var entries = new List<SpecProperty>();
                foreach (SpecProperty entry in responses.EnumerateObject())
                {
                    entries.Add(F.Property(
                        entry,
                        ConvertResponse(
                            entry.Value,
                            F.Child(components + "/responses", entry.Name),
                            _globalProduces)));
                }

                properties.Add(F.Property(
                    "responses",
                    F.Object(responses, components + "/responses", entries),
                    responses));
            }

            if (_isRoot && _root.TryGetProperty("securityDefinitions", out SpecNode security))
            {
                properties.Add(F.Property(
                    "securitySchemes",
                    ConvertSecurityDefinitions(security, components + "/securitySchemes"),
                    security));
            }

            return properties.Count == 0
                ? null
                : F.Object(_root, components, properties);
        }
    }
}
