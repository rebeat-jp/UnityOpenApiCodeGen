using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Schema parsing: type inference, open objects, maps, recursion, enums and annotations.
    /// </summary>
    internal sealed partial class OpenApiSemanticParser
    {
        // Sentinel returned by type resolution when the schema accepts any JSON value.
        private const string AnyType = "*";

        // Schemas that are currently being parsed (as a $ref target or a component). A $ref back to
        // one of them that crosses a container (property, items, additionalProperties) is recursion,
        // not an alias cycle.
        private readonly HashSet<NormalizedSpecNodeIdentity> _schemasInProgress =
            new HashSet<NormalizedSpecNodeIdentity>();

        private bool _recursionObserved;
        private bool _componentsParsed;

        // Runs the named-type cycle check after the components are parsed (not per component) and
        // at the end, only when recursion was seen. The flag is not reset: a check run while an enclosing
        // schema is still being parsed cannot see it yet, so later registrations must re-check.
        private void ValidateRecursionIfObserved()
        {
            if (!_recursionObserved)
            {
                return;
            }

            ValidateStructuralCycles();
        }

        private OpenApiSemanticSchema ParseSchema(
            SpecNode node,
            string suggestedName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            if (_minorVersion == 1 &&
                (node.ValueKind == SpecValueKind.True || node.ValueKind == SpecValueKind.False))
            {
                return ParseBooleanSchema(node, suggestedName);
            }

            RequireKind(node, SpecValueKind.Object, "A schema must be an object in the Phase 4 MVP.");
            ValidateUnsupportedSchemaKeywords(node);

            // not is a response-validation add-on that can accompany any schema shape.
            return ApplyNotKeyword(
                node,
                suggestedName,
                ParseSchemaShape(node, suggestedName, referenceStack));
        }

        private OpenApiSemanticSchema ParseSchemaShape(
            SpecNode node,
            string suggestedName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                if (_minorVersion == 1 && HasStructuralReferenceSiblings(node))
                {
                    // OpenAPI 3.1: $ref with meaningful siblings is allOf: [ { $ref }, { siblings } ].
                    return ParseObjectComposition(node, suggestedName, referenceStack);
                }

                return ParseReferenceSchema(node, suggestedName, referenceStack, reference, referenceNode);
            }

            if (HasUnionKeyword(node))
            {
                return ParseUnion(node, suggestedName, referenceStack);
            }

            if (node.TryGetProperty("discriminator", out _))
            {
                // A discriminator without oneOf / anyOf marks the base of an allOf hierarchy.
                OpenApiSemanticSchema? inheritanceUnion =
                    TryParseInheritanceUnion(node, suggestedName, referenceStack);
                if (inheritanceUnion is not null)
                {
                    return inheritanceUnion;
                }
            }

            if (node.TryGetProperty("allOf", out _))
            {
                return ParseAllOf(node, suggestedName, referenceStack);
            }

            bool nullable = ParseNullable(node);
            string type = ResolveSchemaType(node, ref nullable);
            string format = GetOptionalString(node, "format") ?? string.Empty;
            OpenApiSemanticAnnotations annotations = ParseAnnotations(node, allowConst: true);

            SpecNode? enumNode = GetProperty(node, "enum");
            if (enumNode is not null)
            {
                if (annotations.Const.HasValue)
                {
                    AddIgnoredWarning(
                        GetProperty(node, "const") ?? node,
                        "const on an enum schema is not enforced separately; the enum values are validated.");
                    annotations = WithoutConst(annotations);
                }

                return ParseEnumSchema(node, enumNode, type, suggestedName, format, nullable, annotations);
            }

            if (type == "string" && string.Equals(format, "binary", StringComparison.Ordinal))
            {
                throw Unsupported(node, "Binary schemas are outside the JSON-only Phase 4 MVP.");
            }

            annotations = ValidateConst(node, type, annotations);
            switch (type)
            {
                case "string":
                    return CreateSimpleSchema(
                        OpenApiSemanticSchemaKind.String, node, suggestedName, format, nullable, annotations);
                case "integer":
                    return CreateSimpleSchema(
                        OpenApiSemanticSchemaKind.Integer, node, suggestedName, format, nullable, annotations);
                case "number":
                    return CreateSimpleSchema(
                        OpenApiSemanticSchemaKind.Number, node, suggestedName, format, nullable, annotations);
                case "boolean":
                    return CreateSimpleSchema(
                        OpenApiSemanticSchemaKind.Boolean, node, suggestedName, format, nullable, annotations);
                case "array":
                    // Items are a container boundary: a fresh alias stack lets an item refer back to
                    // an enclosing schema (recursion) while pure $ref alias chains stay rejected.
                    OpenApiSemanticSchema itemSchema = ParseSchema(
                        RequireProperty(node, "items"),
                        suggestedName + "Item",
                        new HashSet<NormalizedSpecNodeIdentity>());
                    return new OpenApiSemanticSchema(
                        OpenApiSemanticSchemaKind.Array,
                        suggestedName,
                        format,
                        nullable,
                        string.Empty,
                        Array.Empty<OpenApiSemanticProperty>(),
                        itemSchema,
                        Array.Empty<string>(),
                        CreateLocation(node),
                        annotations: annotations);
                case "object":
                    return ParseObjectSchema(node, suggestedName, format, nullable, annotations);
                case AnyType:
                    return CreateAnySchema(node, suggestedName, annotations);
                default:
                    throw Unsupported(node, "Unsupported schema type: '" + type + "'.");
            }
        }

        private OpenApiSemanticSchema ParseBooleanSchema(SpecNode node, string suggestedName)
        {
            if (node.ValueKind == SpecValueKind.False)
            {
                throw Unsupported(
                    node,
                    "The 'false' boolean schema accepts no value and cannot be represented by a generated type.");
            }

            return CreateAnySchema(node, suggestedName, OpenApiSemanticAnnotations.Empty);
        }

        private OpenApiSemanticSchema CreateAnySchema(
            SpecNode node,
            string suggestedName,
            OpenApiSemanticAnnotations annotations)
        {
            // Any (JToken) always accepts JSON null, so it is nullable by construction.
            return new OpenApiSemanticSchema(
                OpenApiSemanticSchemaKind.Any,
                suggestedName,
                string.Empty,
                true,
                string.Empty,
                Array.Empty<OpenApiSemanticProperty>(),
                null,
                Array.Empty<string>(),
                CreateLocation(node),
                annotations: annotations);
        }

        private OpenApiSemanticSchema ParseReferenceSchema(
            SpecNode node,
            string suggestedName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack,
            string reference,
            SpecNode referenceNode,
            bool? nullableOverride = null,
            OpenApiSemanticAnnotations? annotationsOverride = null)
        {
            ValidateReferenceSchemaSiblings(node);
            ResolvedSpecReference resolved = ResolveSchemaReference(
                reference,
                referenceNode,
                out string referenceName);
            return BuildReferenceSchema(
                node,
                suggestedName,
                referenceStack,
                reference,
                referenceNode,
                resolved,
                referenceName,
                nullableOverride ?? ParseNullable(node),
                annotationsOverride ?? ParseAnnotations(node, allowConst: false));
        }

        /// <summary>
        /// Creates a Reference schema for an already resolved target, making sure the target is
        /// parsed (or known to be in progress, which marks the reference as recursive).
        /// </summary>
        private OpenApiSemanticSchema BuildReferenceSchema(
            SpecNode node,
            string suggestedName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack,
            string reference,
            SpecNode referenceNode,
            ResolvedSpecReference resolved,
            string referenceName,
            bool nullable,
            OpenApiSemanticAnnotations annotations)
        {
            if (referenceStack.Contains(resolved.Identity))
            {
                // Only $ref aliases lead back here without crossing a container, so nothing
                // would ever give the schema a concrete shape.
                throw CreateException(
                    OpenApiSemanticErrorKind.CyclicReference,
                    "A cyclic internal $ref was detected at '" + reference + "'.",
                    referenceNode,
                    new[] { CreateLocation(resolved.Node, resolved.DocumentId) });
            }

            if (!_schemas.ContainsKey(resolved.Identity))
            {
                if (_schemasInProgress.Contains(resolved.Identity))
                {
                    // A self or mutual reference through property / items / additionalProperties.
                    // The target is registered when its own parse completes; the generation model
                    // resolves the reference by identity.
                    _recursionObserved = true;
                    if (_notDepth > 0)
                    {
                        _notRecursion = true;
                    }
                }
                else
                {
                    _schemasInProgress.Add(resolved.Identity);
                    try
                    {
                        OpenApiSemanticSchema targetSchema = ParseResolvedReferenced(
                            reference,
                            referenceNode,
                            referenceStack,
                            resolved,
                            target => ParseSchema(target, referenceName, referenceStack));
                        AddSchema(resolved.Identity, targetSchema);
                    }
                    finally
                    {
                        _schemasInProgress.Remove(resolved.Identity);
                    }
                }
            }

            return new OpenApiSemanticSchema(
                OpenApiSemanticSchemaKind.Reference,
                suggestedName,
                string.Empty,
                nullable,
                referenceName,
                Array.Empty<OpenApiSemanticProperty>(),
                null,
                Array.Empty<string>(),
                CreateLocation(node),
                identity: new NormalizedSpecNodeIdentity(_currentDocumentId, node.LogicalPath),
                referenceIdentity: resolved.Identity,
                annotations: annotations);
        }

        /// <summary>
        /// Resolves a schema $ref. Unlike other component references it may target any JSON pointer
        /// (a property schema, $defs, an extension) or, in a bundle, any fragment of an external
        /// document. The type name seed is the last pointer token.
        /// </summary>
        private ResolvedSpecReference ResolveSchemaReference(
            string reference,
            SpecNode referenceNode,
            out string referenceName)
        {
            ResolvedSpecReference resolved = ResolveReference(reference, referenceNode);
            if (resolved.Pointer.Length == 0)
            {
                bool bareSchema =
                    (resolved.Node.ValueKind == SpecValueKind.Object ||
                     resolved.Node.ValueKind == SpecValueKind.True ||
                     resolved.Node.ValueKind == SpecValueKind.False) &&
                    !resolved.Node.TryGetProperty("openapi", out _);
                if (!bareSchema)
                {
                    throw Unsupported(
                        referenceNode,
                        "An external document root may be used only when it is a bare schema.");
                }

                referenceName = GetBareSchemaName(resolved);
            }
            else
            {
                referenceName = JsonPointerResolver.GetSchemaPointerName(resolved.Pointer, referenceNode);
            }

            return resolved;
        }

        private OpenApiSemanticSchema ParseObjectSchema(
            SpecNode node,
            string suggestedName,
            string format,
            bool nullable,
            OpenApiSemanticAnnotations annotations)
        {
            SpecNode? additionalNode = GetProperty(node, "additionalProperties");
            OpenApiSemanticAdditionalProperties mode;
            OpenApiSemanticSchema? valueSchema = null;
            if (additionalNode is null || additionalNode.ValueKind == SpecValueKind.True)
            {
                mode = OpenApiSemanticAdditionalProperties.Open;
            }
            else if (additionalNode.ValueKind == SpecValueKind.False)
            {
                mode = OpenApiSemanticAdditionalProperties.Closed;
            }
            else if (additionalNode.ValueKind == SpecValueKind.Object)
            {
                mode = OpenApiSemanticAdditionalProperties.Typed;
                valueSchema = ParseSchema(
                    additionalNode,
                    suggestedName + "Value",
                    new HashSet<NormalizedSpecNodeIdentity>());
            }
            else
            {
                throw Invalid(additionalNode, "The additionalProperties field must be a boolean or a schema object.");
            }

            SpecNode? propertiesNode = GetProperty(node, "properties");
            if (propertiesNode is not null)
            {
                RequireKind(propertiesNode, SpecValueKind.Object, "The schema properties field must be an object.");
            }

            var required = new HashSet<string>(StringComparer.Ordinal);
            var requiredLocations = new Dictionary<string, SpecNode>(StringComparer.Ordinal);
            SpecNode? requiredNode = GetProperty(node, "required");
            if (requiredNode is not null)
            {
                RequireKind(requiredNode, SpecValueKind.Array, "Required property names must be strings.");
                foreach (SpecNode requiredItem in requiredNode.EnumerateArray())
                {
                    string value = RequireString(requiredItem, "Required property names must be strings.");
                    if (!required.Add(value))
                    {
                        throw Invalid(
                            requiredItem,
                            "The required array contains the property name '" + value + "' more than once.");
                    }

                    requiredLocations.Add(value, requiredItem);
                }
            }

            var properties = new List<OpenApiSemanticProperty>();
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            if (propertiesNode is not null)
            {
                foreach (SpecProperty property in propertiesNode.EnumerateObject()
                             .OrderBy(static value => value.Name, StringComparer.Ordinal))
                {
                    propertyNames.Add(property.Name);
                    // Properties are a container boundary (see the array case in ParseSchema).
                    OpenApiSemanticSchema propertySchema = ParseSchema(
                        property.Value,
                        suggestedName + ToPascalFragment(property.Name),
                        new HashSet<NormalizedSpecNodeIdentity>());
                    properties.Add(new OpenApiSemanticProperty(
                        property.Name,
                        required.Contains(property.Name),
                        propertySchema,
                        CreateLocation(property.Value)));
                }
            }

            string[] missingRequiredProperties = required
                .Where(value => !propertyNames.Contains(value))
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
            if (missingRequiredProperties.Length > 0)
            {
                string missingProperty = missingRequiredProperties[0];
                throw Invalid(
                    requiredLocations[missingProperty],
                    "Required property '" + missingProperty + "' is not declared in schema properties.");
            }

            return CreateObjectShape(node, suggestedName, format, nullable, annotations, properties, mode, valueSchema);
        }

        /// <summary>
        /// Chooses between a free-form object, a map and a DTO from the collected members.
        /// Shared by plain object schemas and allOf compositions.
        /// </summary>
        private OpenApiSemanticSchema CreateObjectShape(
            SpecNode node,
            string suggestedName,
            string format,
            bool nullable,
            OpenApiSemanticAnnotations annotations,
            IReadOnlyList<OpenApiSemanticProperty> properties,
            OpenApiSemanticAdditionalProperties mode,
            OpenApiSemanticSchema? valueSchema)
        {
            if (properties.Count == 0 && mode == OpenApiSemanticAdditionalProperties.Open)
            {
                // Free-form object: nothing is declared, so there is no DTO to generate.
                return new OpenApiSemanticSchema(
                    OpenApiSemanticSchemaKind.FreeObject,
                    suggestedName,
                    format,
                    nullable,
                    string.Empty,
                    Array.Empty<OpenApiSemanticProperty>(),
                    null,
                    Array.Empty<string>(),
                    CreateLocation(node),
                    annotations: annotations);
            }

            if (properties.Count == 0 && mode == OpenApiSemanticAdditionalProperties.Typed)
            {
                return new OpenApiSemanticSchema(
                    OpenApiSemanticSchemaKind.Map,
                    suggestedName,
                    format,
                    nullable,
                    string.Empty,
                    Array.Empty<OpenApiSemanticProperty>(),
                    null,
                    Array.Empty<string>(),
                    CreateLocation(node),
                    annotations: annotations,
                    valueSchema: valueSchema);
            }

            return new OpenApiSemanticSchema(
                OpenApiSemanticSchemaKind.Object,
                suggestedName,
                format,
                nullable,
                string.Empty,
                properties,
                null,
                Array.Empty<string>(),
                CreateLocation(node),
                annotations: annotations,
                valueSchema: valueSchema,
                additionalProperties: mode);
        }

        private OpenApiSemanticSchema ParseEnumSchema(
            SpecNode node,
            SpecNode enumNode,
            string type,
            string suggestedName,
            string format,
            bool nullable,
            OpenApiSemanticAnnotations annotations)
        {
            RequireKind(enumNode, SpecValueKind.Array, "The schema enum field must be an array.");
            SpecNode[] entries = enumNode.EnumerateArray().ToArray();
            if (entries.Length == 0)
            {
                throw Invalid(enumNode, "An enum must contain at least one value.");
            }

            bool hasNull = entries.Any(static value => value.ValueKind == SpecValueKind.Null);
            var valueIndices = new List<int>();
            for (int index = 0; index < entries.Length; index++)
            {
                if (entries[index].ValueKind != SpecValueKind.Null)
                {
                    valueIndices.Add(index);
                }
            }

            if (valueIndices.Count == 0)
            {
                throw Unsupported(enumNode, "An enum that contains only null is not supported by the Phase 4 MVP.");
            }

            bool isInteger;
            if (type == "string")
            {
                foreach (int index in valueIndices)
                {
                    RequireString(entries[index], "Enum values must be strings.");
                }

                isInteger = false;
            }
            else if (type == "integer")
            {
                foreach (int index in valueIndices)
                {
                    if (entries[index].ValueKind != SpecValueKind.Number || !entries[index].IsInteger)
                    {
                        throw Invalid(entries[index], "Enum values must be integers.");
                    }
                }

                isInteger = true;
            }
            else if (type == AnyType)
            {
                bool allStrings = valueIndices.All(index => entries[index].ValueKind == SpecValueKind.String);
                bool allIntegers = valueIndices.All(index =>
                    entries[index].ValueKind == SpecValueKind.Number && entries[index].IsInteger);
                if (!allStrings && !allIntegers)
                {
                    bool anyString = valueIndices.Any(index => entries[index].ValueKind == SpecValueKind.String);
                    bool anyInteger = valueIndices.Any(index =>
                        entries[index].ValueKind == SpecValueKind.Number && entries[index].IsInteger);
                    throw Unsupported(
                        enumNode,
                        anyString && anyInteger
                            ? "Enums that mix string and integer values are not supported by the Phase 4 MVP."
                            : "Only string and integer enums are supported by the Phase 4 MVP.");
                }

                isInteger = allIntegers;
            }
            else
            {
                throw Unsupported(enumNode, "Only string and integer enums are supported by the Phase 4 MVP.");
            }

            var literals = new List<OpenApiSemanticLiteral>(valueIndices.Count);
            var seenValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (int index in valueIndices)
            {
                SpecNode entry = entries[index];
                OpenApiSemanticLiteral literal;
                if (isInteger)
                {
                    if (!entry.TryGetInt64(out long number))
                    {
                        throw Unsupported(entry, "Integer enum values must fit in a 64-bit signed integer.");
                    }

                    literal = new OpenApiSemanticLiteral(
                        OpenApiSemanticLiteralKind.Integer,
                        number.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    literal = OpenApiSemanticLiteral.FromString(entry.GetString() ?? string.Empty);
                }

                if (!seenValues.Add(literal.RawText))
                {
                    throw Unsupported(
                        entry,
                        (isInteger ? "Integer" : "String") +
                        " enum values must be unique for the Phase 4 generated C# enum contract.");
                }

                literals.Add(literal);
            }

            IReadOnlyList<string?> memberNames = ParseEnumMemberStrings(
                node, entries, valueIndices, "x-enum-varnames", "x-enumNames");
            IReadOnlyList<string?> memberDescriptions = ParseEnumMemberStrings(
                node, entries, valueIndices, "x-enum-descriptions");

            // A 3.1 type union containing null does not make the enum nullable by itself; only an
            // explicit null value does. In 3.0 the nullable keyword (already in `nullable`) applies.
            if (_minorVersion == 1)
            {
                nullable = false;
            }

            if (hasNull)
            {
                nullable = true;
            }

            return new OpenApiSemanticSchema(
                OpenApiSemanticSchemaKind.Enum,
                suggestedName,
                format,
                nullable,
                string.Empty,
                Array.Empty<OpenApiSemanticProperty>(),
                null,
                Array.Empty<string>(),
                CreateLocation(node),
                annotations: annotations,
                typedEnumValues: literals,
                enumMemberNames: memberNames,
                enumMemberDescriptions: memberDescriptions);
        }

        /// <summary>
        /// Reads a vendor array that is parallel to the enum values (x-enum-varnames and friends).
        /// A malformed array is ignored with a warning instead of failing the whole document.
        /// </summary>
        private IReadOnlyList<string?> ParseEnumMemberStrings(
            SpecNode schemaNode,
            SpecNode[] entries,
            List<int> valueIndices,
            params string[] keywords)
        {
            foreach (string keyword in keywords)
            {
                SpecNode? arrayNode = GetProperty(schemaNode, keyword);
                if (arrayNode is null)
                {
                    continue;
                }

                if (arrayNode.ValueKind != SpecValueKind.Array)
                {
                    AddIgnoredWarning(arrayNode, keyword + " must be an array of strings and was ignored.");
                    return Array.Empty<string?>();
                }

                SpecNode[] items = arrayNode.EnumerateArray().ToArray();
                if (items.Length != entries.Length ||
                    items.Any(static item => item.ValueKind != SpecValueKind.String &&
                                              item.ValueKind != SpecValueKind.Null))
                {
                    AddIgnoredWarning(
                        arrayNode,
                        keyword + " must be an array of strings with one entry per enum value and was ignored.");
                    return Array.Empty<string?>();
                }

                var result = new List<string?>(valueIndices.Count);
                foreach (int index in valueIndices)
                {
                    result.Add(items[index].GetString());
                }

                return result;
            }

            return Array.Empty<string?>();
        }

        private string ResolveSchemaType(SpecNode node, ref bool nullable)
        {
            SpecNode? typeNode = GetProperty(node, "type");
            if (typeNode is null)
            {
                return InferSchemaType(node);
            }

            if (typeNode.ValueKind == SpecValueKind.String)
            {
                string single = typeNode.GetString() ?? string.Empty;
                if (single == "null" && _minorVersion == 1)
                {
                    nullable = true;
                    return AnyType;
                }

                return single;
            }

            if (_minorVersion != 1 || typeNode.ValueKind != SpecValueKind.Array)
            {
                throw Invalid(typeNode, "The schema type must be a string, or a type array in OpenAPI 3.1.");
            }

            IReadOnlyList<string> types = ParseStringArray(typeNode, "OpenAPI 3.1 type union entries must be strings.");
            if (types.Count == 0)
            {
                throw Invalid(typeNode, "The schema type array must not be empty.");
            }

            string[] nonNullTypes = types.Where(static value => value != "null").Distinct(StringComparer.Ordinal).ToArray();
            if (types.Contains("null"))
            {
                nullable = true;
            }

            // One type (optionally with null) keeps its precise mapping; several types have no
            // single CLR representation, so the value stays an untyped JToken.
            return nonNullTypes.Length == 1 ? nonNullTypes[0] : AnyType;
        }

        private string InferSchemaType(SpecNode node)
        {
            if (GetProperty(node, "enum") is not null)
            {
                return AnyType;
            }

            SpecNode? constNode = _minorVersion == 1 ? GetProperty(node, "const") : null;
            if (constNode is not null)
            {
                switch (constNode.ValueKind)
                {
                    case SpecValueKind.String:
                        return "string";
                    case SpecValueKind.True:
                    case SpecValueKind.False:
                        return "boolean";
                    case SpecValueKind.Number:
                        return constNode.IsInteger ? "integer" : "number";
                    default:
                        return AnyType;
                }
            }

            if (GetProperty(node, "properties") is not null ||
                GetProperty(node, "additionalProperties") is not null)
            {
                return "object";
            }

            if (GetProperty(node, "items") is not null)
            {
                return "array";
            }

            return AnyType;
        }

        private OpenApiSemanticAnnotations ParseAnnotations(SpecNode node, bool allowConst)
        {
            string? title = GetOptionalString(node, "title");
            string? description = GetOptionalString(node, "description");
            string documentation;
            if (!string.IsNullOrWhiteSpace(title) &&
                !string.IsNullOrWhiteSpace(description) &&
                !string.Equals(title, description, StringComparison.Ordinal))
            {
                documentation = title + "\n" + description;
            }
            else
            {
                documentation = !string.IsNullOrWhiteSpace(description) ? description! : title ?? string.Empty;
            }

            bool deprecated = GetOptionalBoolean(node, "deprecated") ?? false;
            bool readOnly = GetOptionalBoolean(node, "readOnly") ?? false;
            bool writeOnly = GetOptionalBoolean(node, "writeOnly") ?? false;
            OpenApiSemanticLiteral? defaultValue = ParseScalarLiteral(GetProperty(node, "default"));
            OpenApiSemanticLiteral? constValue = null;
            SpecNode? constNode = allowConst && _minorVersion == 1 ? GetProperty(node, "const") : null;
            if (constNode is not null)
            {
                constValue = ParseScalarLiteral(constNode);
                if (constValue is null)
                {
                    throw Unsupported(
                        constNode,
                        "const values that are objects or arrays are not supported by the Phase 4 MVP.");
                }
            }

            return new OpenApiSemanticAnnotations(
                documentation,
                deprecated,
                readOnly,
                writeOnly,
                defaultValue,
                constValue);
        }

        private static OpenApiSemanticAnnotations WithoutConst(OpenApiSemanticAnnotations annotations)
        {
            return new OpenApiSemanticAnnotations(
                annotations.Description,
                annotations.Deprecated,
                annotations.ReadOnly,
                annotations.WriteOnly,
                annotations.Default,
                null);
        }

        /// <summary>
        /// Converts a scalar JSON value to a literal. Objects and arrays have no literal form and
        /// return null (a default of that kind is documentation only and is ignored).
        /// </summary>
        private static OpenApiSemanticLiteral? ParseScalarLiteral(SpecNode? node)
        {
            if (node is null)
            {
                return null;
            }

            switch (node.ValueKind)
            {
                case SpecValueKind.String:
                    return OpenApiSemanticLiteral.FromString(node.GetString() ?? string.Empty);
                case SpecValueKind.Number:
                    return new OpenApiSemanticLiteral(
                        node.IsInteger ? OpenApiSemanticLiteralKind.Integer : OpenApiSemanticLiteralKind.Number,
                        node.GetRawJson());
                case SpecValueKind.True:
                    return new OpenApiSemanticLiteral(OpenApiSemanticLiteralKind.Boolean, "true");
                case SpecValueKind.False:
                    return new OpenApiSemanticLiteral(OpenApiSemanticLiteralKind.Boolean, "false");
                case SpecValueKind.Null:
                    return OpenApiSemanticLiteral.Null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Checks that a const value matches the schema type. Constants that cannot be enforced
        /// (null, or a schema without a single scalar type) are dropped with a warning.
        /// </summary>
        private OpenApiSemanticAnnotations ValidateConst(
            SpecNode node,
            string type,
            OpenApiSemanticAnnotations annotations)
        {
            if (!annotations.Const.HasValue)
            {
                return annotations;
            }

            OpenApiSemanticLiteral literal = annotations.Const.Value;
            SpecNode constNode = GetProperty(node, "const") ?? node;
            bool scalarType = type == "string" || type == "integer" || type == "number" || type == "boolean";
            if (literal.Kind == OpenApiSemanticLiteralKind.Null || !scalarType)
            {
                AddIgnoredWarning(
                    constNode,
                    "const is not enforced for a null value or for a schema without a single scalar type.");
                return WithoutConst(annotations);
            }

            bool matches;
            switch (type)
            {
                case "string":
                    matches = literal.Kind == OpenApiSemanticLiteralKind.String;
                    break;
                case "integer":
                    matches = literal.Kind == OpenApiSemanticLiteralKind.Integer;
                    break;
                case "number":
                    matches = literal.Kind == OpenApiSemanticLiteralKind.Integer ||
                              literal.Kind == OpenApiSemanticLiteralKind.Number;
                    break;
                default:
                    matches = literal.Kind == OpenApiSemanticLiteralKind.Boolean;
                    break;
            }

            if (!matches)
            {
                throw Invalid(constNode, "The const value does not match the schema type '" + type + "'.");
            }

            return annotations;
        }

        private OpenApiSemanticSchema CreateSimpleSchema(
            OpenApiSemanticSchemaKind kind,
            SpecNode node,
            string suggestedName,
            string format,
            bool nullable,
            OpenApiSemanticAnnotations annotations)
        {
            return new OpenApiSemanticSchema(
                kind,
                suggestedName,
                format,
                nullable,
                string.Empty,
                Array.Empty<OpenApiSemanticProperty>(),
                null,
                Array.Empty<string>(),
                CreateLocation(node),
                annotations: annotations);
        }

        /// <summary>
        /// Rejects recursion that is not broken by a named type. A cycle that passes only through
        /// arrays, maps and $ref aliases has no finite C# type (List of List of ...), so it is
        /// reported as a cyclic reference. Recursion through an object or enum is fine because a
        /// DTO / enum declaration gives the cycle a name.
        /// </summary>
        private void ValidateStructuralCycles()
        {
            var finished = new HashSet<NormalizedSpecNodeIdentity>();
            foreach (KeyValuePair<NormalizedSpecNodeIdentity, OpenApiSemanticSchema> pair in _schemas
                         .OrderBy(static value => value.Key.DocumentId, StringComparer.Ordinal)
                         .ThenBy(static value => value.Key.Pointer, StringComparer.Ordinal)
                         .ToArray())
            {
                if (finished.Contains(pair.Key))
                {
                    continue;
                }

                var path = new List<NormalizedSpecNodeIdentity> { pair.Key };
                VisitStructuralSchema(pair.Value, path, finished);
                finished.Add(pair.Key);
            }
        }

        private void VisitStructuralSchema(
            OpenApiSemanticSchema schema,
            List<NormalizedSpecNodeIdentity> path,
            HashSet<NormalizedSpecNodeIdentity> finished)
        {
            switch (schema.Kind)
            {
                case OpenApiSemanticSchemaKind.Array:
                    if (schema.ItemSchema is not null)
                    {
                        VisitStructuralSchema(schema.ItemSchema, path, finished);
                    }

                    return;
                case OpenApiSemanticSchemaKind.Map:
                    if (schema.ValueSchema is not null)
                    {
                        VisitStructuralSchema(schema.ValueSchema, path, finished);
                    }

                    return;
                case OpenApiSemanticSchemaKind.Union:
                    // A union is a named wrapper class (like an object or an enum), so a cycle through
                    // one of its members is finite; the members are not part of the walk.
                    return;
                case OpenApiSemanticSchemaKind.Reference:
                    NormalizedSpecNodeIdentity identity = schema.ReferenceIdentity;
                    if (identity.IsEmpty ||
                        !_schemas.TryGetValue(identity, out OpenApiSemanticSchema? target) ||
                        target.Kind == OpenApiSemanticSchemaKind.Object ||
                        target.Kind == OpenApiSemanticSchemaKind.Enum ||
                        target.Kind == OpenApiSemanticSchemaKind.Union)
                    {
                        return;
                    }

                    if (path.Contains(identity))
                    {
                        throw new OpenApiSemanticException(
                            OpenApiSemanticErrorKind.CyclicReference,
                            "A cyclic $ref to '" + schema.ReferenceName +
                            "' passes only through arrays and maps, so no named type can represent it.",
                            schema.Location,
                            Array.Empty<OpenApiSourceLocation>());
                    }

                    if (finished.Contains(identity))
                    {
                        return;
                    }

                    path.Add(identity);
                    VisitStructuralSchema(target, path, finished);
                    path.RemoveAt(path.Count - 1);
                    finished.Add(identity);
                    return;
                default:
                    return;
            }
        }
    }
}
