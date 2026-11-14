using System;
using System.Collections.Generic;
using System.Linq;

using F = Rhycol.OpenApiCodeGen.SourceGenerator.Swagger2NodeFactory;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed partial class Swagger2DocumentConverter
    {
        private const string MultipartMediaType = "multipart/form-data";
        private const string UrlEncodedMediaType = "application/x-www-form-urlencoded";

        /// <summary>A parameter of a Path Item or Operation with its $ref (if any) followed.</summary>
        private sealed class ResolvedParameter
        {
            internal ResolvedParameter(SpecNode element, SpecNode node, SpecNode? referenceNode, string name, string location)
            {
                Element = element;
                Node = node;
                ReferenceNode = referenceNode;
                Name = name;
                Location = location;
            }

            /// <summary>The array item as written: a Parameter Object or a Reference Object.</summary>
            internal SpecNode Element { get; }

            /// <summary>The Parameter Object (the target when the item is a reference).</summary>
            internal SpecNode Node { get; }

            internal SpecNode? ReferenceNode { get; }

            internal string Name { get; }

            internal string Location { get; }

            internal bool IsBody => string.Equals(Location, "body", StringComparison.Ordinal);

            internal bool IsFormData => string.Equals(Location, "formData", StringComparison.Ordinal);
        }

        private static bool IsBodyOrFormData(SpecNode parameter)
        {
            string? location = parameter.TryGetProperty("in", out SpecNode value) ? value.GetString() : null;
            return string.Equals(location, "body", StringComparison.Ordinal) ||
                   string.Equals(location, "formData", StringComparison.Ordinal);
        }

        private IReadOnlyList<ResolvedParameter> ResolveParameters(SpecNode? parametersNode)
        {
            var result = new List<ResolvedParameter>();
            if (parametersNode is null)
            {
                return result;
            }

            RequireKind(parametersNode, SpecValueKind.Array, "The 'parameters' field must be an array.");
            foreach (SpecNode element in parametersNode.EnumerateArray())
            {
                RequireKind(element, SpecValueKind.Object, "A parameter must be an object.");
                SpecNode node = element;
                SpecNode? referenceNode = null;
                if (element.TryGetProperty("$ref", out SpecNode reference))
                {
                    RequireStringReference(reference);
                    referenceNode = reference;
                    ResolvedSpecReference resolved = _context.Resolver.ResolveReference(
                        _documentId,
                        reference.GetString() ?? string.Empty,
                        reference);
                    if (!resolved.Pointer.StartsWith("/parameters/", StringComparison.Ordinal) ||
                        resolved.Pointer.IndexOf('/', "/parameters/".Length) >= 0)
                    {
                        throw Unsupported(
                            reference,
                            "A Swagger 2.0 parameter $ref must target '#/parameters/<name>': '" +
                            reference.GetString() + "'.");
                    }

                    if (_context.Bundle is not null && !_context.IsSwaggerDocument(resolved.DocumentId))
                    {
                        throw Unsupported(
                            reference,
                            "A Swagger 2.0 document can reference only other Swagger 2.0 documents; '" +
                            reference.GetString() + "' targets an OpenAPI 3.x document or a bare schema file.");
                    }

                    node = resolved.Node;
                    RequireKind(node, SpecValueKind.Object, "A parameter definition must be an object.");
                    if (node.TryGetProperty("$ref", out _))
                    {
                        // Also stops reference cycles (#/parameters/A -> #/parameters/B -> #/parameters/A).
                        throw Unsupported(
                            reference,
                            "A parameter $ref must target a Parameter Object, not another reference: '" +
                            reference.GetString() + "'.");
                    }

                    if (!string.Equals(resolved.DocumentId, _documentId, StringComparison.Ordinal) &&
                        IsBodyOrFormData(node))
                    {
                        throw Unsupported(
                            reference,
                            "A body or formData parameter cannot be referenced from another document; " +
                            "it becomes a requestBody of the referencing operation.");
                    }
                }

                string name = RequireString(RequireProperty(node, "name"), "The parameter name must be a string.");
                string location = RequireString(RequireProperty(node, "in"), "The parameter 'in' must be a string.");
                result.Add(new ResolvedParameter(element, node, referenceNode, name, location));
            }

            return result;
        }

        /// <summary>
        /// The query, header and path parameters of one parameters array (body and formData parameters become
        /// the operation's requestBody). Returns null when none remains.
        /// </summary>
        private SpecNode? ConvertRegularParameters(
            IReadOnlyList<ResolvedParameter> parameters,
            SpecNode arrayNode,
            string path)
        {
            var items = new List<SpecNode>();
            foreach (ResolvedParameter parameter in parameters)
            {
                if (parameter.IsBody || parameter.IsFormData)
                {
                    continue;
                }

                string itemPath = F.Index(path, items.Count);
                if (parameter.ReferenceNode is not null)
                {
                    SpecNode reference = ConvertReference(
                        parameter.ReferenceNode,
                        F.Child(itemPath, "$ref"),
                        ReferenceKind.Parameter);
                    items.Add(F.Object(
                        parameter.Element,
                        itemPath,
                        new[] { F.Property("$ref", reference, parameter.ReferenceNode) }));
                }
                else
                {
                    items.Add(ConvertParameter(parameter.Node, itemPath));
                }
            }

            return items.Count == 0 ? null : F.Array(arrayNode, path, items);
        }

        /// <summary>
        /// Converts a query, header or path Parameter Object: type, format, items, enum, default and the
        /// constraints move into 'schema'; collectionFormat becomes style / explode.
        /// </summary>
        private SpecNode ConvertParameter(SpecNode node, string path)
        {
            RequireKind(node, SpecValueKind.Object, "A parameter must be an object.");
            SpecNode locationNode = RequireProperty(node, "in");
            string location = RequireString(locationNode, "The parameter 'in' must be a string.");
            if (location != "query" && location != "header" && location != "path")
            {
                throw Invalid(locationNode, "Unknown parameter location '" + location + "'.");
            }

            SpecNode typeNode = GetProperty(node, "type")
                ?? throw Invalid(node, "A non-body parameter requires a 'type'.");

            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in node.EnumerateObject())
            {
                if (SimpleSchemaKeywords.Contains(property.Name) || property.Name == "collectionFormat")
                {
                    continue;
                }

                // Policy for every other field (name, in, description, required, allowEmptyValue, x-* and
                // unknown fields): copy it as written. A field that OpenAPI 3.0 also has keeps its meaning;
                // x-* extensions are ignored by the parser; an unknown field is rejected by the parser's
                // Parameter field check (OACG101) at its original location rather than silently dropped.
                properties.Add(F.Property(property, F.Clone(property.Value, F.Child(path, property.Name))));
            }

            if (string.Equals(typeNode.GetString(), "array", StringComparison.Ordinal))
            {
                (string? style, bool? explode, SpecNode origin) = MapCollectionFormat(node, location);
                if (style is not null)
                {
                    properties.Add(F.Property("style", F.String(origin, F.Child(path, "style"), style), origin));
                    properties.Add(F.Property(
                        "explode",
                        F.Boolean(origin, F.Child(path, "explode"), explode ?? false),
                        origin));
                }
            }

            properties.Add(F.Property(
                "schema",
                BuildSimpleSchema(node, F.Child(path, "schema"), allowFile: false, copyDescription: false),
                node));
            return F.Object(node, path, properties);
        }

        /// <summary>
        /// Swagger 2.0 collectionFormat to an OpenAPI 3.0 style / explode. csv is the default.
        /// query: csv = form explode=false, ssv = spaceDelimited, pipes = pipeDelimited, multi = form
        /// explode=true (the default style, so nothing is returned). path and header only have csv
        /// (simple, the default). tsv and the other combinations have no equivalent.
        /// </summary>
        private (string? Style, bool? Explode, SpecNode Origin) MapCollectionFormat(
            SpecNode owner,
            string location)
        {
            SpecNode? formatNode = GetProperty(owner, "collectionFormat");
            SpecNode origin = formatNode ?? owner;
            string format = "csv";
            if (formatNode is not null)
            {
                format = RequireString(formatNode, "The collectionFormat must be a string.");
            }

            if (format == "tsv")
            {
                throw Unsupported(origin, "The collectionFormat 'tsv' has no OpenAPI 3.0 equivalent.");
            }

            if (format != "csv" && format != "ssv" && format != "pipes" && format != "multi")
            {
                throw Invalid(origin, "Unknown collectionFormat '" + format + "'.");
            }

            if (location == "query" || location == "formData")
            {
                switch (format)
                {
                    case "csv":
                        return ("form", false, origin);
                    case "ssv":
                        return ("spaceDelimited", false, origin);
                    case "pipes":
                        return ("pipeDelimited", false, origin);
                    default:
                        return (null, null, origin);
                }
            }

            if (format != "csv")
            {
                throw Unsupported(
                    origin,
                    "The collectionFormat '" + format + "' of a " + location +
                    " parameter has no OpenAPI 3.0 equivalent (only csv is supported there).");
            }

            return (null, null, origin);
        }

        // ---- requestBody -------------------------------------------------------------------------------

        private SpecNode? ConvertRequestBody(
            IReadOnlyList<ResolvedParameter> pathParameters,
            IReadOnlyList<ResolvedParameter> operationParameters,
            IReadOnlyList<string> consumes,
            string path)
        {
            // An operation parameter replaces a path parameter with the same name and location.
            var effective = new List<ResolvedParameter>();
            foreach (ResolvedParameter parameter in pathParameters.Concat(operationParameters))
            {
                int index = effective.FindIndex(existing =>
                    string.Equals(existing.Name, parameter.Name, StringComparison.Ordinal) &&
                    string.Equals(existing.Location, parameter.Location, StringComparison.Ordinal));
                if (index >= 0)
                {
                    effective[index] = parameter;
                }
                else
                {
                    effective.Add(parameter);
                }
            }

            List<ResolvedParameter> bodies = effective.Where(static parameter => parameter.IsBody).ToList();
            List<ResolvedParameter> forms = effective.Where(static parameter => parameter.IsFormData).ToList();
            if (bodies.Count > 1)
            {
                throw Invalid(bodies[1].Element, "An operation can have only one body parameter.");
            }

            if (bodies.Count == 1 && forms.Count > 0)
            {
                throw Invalid(forms[0].Element, "A body parameter and formData parameters cannot be combined.");
            }

            if (bodies.Count == 1)
            {
                return ConvertBodyParameter(bodies[0], consumes, path);
            }

            return forms.Count > 0 ? ConvertFormParameters(forms, consumes, path) : null;
        }

        private SpecNode ConvertBodyParameter(ResolvedParameter body, IReadOnlyList<string> consumes, string path)
        {
            SpecNode parameter = body.Node;
            SpecNode schema = GetProperty(parameter, "schema")
                ?? throw Invalid(parameter, "A body parameter requires a 'schema'.");
            var properties = new List<SpecProperty>();
            foreach (string name in new[] { "description", "required" })
            {
                if (parameter.TryGetProperty(name, out SpecNode value))
                {
                    SpecProperty source = parameter.EnumerateObject().First(
                        property => string.Equals(property.Name, name, StringComparison.Ordinal));
                    properties.Add(F.Property(source, F.Clone(value, F.Child(path, name))));
                }
            }

            string contentPath = F.Child(path, "content");
            var media = new List<SpecProperty>();
            foreach (string mediaType in consumes.Count > 0 ? consumes : new[] { DefaultRequestMediaType })
            {
                string mediaPath = F.Child(contentPath, mediaType);
                media.Add(F.Property(
                    mediaType,
                    F.Object(
                        parameter,
                        mediaPath,
                        new[]
                        {
                            F.Property(
                                "schema",
                                ConvertSchema(schema, F.Child(mediaPath, "schema")),
                                schema)
                        }),
                    parameter));
            }

            properties.Add(F.Property("content", F.Object(parameter, contentPath, media), parameter));
            return F.Object(parameter, path, properties);
        }

        /// <summary>
        /// formData parameters become one object schema (the parameters are its properties; type: file is
        /// string / binary) under multipart/form-data when a parameter is a file or consumes lists it, else
        /// under application/x-www-form-urlencoded. For url-encoded bodies an array parameter's
        /// collectionFormat becomes the property's encoding style; multipart bodies repeat the part for each
        /// element (the collectionFormat does not apply).
        /// </summary>
        private SpecNode ConvertFormParameters(
            IReadOnlyList<ResolvedParameter> forms,
            IReadOnlyList<string> consumes,
            string path)
        {
            SpecNode first = forms[0].Node;
            bool hasFile = forms.Any(static form =>
                form.Node.TryGetProperty("type", out SpecNode type) &&
                string.Equals(type.GetString(), "file", StringComparison.Ordinal));
            List<string> mediaTypes;
            if (hasFile)
            {
                mediaTypes = new List<string> { MultipartMediaType };
            }
            else
            {
                mediaTypes = consumes
                    .Where(static value => value == MultipartMediaType || value == UrlEncodedMediaType)
                    .ToList();
                if (mediaTypes.Count == 0)
                {
                    mediaTypes.Add(UrlEncodedMediaType);
                }
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var required = new List<ResolvedParameter>();
            foreach (ResolvedParameter form in forms)
            {
                if (!form.Node.TryGetProperty("type", out _))
                {
                    throw Invalid(form.Node, "A formData parameter requires a 'type'.");
                }

                if (!names.Add(form.Name))
                {
                    throw Invalid(form.Node, "The formData parameter '" + form.Name + "' is declared more than once.");
                }

                if (form.Node.TryGetProperty("required", out SpecNode requiredNode) &&
                    requiredNode.ValueKind == SpecValueKind.True)
                {
                    required.Add(form);
                }

                if (string.Equals(
                        GetProperty(form.Node, "type")?.GetString(),
                        "array",
                        StringComparison.Ordinal))
                {
                    // Validates the collectionFormat (tsv has no equivalent) even for multipart bodies.
                    MapCollectionFormat(form.Node, "formData");
                }
            }

            string contentPath = F.Child(path, "content");
            var media = new List<SpecProperty>();
            foreach (string mediaType in mediaTypes)
            {
                string mediaPath = F.Child(contentPath, mediaType);
                string schemaPath = F.Child(mediaPath, "schema");
                var schemaProperties = new List<SpecProperty>();
                var encodings = new List<SpecProperty>();
                foreach (ResolvedParameter form in forms)
                {
                    string propertyPath = F.Child(F.Child(schemaPath, "properties"), form.Name);
                    schemaProperties.Add(F.Property(
                        form.Name,
                        BuildSimpleSchema(form.Node, propertyPath, allowFile: true, copyDescription: true),
                        form.Node));
                    if (mediaType == UrlEncodedMediaType &&
                        string.Equals(GetProperty(form.Node, "type")?.GetString(), "array", StringComparison.Ordinal))
                    {
                        (string? style, bool? explode, SpecNode origin) =
                            MapCollectionFormat(form.Node, "formData");
                        if (style is not null)
                        {
                            string encodingPath = F.Child(F.Child(mediaPath, "encoding"), form.Name);
                            encodings.Add(F.Property(
                                form.Name,
                                F.Object(
                                    origin,
                                    encodingPath,
                                    new[]
                                    {
                                        F.Property("style", F.String(origin, F.Child(encodingPath, "style"), style), origin),
                                        F.Property(
                                            "explode",
                                            F.Boolean(origin, F.Child(encodingPath, "explode"), explode ?? false),
                                            origin)
                                    }),
                                origin));
                        }
                    }
                }

                var objectProperties = new List<SpecProperty>
                {
                    F.Property("type", F.String(first, F.Child(schemaPath, "type"), "object"), first),
                    F.Property(
                        "additionalProperties",
                        F.Boolean(first, F.Child(schemaPath, "additionalProperties"), false),
                        first),
                    F.Property(
                        "properties",
                        F.Object(first, F.Child(schemaPath, "properties"), schemaProperties),
                        first)
                };
                if (required.Count > 0)
                {
                    string requiredPath = F.Child(schemaPath, "required");
                    var names2 = new List<SpecNode>();
                    foreach (ResolvedParameter form in required)
                    {
                        names2.Add(F.String(form.Node, F.Index(requiredPath, names2.Count), form.Name));
                    }

                    objectProperties.Add(F.Property("required", F.Array(first, requiredPath, names2), first));
                }

                var mediaProperties = new List<SpecProperty>
                {
                    F.Property("schema", F.Object(first, schemaPath, objectProperties), first)
                };
                if (encodings.Count > 0)
                {
                    mediaProperties.Add(F.Property(
                        "encoding",
                        F.Object(first, F.Child(mediaPath, "encoding"), encodings),
                        first));
                }

                media.Add(F.Property(mediaType, F.Object(first, mediaPath, mediaProperties), first));
            }

            var properties = new List<SpecProperty>();
            if (required.Count > 0)
            {
                properties.Add(F.Property("required", F.Boolean(first, F.Child(path, "required"), true), first));
            }

            properties.Add(F.Property("content", F.Object(first, contentPath, media), first));
            return F.Object(first, path, properties);
        }
    }
}
