using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Maps any schema subtree to an <see cref="OpenApiSemanticAssertion"/> (JSON Schema semantics, no type of
    /// its own). It does not go through ParseSchema, so it also accepts the part schemas that have no type
    /// ({"properties":{"x":{"maximum":99}}}, {"enum":[null]}, {"required":["a"]}, {"not":{"required":["x"]}}).
    /// The dialect decisions are those of the parser: OpenAPI 3.0 has type + nullable (nullable adds null only
    /// next to a type; a schema without type does not restrict null), the siblings of a $ref are ignored, and the
    /// exclusive bounds are booleans; OpenAPI 3.1 has type arrays and "null", const, numeric exclusive bounds and
    /// the 2020-12 applicators (patternProperties, propertyNames, contains, prefixItems, dependentRequired). Unknown
    /// keywords, x- extensions and annotations (description, example, ...) are ignored.
    /// A $ref becomes a shared, named node (see <see cref="OpenApiSemanticAssertion.Name"/>), so a recursive
    /// schema is a cycle of nodes. The mapper relies on the document having passed the parser's checks for cyclic
    /// $ref aliases (A: {$ref: A}); a cycle that consumes no data would loop at run time.
    /// </summary>
    internal sealed partial class OpenApiSemanticParser
    {
        private readonly Dictionary<NormalizedSpecNodeIdentity, OpenApiSemanticAssertion> _assertionTargets =
            new Dictionary<NormalizedSpecNodeIdentity, OpenApiSemanticAssertion>();

        /// <summary>
        /// Creates a mapper for a document whose "openapi" version selects the dialect (the document is not parsed
        /// otherwise). The $refs of the subtrees are resolved against <paramref name="root"/>.
        /// </summary>
        internal static OpenApiSemanticParser CreateAssertionMapper(SpecNode root)
        {
            if (root is null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            RequireKind(root, SpecValueKind.Object, "The OpenAPI document root must be an object.");
            SpecNode versionNode = RequireProperty(root, "openapi");
            string version = RequireString(versionNode, "The 'openapi' field must be a string.");
            return new OpenApiSemanticParser(root, ParseSupportedVersion(version, versionNode));
        }

        /// <summary>Maps a schema node (and, through $ref, the schemas it uses) to an assertion tree.</summary>
        internal OpenApiSemanticAssertion MapAssertion(SpecNode node)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (_minorVersion == 1 && (node.ValueKind == SpecValueKind.True || node.ValueKind == SpecValueKind.False))
            {
                return new OpenApiSemanticAssertion { AlwaysFalse = node.ValueKind == SpecValueKind.False };
            }

            RequireKind(node, SpecValueKind.Object, "A schema must be an object.");
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                OpenApiSemanticAssertion target = MapAssertionTarget(reference, referenceNode);
                if (_minorVersion == 0)
                {
                    // OpenAPI 3.0: the siblings are ignored, except those the parser keeps (nullable, readOnly, writeOnly).
                    return WrapReference30(node, target);
                }

                var withSiblings = new OpenApiSemanticAssertion { Reference = target };
                FillAssertion(withSiblings, node);
                return withSiblings.HasNoOwnKeyword && !withSiblings.ReadOnly && !withSiblings.WriteOnly
                    ? target
                    : withSiblings;
            }

            var result = new OpenApiSemanticAssertion();
            FillAssertion(result, node);
            return result;
        }

        // {$ref, nullable, readOnly, writeOnly} of OpenAPI 3.0, the same siblings that ParseSchema accepts next to $ref.
        // nullable: true makes the schema "null or the target".
        private OpenApiSemanticAssertion WrapReference30(SpecNode node, OpenApiSemanticAssertion target)
        {
            bool readOnly = GetOptionalBoolean(node, "readOnly") ?? false;
            bool writeOnly = GetOptionalBoolean(node, "writeOnly") ?? false;
            bool nullable = ParseNullable(node);
            if (!readOnly && !writeOnly && !nullable)
            {
                return target;
            }

            var wrapper = new OpenApiSemanticAssertion { ReadOnly = readOnly, WriteOnly = writeOnly };
            ApplyReference30(wrapper, target, nullable);
            return wrapper;
        }

        private static void ApplyReference30(OpenApiSemanticAssertion wrapper, OpenApiSemanticAssertion target, bool nullable)
        {
            if (nullable)
            {
                wrapper.AnyOf = new[]
                {
                    new OpenApiSemanticAssertion { Types = OpenApiSemanticAssertionTypes.Null },
                    target
                };
                wrapper.AnnotationTarget = target;
            }
            else
            {
                wrapper.Reference = target;
            }
        }

        private OpenApiSemanticAssertion MapAssertionTarget(string reference, SpecNode referenceNode)
        {
            ResolvedSpecReference resolved = ResolveReference(reference, referenceNode);
            if (_assertionTargets.TryGetValue(resolved.Identity, out OpenApiSemanticAssertion? existing))
            {
                return existing;
            }

            // Registered before the body is mapped: a $ref back to it (a recursive schema) finds the node.
            var target = new OpenApiSemanticAssertion
            {
                Name = JsonPointerResolver.GetSchemaPointerName(resolved.Pointer, referenceNode)
            };
            _assertionTargets.Add(resolved.Identity, target);
            string previousDocumentId = _currentDocumentId;
            try
            {
                _currentDocumentId = resolved.DocumentId;
                if (_minorVersion == 1 && (resolved.Node.ValueKind == SpecValueKind.True || resolved.Node.ValueKind == SpecValueKind.False))
                {
                    target.AlwaysFalse = resolved.Node.ValueKind == SpecValueKind.False;
                }
                else
                {
                    RequireKind(resolved.Node, SpecValueKind.Object, "A schema must be an object.");
                    if (TryGetReference(resolved.Node, out string innerReference, out SpecNode innerReferenceNode))
                    {
                        // A $ref whose target is a $ref: 3.0 keeps nullable, readOnly and writeOnly, 3.1 keeps every sibling.
                        OpenApiSemanticAssertion inner = MapAssertionTarget(innerReference, innerReferenceNode);
                        if (_minorVersion == 1)
                        {
                            target.Reference = inner;
                            FillAssertion(target, resolved.Node);
                        }
                        else
                        {
                            target.ReadOnly = GetOptionalBoolean(resolved.Node, "readOnly") ?? false;
                            target.WriteOnly = GetOptionalBoolean(resolved.Node, "writeOnly") ?? false;
                            ApplyReference30(target, inner, ParseNullable(resolved.Node));
                        }
                    }
                    else
                    {
                        FillAssertion(target, resolved.Node);
                    }
                }
            }
            finally
            {
                _currentDocumentId = previousDocumentId;
            }

            return target;
        }

        private void FillAssertion(OpenApiSemanticAssertion result, SpecNode node)
        {
            result.ReadOnly = GetOptionalBoolean(node, "readOnly") ?? false;
            result.WriteOnly = GetOptionalBoolean(node, "writeOnly") ?? false;

            SpecNode? typeNode = GetProperty(node, "type");
            if (typeNode is not null)
            {
                OpenApiSemanticAssertionTypes types = ParseAssertionTypes(typeNode);
                if (_minorVersion == 0 && ParseNullable(node))
                {
                    // 3.0.3: nullable adds null to the type, and only when the same schema declares a type.
                    types |= OpenApiSemanticAssertionTypes.Null;
                }

                result.Types = types;
            }

            SpecNode? enumNode = GetProperty(node, "enum");
            if (enumNode is not null)
            {
                RequireKind(enumNode, SpecValueKind.Array, "The schema enum field must be an array.");
                result.Enum = enumNode.EnumerateArray().Select(ToAssertionValue).ToArray();
            }

            SpecNode? constNode = _minorVersion == 1 ? GetProperty(node, "const") : null;
            if (constNode is not null)
            {
                result.Const = ToAssertionValue(constNode);
            }

            result.Format = GetOptionalString(node, "format") ?? string.Empty;
            result.Constraints = ParseAssertionScalars(node);

            SpecNode? propertiesNode = GetProperty(node, "properties");
            if (propertiesNode is not null)
            {
                RequireKind(propertiesNode, SpecValueKind.Object, "The schema properties field must be an object.");
                var properties = new List<OpenApiSemanticAssertionProperty>();
                foreach (SpecProperty property in propertiesNode.EnumerateObject())
                {
                    OpenApiSemanticAssertion child = MapAssertion(property.Value);
                    properties.Add(new OpenApiSemanticAssertionProperty(
                        property.Name, child, child.EffectiveReadOnly, child.EffectiveWriteOnly));
                }

                result.Properties = properties;
            }

            SpecNode? requiredNode = GetProperty(node, "required");
            if (requiredNode is not null)
            {
                result.Required = ParseStringArray(requiredNode, "Required property names must be strings.");
            }

            SpecNode? additionalNode = GetProperty(node, "additionalProperties");
            if (additionalNode is not null)
            {
                if (additionalNode.ValueKind == SpecValueKind.True)
                {
                    result.AdditionalProperties = OpenApiSemanticAdditionalProperties.Open;
                }
                else if (additionalNode.ValueKind == SpecValueKind.False)
                {
                    result.AdditionalProperties = OpenApiSemanticAdditionalProperties.Closed;
                }
                else
                {
                    result.AdditionalProperties = OpenApiSemanticAdditionalProperties.Typed;
                    result.AdditionalPropertiesSchema = MapAssertion(additionalNode);
                }
            }

            SpecNode? itemsNode = GetProperty(node, "items");
            if (itemsNode is not null)
            {
                result.Items = MapAssertion(itemsNode);
            }

            result.AllOf = MapAssertionList(node, "allOf");
            result.AnyOf = MapAssertionList(node, "anyOf");
            result.OneOf = MapAssertionList(node, "oneOf");
            SpecNode? notNode = GetProperty(node, "not");
            if (notNode is not null)
            {
                result.Not = MapAssertion(notNode);
            }

            if (_minorVersion == 1)
            {
                FillApplicators(result, node);
            }
        }

        private void FillApplicators(OpenApiSemanticAssertion result, SpecNode node)
        {
            SpecNode? patternPropertiesNode = GetProperty(node, "patternProperties");
            if (patternPropertiesNode is not null)
            {
                RequireKind(
                    patternPropertiesNode,
                    SpecValueKind.Object,
                    "The schema 'patternProperties' field must be an object.");
                var entries = new List<OpenApiSemanticAssertionPatternProperty>();
                foreach (SpecProperty entry in patternPropertiesNode.EnumerateObject())
                {
                    bool ecmaScript = CompilePattern(entry.Value, entry.Name, "patternProperties");
                    entries.Add(new OpenApiSemanticAssertionPatternProperty(
                        entry.Name, ecmaScript, MapAssertion(entry.Value)));
                }

                result.PatternProperties = entries;
            }

            SpecNode? propertyNamesNode = GetProperty(node, "propertyNames");
            if (propertyNamesNode is not null)
            {
                result.PropertyNames = MapAssertion(propertyNamesNode);
            }

            SpecNode? containsNode = GetProperty(node, "contains");
            if (containsNode is not null)
            {
                result.Contains = MapAssertion(containsNode);
                result.MinContains = ReadCount(node, "minContains");
                result.MaxContains = ReadCount(node, "maxContains");
            }

            SpecNode? prefixNode = GetProperty(node, "prefixItems");
            if (prefixNode is not null)
            {
                RequireKind(prefixNode, SpecValueKind.Array, "The schema 'prefixItems' field must be an array of schemas.");
                result.PrefixItems = prefixNode.EnumerateArray().Select(MapAssertion).ToArray();
            }

            SpecNode? dependentNode = GetProperty(node, "dependentRequired");
            if (dependentNode is not null)
            {
                RequireKind(dependentNode, SpecValueKind.Object, "The schema 'dependentRequired' field must be an object.");
                result.DependentRequired = dependentNode.EnumerateObject()
                    .Select(entry => new OpenApiSemanticDependentRequired(
                        entry.Name,
                        ParseStringArray(
                            entry.Value,
                            "The dependentRequired value of '" + entry.Name + "' must be an array of strings.")))
                    .ToArray();
            }
        }

        /// <summary>
        /// A node that owns a shape and has oneOf / anyOf next to it, and that is open (additionalProperties is not false):
        /// the properties its alternatives declare and the node does not become optional properties of the node, typed when
        /// every alternative that says a type agrees (otherwise untyped). The alternatives themselves stay assertions. A
        /// declaration without a type (only an enum or constraints, such as `{enum: [null]}`) adds no type information.
        /// A closed node gets no new property: the alternatives can only select among the declared ones.
        /// </summary>
        private OpenApiSemanticSchema LiftAlternativeProperties(SpecNode node, OpenApiSemanticSchema shape, string suggestedName)
        {
            if (shape.Kind != OpenApiSemanticSchemaKind.Object ||
                shape.AdditionalProperties == OpenApiSemanticAdditionalProperties.Closed)
            {
                return shape;
            }

            var declared = new HashSet<string>(shape.Properties.Select(static property => property.WireName), StringComparer.Ordinal);
            var order = new List<string>();
            var candidates = new Dictionary<string, List<SpecNode>>(StringComparer.Ordinal);
            foreach (string keyword in new[] { "oneOf", "anyOf" })
            {
                SpecNode? listNode = GetProperty(node, keyword);
                if (listNode is null || listNode.ValueKind != SpecValueKind.Array)
                {
                    continue;
                }

                foreach (SpecNode member in listNode.EnumerateArray())
                {
                    SpecNode? body = member;
                    if (TryGetReference(member, out string reference, out SpecNode referenceNode))
                    {
                        ResolvedSpecReference resolved = ResolveReference(reference, referenceNode);
                        body = string.Equals(resolved.DocumentId, _currentDocumentId, StringComparison.Ordinal) ? resolved.Node : null;
                    }

                    SpecNode? propertiesNode = body is not null && body.ValueKind == SpecValueKind.Object
                        ? GetProperty(body, "properties")
                        : null;
                    if (propertiesNode is null || propertiesNode.ValueKind != SpecValueKind.Object)
                    {
                        continue;
                    }

                    foreach (SpecProperty property in propertiesNode.EnumerateObject())
                    {
                        if (declared.Contains(property.Name) || property.Value.ValueKind != SpecValueKind.Object)
                        {
                            continue;
                        }

                        if (!candidates.TryGetValue(property.Name, out List<SpecNode>? list))
                        {
                            list = new List<SpecNode>();
                            candidates.Add(property.Name, list);
                            order.Add(property.Name);
                        }

                        list.Add(property.Value);
                    }
                }
            }

            var lifted = new List<OpenApiSemanticProperty>();
            foreach (string name in order)
            {
                SpecNode[] typed = candidates[name]
                    .Where(static candidate => candidate.TryGetProperty("type", out _) || candidate.TryGetProperty("$ref", out _))
                    .ToArray();
                if (typed.Length == 0)
                {
                    continue;
                }

                OpenApiSemanticSchema[] parsed;
                try
                {
                    parsed = typed
                        .Select(candidate => ParseSchema(
                            candidate,
                            suggestedName + ToPascalFragment(name),
                            new HashSet<NormalizedSpecNodeIdentity>()))
                        .ToArray();
                }
                catch (OpenApiSemanticException)
                {
                    // A declaration the typed model cannot hold: the property stays untyped (extension data).
                    continue;
                }

                string signature = GetSchemaSignature(parsed[0].WithNullable(false));
                bool agree = parsed.All(item => string.Equals(
                    GetSchemaSignature(item.WithNullable(false)), signature, StringComparison.Ordinal));
                // Nullable: an alternative may pin the property to null, which the assertions then allow or reject.
                OpenApiSemanticSchema chosen = agree
                    ? parsed[0].WithNullable(true)
                    : CreateAnySchema(typed[0], suggestedName + ToPascalFragment(name), OpenApiSemanticAnnotations.Empty);
                lifted.Add(new OpenApiSemanticProperty(name, false, chosen, CreateLocation(typed[0])));
            }

            return lifted.Count == 0 ? shape : shape.WithProperties(shape.Properties.Concat(lifted).ToArray());
        }

        private string AssertionOrigin(SpecNode node, string tag)
        {
            return _currentDocumentId + "|" + node.LogicalPath + "|" + tag;
        }

        /// <summary>The enum of a schema whose type is not a C# enum (boolean, number, null only, ...): only the values are asserted.</summary>
        private OpenApiSemanticAssertion CreateEnumAssertion(SpecNode node, SpecNode enumNode)
        {
            return new OpenApiSemanticAssertion
            {
                Enum = enumNode.EnumerateArray().Select(ToAssertionValue).ToArray(),
                Origin = AssertionOrigin(node, "enum"),
                Location = CreateLocation(node)
            };
        }

        /// <summary>The oneOf / anyOf next to the shape of a schema: the type is the shape, the alternatives are asserted.</summary>
        private OpenApiSemanticAssertion CreateAlternativesAssertion(SpecNode node)
        {
            return new OpenApiSemanticAssertion
            {
                AnyOf = MapAssertionList(node, "anyOf"),
                OneOf = MapAssertionList(node, "oneOf"),
                Origin = AssertionOrigin(node, "alternatives"),
                Location = CreateLocation(node)
            };
        }

        /// <summary>
        /// The whole schema as written (allOf parts, oneOf / anyOf, not, required, ...). Used where the typed shape is only
        /// an upper bound of the valid values. A wrapper, so that the shared node of a $ref keeps its own origin.
        /// </summary>
        private OpenApiSemanticAssertion CreateWholeAssertion(SpecNode node)
        {
            return new OpenApiSemanticAssertion
            {
                AllOf = new[] { MapAssertion(node) },
                Origin = AssertionOrigin(node, "whole"),
                Location = CreateLocation(node)
            };
        }

        private OpenApiSemanticAssertion CreateNotAssertion(SpecNode node, SpecNode notNode)
        {
            return new OpenApiSemanticAssertion
            {
                Not = MapAssertion(notNode),
                Origin = AssertionOrigin(node, "not"),
                Location = CreateLocation(node)
            };
        }

        private IReadOnlyList<OpenApiSemanticAssertion> MapAssertionList(SpecNode node, string keyword)
        {
            SpecNode? listNode = GetProperty(node, keyword);
            if (listNode is null)
            {
                return Array.Empty<OpenApiSemanticAssertion>();
            }

            RequireKind(listNode, SpecValueKind.Array, "The schema '" + keyword + "' field must be an array.");
            return listNode.EnumerateArray().Select(MapAssertion).ToArray();
        }

        private OpenApiSemanticAssertionTypes ParseAssertionTypes(SpecNode typeNode)
        {
            if (typeNode.ValueKind == SpecValueKind.String)
            {
                return AssertionTypeOf(typeNode, typeNode.GetString() ?? string.Empty);
            }

            if (_minorVersion != 1 || typeNode.ValueKind != SpecValueKind.Array)
            {
                throw Invalid(typeNode, "The schema type must be a string, or a type array in OpenAPI 3.1.");
            }

            IReadOnlyList<string> names = ParseStringArray(typeNode, "OpenAPI 3.1 type union entries must be strings.");
            if (names.Count == 0)
            {
                throw Invalid(typeNode, "The schema type array must not be empty.");
            }

            OpenApiSemanticAssertionTypes types = OpenApiSemanticAssertionTypes.None;
            foreach (string name in names)
            {
                types |= AssertionTypeOf(typeNode, name);
            }

            return types;
        }

        private static OpenApiSemanticAssertionTypes AssertionTypeOf(SpecNode typeNode, string name)
        {
            switch (name)
            {
                case "null":
                    return OpenApiSemanticAssertionTypes.Null;
                case "boolean":
                    return OpenApiSemanticAssertionTypes.Boolean;
                case "integer":
                    return OpenApiSemanticAssertionTypes.Integer;
                case "number":
                    return OpenApiSemanticAssertionTypes.Number;
                case "string":
                    return OpenApiSemanticAssertionTypes.String;
                case "array":
                    return OpenApiSemanticAssertionTypes.Array;
                case "object":
                    return OpenApiSemanticAssertionTypes.Object;
                default:
                    throw Invalid(typeNode, "The schema type '" + name + "' is not a JSON Schema type.");
            }
        }

        private static OpenApiSemanticAssertionValue ToAssertionValue(SpecNode node)
        {
            OpenApiSemanticAssertionValueKind kind;
            switch (node.ValueKind)
            {
                case SpecValueKind.Null:
                    kind = OpenApiSemanticAssertionValueKind.Null;
                    break;
                case SpecValueKind.True:
                case SpecValueKind.False:
                    kind = OpenApiSemanticAssertionValueKind.Boolean;
                    break;
                case SpecValueKind.String:
                    kind = OpenApiSemanticAssertionValueKind.String;
                    break;
                case SpecValueKind.Number:
                    kind = node.IsInteger
                        ? OpenApiSemanticAssertionValueKind.Integer
                        : OpenApiSemanticAssertionValueKind.Number;
                    break;
                case SpecValueKind.Array:
                    kind = OpenApiSemanticAssertionValueKind.Array;
                    break;
                default:
                    kind = OpenApiSemanticAssertionValueKind.Object;
                    break;
            }

            return new OpenApiSemanticAssertionValue(kind, node.GetRawJson());
        }

        // The keywords that carry no schema; the others are fields of the assertion node.
        private OpenApiSemanticConstraints? ParseAssertionScalars(SpecNode node)
        {
            var set = new OpenApiSemanticConstraints
            {
                MinLength = ReadCount(node, "minLength"),
                MaxLength = ReadCount(node, "maxLength")
            };
            SpecNode? patternNode = GetProperty(node, "pattern");
            if (patternNode is not null)
            {
                string pattern = RequireString(patternNode, "The schema 'pattern' field must be a string.");
                set.Pattern = pattern;
                set.PatternEcmaScript = CompilePattern(patternNode, pattern, "pattern");
            }

            ParseNumberBounds(node, set);
            set.MinItems = ReadCount(node, "minItems");
            set.MaxItems = ReadCount(node, "maxItems");
            SpecNode? uniqueNode = GetProperty(node, "uniqueItems");
            if (uniqueNode is not null)
            {
                set.UniqueItems = RequireBoolean(uniqueNode, "The schema 'uniqueItems' field must be a boolean.");
            }

            set.MinProperties = ReadCount(node, "minProperties");
            set.MaxProperties = ReadCount(node, "maxProperties");
            return set.IsEmpty ? null : set;
        }
    }
}
