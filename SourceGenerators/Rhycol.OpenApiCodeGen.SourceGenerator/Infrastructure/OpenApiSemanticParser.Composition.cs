using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Schema composition: allOf (flattened into one DTO), oneOf / anyOf with and without a
    /// discriminator, the 3.1 $ref + siblings form, and not (response validation only).
    /// </summary>
    internal sealed partial class OpenApiSemanticParser
    {
        private const string ComponentSchemaPrefix = "/components/schemas/";

        // Keywords that may sit next to oneOf / anyOf without changing what the union means.
        private static readonly HashSet<string> UnionAllowedSiblings =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "$comment", "$defs", "$schema", "anyOf", "default", "deprecated", "description",
                "discriminator", "example", "examples", "externalDocs", "nullable", "not", "oneOf",
                "readOnly", "summary", "title", "type", "writeOnly", "xml"
            };

        // Keywords that give a schema its own shape. Next to a single allOf: [ { $ref } ] they
        // turn the idiom into a real composition.
        private static readonly HashSet<string> StructuralShapeKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "properties", "required", "additionalProperties", "items", "enum", "const", "format",
                "oneOf", "anyOf", "contentEncoding", "contentMediaType"
            };

        // Recursion detection while a not schema is parsed (see ApplyNotKeyword).
        private int _notDepth;
        private bool _notRecursion;

        private readonly List<OpenApiSemanticSchema> _discriminatedUnions = new List<OpenApiSemanticSchema>();
        private Dictionary<string, List<string>>? _inheritanceChildren;

        private static bool HasUnionKeyword(SpecNode node)
        {
            return node.TryGetProperty("oneOf", out _) || node.TryGetProperty("anyOf", out _);
        }

        /// <summary>
        /// OpenAPI 3.1: a $ref next to keywords that describe a shape is an allOf of the target and
        /// those keywords. Annotations (and not) next to a $ref keep the plain reference form.
        /// </summary>
        private bool HasStructuralReferenceSiblings(SpecNode node)
        {
            foreach (SpecProperty property in node.EnumerateObject())
            {
                // type alone does not add structure; it only restates the target.
                if (property.Name == "$ref" ||
                    property.Name.StartsWith("x-", StringComparison.Ordinal) ||
                    AllowedReferenceSchemaSiblings.Contains(property.Name) ||
                    property.Name == "not" ||
                    property.Name == "type")
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private bool ParseCompositionNullable(SpecNode node)
        {
            bool nullable = ParseNullable(node);
            SpecNode? typeNode = GetProperty(node, "type");
            if (typeNode is not null && _minorVersion == 1)
            {
                if (typeNode.ValueKind == SpecValueKind.Array)
                {
                    nullable |= typeNode.EnumerateArray().Any(static item =>
                        item.ValueKind == SpecValueKind.String &&
                        string.Equals(item.GetString(), "null", StringComparison.Ordinal));
                }
            }

            return nullable;
        }

        private static OpenApiSemanticAnnotations MergeAnnotations(
            OpenApiSemanticAnnotations outer,
            OpenApiSemanticAnnotations inner)
        {
            return new OpenApiSemanticAnnotations(
                string.IsNullOrWhiteSpace(outer.Description) ? inner.Description : outer.Description,
                outer.Deprecated || inner.Deprecated,
                outer.ReadOnly || inner.ReadOnly,
                outer.WriteOnly || inner.WriteOnly,
                outer.Default ?? inner.Default,
                inner.Const);
        }

        // ---- oneOf / anyOf ----------------------------------------------------------------------

        private OpenApiSemanticSchema ParseUnion(
            SpecNode node,
            string suggestedName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            SpecNode? oneOfNode = GetProperty(node, "oneOf");
            SpecNode? anyOfNode = GetProperty(node, "anyOf");
            if (node.TryGetProperty("allOf", out SpecNode allOfNode))
            {
                throw Unsupported(
                    allOfNode,
                    "allOf cannot be combined with oneOf or anyOf in the same schema by the Phase 4 MVP.");
            }

            if (oneOfNode is not null && anyOfNode is not null)
            {
                throw Unsupported(
                    anyOfNode,
                    "oneOf and anyOf cannot be combined in the same schema by the Phase 4 MVP.");
            }

            bool isOneOf = oneOfNode is not null;
            string keyword = isOneOf ? "oneOf" : "anyOf";
            SpecNode listNode = (oneOfNode ?? anyOfNode)!;
            RequireKind(listNode, SpecValueKind.Array, "The schema '" + keyword + "' field must be an array.");
            ValidateUnionSiblings(node, keyword);
            SpecNode[] parts = listNode.EnumerateArray().ToArray();
            if (parts.Length == 0)
            {
                throw Invalid(listNode, "The schema '" + keyword + "' array must not be empty.");
            }

            bool nullable = ParseCompositionNullable(node);
            var memberNodes = new List<SpecNode>();
            foreach (SpecNode part in parts)
            {
                if (IsNullTypeSchema(part))
                {
                    nullable = true;
                }
                else
                {
                    memberNodes.Add(part);
                }
            }

            if (memberNodes.Count == 0)
            {
                throw Unsupported(
                    listNode,
                    "The schema '" + keyword + "' must contain at least one schema other than null.");
            }

            OpenApiSemanticAnnotations annotations = ParseAnnotations(node, allowConst: false);
            SpecNode? discriminatorNode = GetProperty(node, "discriminator");
            if (memberNodes.Count == 1 && discriminatorNode is null)
            {
                // [X, null] (or a single candidate) is not a union: it is X, possibly nullable. It
                // is an alias, so it shares the alias stack and a cycle through it stays an error.
                OpenApiSemanticSchema single = ParseSchema(memberNodes[0], suggestedName, referenceStack);
                return single
                    .WithNullable(single.Nullable || nullable)
                    .WithAnnotations(MergeAnnotations(annotations, single.Annotations));
            }

            // Members are a container boundary: the union wrapper is a named type, so a member
            // that refers back to an enclosing schema is recursion, not an alias cycle.
            var members = new List<OpenApiSemanticSchema>(memberNodes.Count);
            for (int index = 0; index < memberNodes.Count; index++)
            {
                members.Add(ParseSchema(
                    memberNodes[index],
                    suggestedName + "Option" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new HashSet<NormalizedSpecNodeIdentity>()));
            }

            OpenApiSemanticDiscriminator? discriminator = null;
            if (discriminatorNode is not null)
            {
                discriminator = ParseDiscriminator(discriminatorNode, memberNodes, members, keyword);
            }
            else
            {
                nullable |= members.Any(member => IsSchemaNullable(member));
            }

            var union = new OpenApiSemanticSchema(
                OpenApiSemanticSchemaKind.Union,
                suggestedName,
                string.Empty,
                nullable,
                string.Empty,
                Array.Empty<OpenApiSemanticProperty>(),
                null,
                Array.Empty<string>(),
                CreateLocation(node),
                annotations: annotations,
                unionMembers: members,
                discriminator: discriminator,
                unionIsOneOf: isOneOf);
            if (discriminator is not null)
            {
                _discriminatedUnions.Add(union);
            }

            return union;
        }

        private static bool IsNullTypeSchema(SpecNode part)
        {
            return part.ValueKind == SpecValueKind.Object &&
                   !part.TryGetProperty("$ref", out _) &&
                   part.TryGetProperty("type", out SpecNode type) &&
                   type.ValueKind == SpecValueKind.String &&
                   string.Equals(type.GetString(), "null", StringComparison.Ordinal);
        }

        private static void ValidateUnionSiblings(SpecNode node, string keyword)
        {
            foreach (SpecProperty property in node.EnumerateObject())
            {
                if (UnionAllowedSiblings.Contains(property.Name) ||
                    property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                throw Unsupported(
                    property.Value,
                    "Schema keyword '" + property.Name + "' cannot be combined with " + keyword +
                    " by the Phase 4 MVP. Move it into the union members.");
            }
        }

        private OpenApiSemanticDiscriminator ParseDiscriminator(
            SpecNode discriminatorNode,
            IReadOnlyList<SpecNode> memberNodes,
            IReadOnlyList<OpenApiSemanticSchema> members,
            string keyword)
        {
            string propertyName = ReadDiscriminatorPropertyName(discriminatorNode);
            for (int index = 0; index < members.Count; index++)
            {
                if (members[index].Kind != OpenApiSemanticSchemaKind.Reference)
                {
                    throw Unsupported(
                        memberNodes[index],
                        "The members of a discriminated " + keyword +
                        " must be $ref schemas so that their discriminator values can be resolved.");
                }
            }

            var values = new List<string>[members.Count];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = new List<string>();
            }

            SpecNode? mappingNode = GetProperty(discriminatorNode, "mapping");
            if (mappingNode is not null)
            {
                RequireKind(mappingNode, SpecValueKind.Object, "The discriminator mapping field must be an object.");
                foreach (SpecProperty entry in mappingNode.EnumerateObject())
                {
                    string target = RequireString(entry.Value, "Discriminator mapping values must be strings.");
                    int memberIndex = ResolveMappingMember(target, entry.Value, memberNodes, members);
                    if (memberIndex < 0)
                    {
                        throw Unsupported(
                            entry.Value,
                            "The discriminator mapping value '" + target + "' does not refer to a member of the " +
                            keyword + ".");
                    }

                    values[memberIndex].Add(entry.Name);
                }
            }

            for (int index = 0; index < values.Length; index++)
            {
                if (values[index].Count == 0)
                {
                    // Without an explicit mapping the schema name is the discriminator value.
                    values[index].Add(members[index].ReferenceName);
                }
            }

            EnsureUniqueDiscriminatorValues(discriminatorNode, values);
            return new OpenApiSemanticDiscriminator(
                propertyName,
                values.Select(static list => (IReadOnlyList<string>)list.ToArray()).ToArray());
        }

        private static string ReadDiscriminatorPropertyName(SpecNode discriminatorNode)
        {
            RequireKind(discriminatorNode, SpecValueKind.Object, "The discriminator field must be an object.");
            SpecNode nameNode = RequireProperty(discriminatorNode, "propertyName");
            string propertyName = RequireString(nameNode, "The discriminator propertyName must be a string.");
            if (propertyName.Length == 0)
            {
                throw Invalid(nameNode, "The discriminator propertyName must not be empty.");
            }

            return propertyName;
        }

        private static void EnsureUniqueDiscriminatorValues(SpecNode discriminatorNode, List<string>[] values)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (List<string> memberValues in values)
            {
                foreach (string value in memberValues)
                {
                    if (!seen.Add(value))
                    {
                        throw Invalid(
                            discriminatorNode,
                            "The discriminator value '" + value + "' selects more than one schema.");
                    }
                }
            }
        }

        /// <summary>
        /// A mapping value is a schema name or a reference. It is matched against the $ref strings
        /// of the members first, then resolved as an internal pointer; anything that points into
        /// another file is not supported.
        /// </summary>
        private int ResolveMappingMember(
            string target,
            SpecNode mappingValueNode,
            IReadOnlyList<SpecNode> memberNodes,
            IReadOnlyList<OpenApiSemanticSchema> members)
        {
            for (int index = 0; index < memberNodes.Count; index++)
            {
                if (TryGetReference(memberNodes[index], out string memberReference, out _) &&
                    string.Equals(memberReference, target, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            ResolvedSpecReference resolved = ResolveMappingTarget(target, mappingValueNode, out _);
            for (int index = 0; index < members.Count; index++)
            {
                if (members[index].ReferenceIdentity.Equals(resolved.Identity))
                {
                    return index;
                }
            }

            return -1;
        }

        private ResolvedSpecReference ResolveMappingTarget(
            string target,
            SpecNode mappingValueNode,
            out string schemaName)
        {
            string pointer;
            if (target.IndexOf('#') < 0 && target.IndexOf('/') < 0 && !HasFileExtension(target))
            {
                pointer = ComponentSchemaPrefix + EncodePointerToken(target);
            }
            else if (target.StartsWith("#/", StringComparison.Ordinal))
            {
                pointer = target.Substring(1);
            }
            else
            {
                throw Unsupported(
                    mappingValueNode,
                    "The discriminator mapping value '" + target +
                    "' points outside the current document; only internal references and schema names are supported.");
            }

            return ResolveInternalSchemaPointer(pointer, mappingValueNode, out schemaName);
        }

        private static bool HasFileExtension(string value)
        {
            return value.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        // A schema that is named by a pointer instead of a $ref node has no reference edge in a v2
        // bundle, so it is resolved inside the current document directly.
        private ResolvedSpecReference ResolveInternalSchemaPointer(
            string pointer,
            SpecNode node,
            out string schemaName)
        {
            ResolvedSpecReference resolved = _resolver.ResolveInternalPointer(_currentDocumentId, pointer, node);
            schemaName = JsonPointerResolver.GetSchemaPointerName(resolved.Pointer, node);
            return resolved;
        }

        /// <summary>
        /// Members of a discriminated union are dispatched by their discriminator value and
        /// deserialized as DTOs, so each one has to resolve to an object schema. Checked after
        /// parsing because a member can still be in progress while the union is created.
        /// </summary>
        private void ValidateDiscriminatedUnionMembers()
        {
            foreach (OpenApiSemanticSchema union in _discriminatedUnions)
            {
                foreach (OpenApiSemanticSchema member in union.UnionMembers)
                {
                    OpenApiSemanticSchema effective = GetEffectiveSchema(member);
                    if (effective.Kind != OpenApiSemanticSchemaKind.Object)
                    {
                        throw new OpenApiSemanticException(
                            OpenApiSemanticErrorKind.UnsupportedElement,
                            "The member '" + member.ReferenceName +
                            "' of a discriminated union must be an object schema with declared properties.",
                            member.Location,
                            Array.Empty<OpenApiSourceLocation>());
                    }
                }
            }
        }

        // ---- discriminator on a base schema (inheritance) -------------------------------------------

        /// <summary>
        /// A schema with a discriminator and no oneOf / anyOf is the base of an inheritance
        /// hierarchy: its subtypes are the mapping targets and the schemas that list it in allOf.
        /// It becomes a union over those subtypes (its own properties reach them through allOf).
        /// Returns null, with a warning, when there is no subtype.
        /// </summary>
        private OpenApiSemanticSchema? TryParseInheritanceUnion(
            SpecNode node,
            string suggestedName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            SpecNode discriminatorNode = RequireProperty(node, "discriminator");
            string propertyName = ReadDiscriminatorPropertyName(discriminatorNode);
            var ownIdentity = new NormalizedSpecNodeIdentity(_currentDocumentId, node.LogicalPath);
            var candidates = new List<(ResolvedSpecReference Resolved, string Name, SpecNode Node)>();
            var explicitValues = new Dictionary<NormalizedSpecNodeIdentity, List<string>>();

            SpecNode? mappingNode = GetProperty(discriminatorNode, "mapping");
            if (mappingNode is not null)
            {
                RequireKind(mappingNode, SpecValueKind.Object, "The discriminator mapping field must be an object.");
                foreach (SpecProperty entry in mappingNode.EnumerateObject())
                {
                    string target = RequireString(entry.Value, "Discriminator mapping values must be strings.");
                    ResolvedSpecReference resolved = ResolveMappingTarget(target, entry.Value, out string name);
                    if (resolved.Identity.Equals(ownIdentity))
                    {
                        AddIgnoredWarning(
                            entry.Value,
                            "A discriminator mapping to the schema that declares the discriminator is not a subtype and was ignored.");
                        continue;
                    }

                    if (!candidates.Any(candidate => candidate.Resolved.Identity.Equals(resolved.Identity)))
                    {
                        candidates.Add((resolved, name, entry.Value));
                    }

                    if (!explicitValues.TryGetValue(resolved.Identity, out List<string>? list))
                    {
                        list = new List<string>();
                        explicitValues.Add(resolved.Identity, list);
                    }

                    list.Add(entry.Name);
                }
            }

            if (TryGetComponentSchemaName(node, out string componentName))
            {
                foreach (string child in GetInheritanceDescendants(componentName))
                {
                    ResolvedSpecReference resolved = ResolveInternalSchemaPointer(
                        ComponentSchemaPrefix + EncodePointerToken(child),
                        discriminatorNode,
                        out string name);
                    if (!resolved.Identity.Equals(ownIdentity) &&
                        !candidates.Any(candidate => candidate.Resolved.Identity.Equals(resolved.Identity)))
                    {
                        candidates.Add((resolved, name, discriminatorNode));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                AddIgnoredWarning(
                    discriminatorNode,
                    "A discriminator on a schema that has no oneOf / anyOf and no allOf subtypes has nothing to select and was ignored.");
                return null;
            }

            var members = new List<OpenApiSemanticSchema>(candidates.Count);
            var values = new List<string>[candidates.Count];
            for (int index = 0; index < candidates.Count; index++)
            {
                (ResolvedSpecReference resolved, string name, SpecNode locationNode) = candidates[index];
                members.Add(BuildReferenceSchema(
                    locationNode,
                    suggestedName + "Option" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new HashSet<NormalizedSpecNodeIdentity>(),
                    "#" + resolved.Pointer,
                    locationNode,
                    resolved,
                    name,
                    false,
                    OpenApiSemanticAnnotations.Empty));
                values[index] = explicitValues.TryGetValue(resolved.Identity, out List<string>? mapped)
                    ? mapped
                    : new List<string> { name };
            }

            EnsureUniqueDiscriminatorValues(discriminatorNode, values);
            var union = new OpenApiSemanticSchema(
                OpenApiSemanticSchemaKind.Union,
                suggestedName,
                string.Empty,
                ParseCompositionNullable(node),
                string.Empty,
                Array.Empty<OpenApiSemanticProperty>(),
                null,
                Array.Empty<string>(),
                CreateLocation(node),
                annotations: ParseAnnotations(node, allowConst: false),
                unionMembers: members,
                discriminator: new OpenApiSemanticDiscriminator(
                    propertyName,
                    values.Select(static list => (IReadOnlyList<string>)list.ToArray()).ToArray()),
                unionIsOneOf: true);
            _discriminatedUnions.Add(union);
            return union;
        }

        private static bool TryGetComponentSchemaName(SpecNode node, out string name)
        {
            string path = node.LogicalPath;
            if (path.StartsWith(ComponentSchemaPrefix, StringComparison.Ordinal) &&
                path.IndexOf('/', ComponentSchemaPrefix.Length) < 0)
            {
                name = path.Substring(ComponentSchemaPrefix.Length).Replace("~1", "/").Replace("~0", "~");
                return name.Length > 0;
            }

            name = string.Empty;
            return false;
        }

        /// <summary>
        /// Schemas of the entry document that list the named schema in allOf, transitively. Only
        /// direct internal references are recognised; other documents are not scanned.
        /// </summary>
        private IReadOnlyList<string> GetInheritanceDescendants(string name)
        {
            if (!string.Equals(_currentDocumentId, "root", StringComparison.Ordinal))
            {
                return Array.Empty<string>();
            }

            if (_inheritanceChildren is null)
            {
                _inheritanceChildren = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                SpecNode? components = GetProperty(_root, "components");
                SpecNode? schemas = components is null ? null : GetProperty(components, "schemas");
                if (schemas is not null && schemas.ValueKind == SpecValueKind.Object)
                {
                    foreach (SpecProperty schema in schemas.EnumerateObject()
                                 .OrderBy(static value => value.Name, StringComparer.Ordinal))
                    {
                        if (schema.Value.ValueKind != SpecValueKind.Object ||
                            !schema.Value.TryGetProperty("allOf", out SpecNode allOf) ||
                            allOf.ValueKind != SpecValueKind.Array)
                        {
                            continue;
                        }

                        foreach (SpecNode part in allOf.EnumerateArray())
                        {
                            if (part.ValueKind != SpecValueKind.Object ||
                                !part.TryGetProperty("$ref", out SpecNode referenceNode) ||
                                referenceNode.ValueKind != SpecValueKind.String)
                            {
                                continue;
                            }

                            string reference = referenceNode.GetString() ?? string.Empty;
                            if (!reference.StartsWith("#" + ComponentSchemaPrefix, StringComparison.Ordinal) ||
                                reference.IndexOf('/', ComponentSchemaPrefix.Length + 1) >= 0)
                            {
                                continue;
                            }

                            string parent = reference.Substring(ComponentSchemaPrefix.Length + 1)
                                .Replace("~1", "/").Replace("~0", "~");
                            if (!_inheritanceChildren.TryGetValue(parent, out List<string>? children))
                            {
                                children = new List<string>();
                                _inheritanceChildren.Add(parent, children);
                            }

                            children.Add(schema.Name);
                        }
                    }
                }
            }

            var result = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { name };
            var queue = new Queue<string>();
            queue.Enqueue(name);
            while (queue.Count > 0)
            {
                if (!_inheritanceChildren.TryGetValue(queue.Dequeue(), out List<string>? children))
                {
                    continue;
                }

                foreach (string child in children)
                {
                    if (visited.Add(child))
                    {
                        result.Add(child);
                        queue.Enqueue(child);
                    }
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        // ---- allOf --------------------------------------------------------------------------------

        private OpenApiSemanticSchema ParseAllOf(
            SpecNode node,
            string suggestedName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            SpecNode allOfNode = RequireProperty(node, "allOf");
            RequireKind(allOfNode, SpecValueKind.Array, "The schema allOf field must be an array.");
            SpecNode[] parts = allOfNode.EnumerateArray().ToArray();
            if (parts.Length == 0)
            {
                throw Invalid(allOfNode, "The schema allOf array must not be empty.");
            }

            if (parts.Length == 1 && IsSingleReferenceIdiom(node, parts[0]))
            {
                // allOf: [ { $ref } ] with a description / nullable next to it is the classic way
                // to annotate a reference. It is a reference: no new type is created.
                TryGetReference(parts[0], out string reference, out SpecNode referenceNode);
                OpenApiSemanticAnnotations annotations = MergeAnnotations(
                    ParseAnnotations(node, allowConst: false),
                    ParseAnnotations(parts[0], allowConst: false));
                return ParseReferenceSchema(
                    parts[0],
                    suggestedName,
                    referenceStack,
                    reference,
                    referenceNode,
                    ParseCompositionNullable(node) || ParseNullable(parts[0]),
                    annotations);
            }

            return ParseObjectComposition(node, suggestedName, referenceStack);
        }

        private bool IsSingleReferenceIdiom(SpecNode node, SpecNode part)
        {
            if (part.ValueKind != SpecValueKind.Object ||
                !part.TryGetProperty("$ref", out _) ||
                (_minorVersion == 1 && HasStructuralReferenceSiblings(part)))
            {
                return false;
            }

            foreach (SpecProperty property in node.EnumerateObject())
            {
                if (StructuralShapeKeywords.Contains(property.Name))
                {
                    return false;
                }
            }

            return true;
        }

        private OpenApiSemanticSchema ParseObjectComposition(
            SpecNode node,
            string suggestedName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            bool nullable = ParseCompositionNullable(node);
            OpenApiSemanticAnnotations annotations = ParseAnnotations(node, allowConst: false);
            var composition = new ObjectComposition();
            CollectObjectParts(node, suggestedName, referenceStack, composition, isRoot: true);

            string? missing = composition.Required.Keys
                .Where(name => !composition.Properties.ContainsKey(name))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (missing is not null)
            {
                throw Invalid(
                    composition.Required[missing],
                    "Required property '" + missing + "' is not declared in any allOf part.");
            }

            var properties = new List<OpenApiSemanticProperty>();
            foreach (string name in composition.Properties.Keys.OrderBy(static value => value, StringComparer.Ordinal))
            {
                OpenApiSemanticProperty declared = composition.Properties[name];
                properties.Add(new OpenApiSemanticProperty(
                    name,
                    composition.Required.ContainsKey(name),
                    declared.Schema,
                    declared.Location));
            }

            // Closedness is evaluated on the composed object: one closed part closes the whole
            // object over the union of every part's properties (it does not reject the properties
            // of the other parts).
            OpenApiSemanticAdditionalProperties mode;
            OpenApiSemanticSchema? valueSchema = null;
            if (composition.Closed)
            {
                mode = OpenApiSemanticAdditionalProperties.Closed;
            }
            else if (composition.TypedValue is not null)
            {
                mode = OpenApiSemanticAdditionalProperties.Typed;
                valueSchema = composition.TypedValue;
            }
            else
            {
                mode = OpenApiSemanticAdditionalProperties.Open;
            }

            return CreateObjectShape(node, suggestedName, string.Empty, nullable, annotations, properties, mode, valueSchema);
        }

        private void CollectObjectParts(
            SpecNode node,
            string rootName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack,
            ObjectComposition composition,
            bool isRoot)
        {
            if (node.ValueKind == SpecValueKind.True)
            {
                return;
            }

            if (node.ValueKind == SpecValueKind.False)
            {
                throw Unsupported(node, "The 'false' boolean schema cannot be part of an allOf composition.");
            }

            RequireKind(node, SpecValueKind.Object, "A schema must be an object in the Phase 4 MVP.");
            ValidateUnsupportedSchemaKeywords(node);

            // Checked first so that an invalid keyword next to a $ref is reported at the keyword itself,
            // not inside the referenced schema.
            ValidateObjectPartKeywords(node);

            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                if (_minorVersion == 0)
                {
                    ValidateReferenceSchemaSiblings(node);
                }

                ResolvedSpecReference resolved = ResolveSchemaReference(reference, referenceNode, out _);
                ParseResolvedReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    resolved,
                    target =>
                    {
                        CollectObjectParts(target, rootName, referenceStack, composition, isRoot: false);
                        return true;
                    });
            }

            SpecNode? unionNode = GetProperty(node, "oneOf") ?? GetProperty(node, "anyOf");
            if (unionNode is not null &&
                (isRoot || GetProperty(node, "discriminator") is null))
            {
                throw Unsupported(
                    unionNode,
                    "oneOf and anyOf cannot be combined with allOf by the Phase 4 MVP. " +
                    "(A schema with a discriminator is accepted as the base of an allOf subtype.)");
            }

            // A base schema with a discriminator keeps its oneOf / anyOf for itself: a subtype that
            // lists it in allOf only inherits its properties.
            SpecNode? allOfNode = GetProperty(node, "allOf");
            if (allOfNode is not null)
            {
                RequireKind(allOfNode, SpecValueKind.Array, "The schema allOf field must be an array.");
                if (!allOfNode.EnumerateArray().Any())
                {
                    throw Invalid(allOfNode, "The schema allOf array must not be empty.");
                }

                foreach (SpecNode part in allOfNode.EnumerateArray())
                {
                    CollectObjectParts(part, rootName, referenceStack, composition, isRoot: false);
                }
            }

            SpecNode? notNode = GetProperty(node, "not");
            if (!isRoot && notNode is not null)
            {
                AddIgnoredWarning(notNode, "not inside an allOf part is not evaluated and was ignored.");
            }

            CollectOwnObjectKeywords(node, rootName, composition);
        }

        private void ValidateObjectPartKeywords(SpecNode node)
        {
            SpecNode? typeNode = GetProperty(node, "type");
            if (typeNode is not null)
            {
                bool valid;
                if (typeNode.ValueKind == SpecValueKind.String)
                {
                    valid = string.Equals(typeNode.GetString(), "object", StringComparison.Ordinal);
                }
                else if (typeNode.ValueKind == SpecValueKind.Array && _minorVersion == 1)
                {
                    valid = typeNode.EnumerateArray().All(static item =>
                        item.ValueKind == SpecValueKind.String &&
                        (string.Equals(item.GetString(), "object", StringComparison.Ordinal) ||
                         string.Equals(item.GetString(), "null", StringComparison.Ordinal)));
                }
                else
                {
                    throw Invalid(typeNode, "The schema type must be a string, or a type array in OpenAPI 3.1.");
                }

                if (!valid)
                {
                    throw Unsupported(
                        typeNode,
                        "Every part of an allOf composition must be an object schema; this part has a different type.");
                }
            }

            foreach (string keyword in new[] { "items", "enum", "const", "prefixItems" })
            {
                SpecNode? value = GetProperty(node, keyword);
                if (value is not null)
                {
                    throw Unsupported(
                        value,
                        "Every part of an allOf composition must be an object schema; '" + keyword +
                        "' is not supported inside an allOf part.");
                }
            }
        }

        private void CollectOwnObjectKeywords(
            SpecNode node,
            string rootName,
            ObjectComposition composition)
        {
            SpecNode? propertiesNode = GetProperty(node, "properties");
            if (propertiesNode is not null)
            {
                RequireKind(propertiesNode, SpecValueKind.Object, "The schema properties field must be an object.");
                foreach (SpecProperty property in propertiesNode.EnumerateObject()
                             .OrderBy(static value => value.Name, StringComparer.Ordinal))
                {
                    OpenApiSemanticSchema schema = ParseSchema(
                        property.Value,
                        rootName + ToPascalFragment(property.Name),
                        new HashSet<NormalizedSpecNodeIdentity>());
                    OpenApiSourceLocation location = CreateLocation(property.Value);
                    if (composition.Properties.TryGetValue(property.Name, out OpenApiSemanticProperty? existing))
                    {
                        if (!string.Equals(
                                GetSchemaSignature(existing.Schema),
                                GetSchemaSignature(schema),
                                StringComparison.Ordinal))
                        {
                            throw CreateException(
                                OpenApiSemanticErrorKind.UnsupportedElement,
                                "Property '" + property.Name +
                                "' is declared with different types by the parts of an allOf composition.",
                                property.Value,
                                new[] { existing.Location });
                        }

                        continue;
                    }

                    composition.Properties.Add(
                        property.Name,
                        new OpenApiSemanticProperty(property.Name, false, schema, location));
                }
            }

            SpecNode? requiredNode = GetProperty(node, "required");
            if (requiredNode is not null)
            {
                RequireKind(requiredNode, SpecValueKind.Array, "Required property names must be strings.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (SpecNode requiredItem in requiredNode.EnumerateArray())
                {
                    string value = RequireString(requiredItem, "Required property names must be strings.");
                    if (!seen.Add(value))
                    {
                        throw Invalid(
                            requiredItem,
                            "The required array contains the property name '" + value + "' more than once.");
                    }

                    if (!composition.Required.ContainsKey(value))
                    {
                        composition.Required.Add(value, requiredItem);
                    }
                }
            }

            SpecNode? additionalNode = GetProperty(node, "additionalProperties");
            if (additionalNode is null || additionalNode.ValueKind == SpecValueKind.True)
            {
                return;
            }

            if (additionalNode.ValueKind == SpecValueKind.False)
            {
                composition.Closed = true;
            }
            else if (additionalNode.ValueKind == SpecValueKind.Object)
            {
                OpenApiSemanticSchema valueSchema = ParseSchema(
                    additionalNode,
                    rootName + "Value",
                    new HashSet<NormalizedSpecNodeIdentity>());
                if (composition.TypedValue is null)
                {
                    composition.TypedValue = valueSchema;
                    composition.TypedValueLocation = CreateLocation(additionalNode);
                }
                else if (!string.Equals(
                             GetSchemaSignature(composition.TypedValue),
                             GetSchemaSignature(valueSchema),
                             StringComparison.Ordinal))
                {
                    throw CreateException(
                        OpenApiSemanticErrorKind.UnsupportedElement,
                        "The parts of an allOf composition declare different additionalProperties schemas.",
                        additionalNode,
                        new[] { composition.TypedValueLocation });
                }
            }
            else
            {
                throw Invalid(additionalNode, "The additionalProperties field must be a boolean or a schema object.");
            }
        }

        private sealed class ObjectComposition
        {
            internal Dictionary<string, OpenApiSemanticProperty> Properties { get; } =
                new Dictionary<string, OpenApiSemanticProperty>(StringComparer.Ordinal);

            internal Dictionary<string, SpecNode> Required { get; } =
                new Dictionary<string, SpecNode>(StringComparer.Ordinal);

            internal bool Closed { get; set; }

            internal OpenApiSemanticSchema? TypedValue { get; set; }

            internal OpenApiSourceLocation TypedValueLocation { get; set; }
        }

        // ---- not ----------------------------------------------------------------------------------

        /// <summary>
        /// not only takes part in response validation (a value that matches the not schema is
        /// rejected); it never changes the generated type. A not schema that cannot be evaluated
        /// reliably is ignored with a warning: boolean not, a schema without a concrete shape
        /// (for example only required, which would match every value), a recursive one, or one that
        /// uses an unsupported element.
        /// </summary>
        private OpenApiSemanticSchema ApplyNotKeyword(
            SpecNode node,
            string suggestedName,
            OpenApiSemanticSchema schema)
        {
            SpecNode? notNode = GetProperty(node, "not");
            if (notNode is null)
            {
                return schema;
            }

            if (_minorVersion == 1 &&
                (notNode.ValueKind == SpecValueKind.True || notNode.ValueKind == SpecValueKind.False))
            {
                AddIgnoredWarning(notNode, "A boolean not schema is not evaluated and was ignored.");
                return schema;
            }

            RequireKind(notNode, SpecValueKind.Object, "The schema not field must be a schema object.");
            bool previousRecursion = _notRecursion;
            _notRecursion = false;
            _notDepth++;
            OpenApiSemanticSchema? notSchema = null;
            string? failure = null;
            try
            {
                notSchema = ParseSchema(notNode, suggestedName + "Not", new HashSet<NormalizedSpecNodeIdentity>());
            }
            catch (OpenApiSemanticException exception) when (exception.Kind == OpenApiSemanticErrorKind.UnsupportedElement)
            {
                failure = exception.Message;
            }
            finally
            {
                _notDepth--;
            }

            bool recursive = _notRecursion;
            _notRecursion = previousRecursion || recursive;
            if (failure is not null)
            {
                AddIgnoredWarning(
                    notNode,
                    "The not schema uses an element that is not supported (" + failure + ") and was ignored.");
                return schema;
            }

            if (recursive)
            {
                AddIgnoredWarning(notNode, "A recursive not schema is not evaluated and was ignored.");
                return schema;
            }

            if (!IsMeaningfulNotSchema(notSchema!, new HashSet<NormalizedSpecNodeIdentity>()))
            {
                AddIgnoredWarning(
                    notNode,
                    "A not schema without a concrete type or shape would reject every value and was ignored.");
                return schema;
            }

            return schema.WithNot(notSchema!);
        }

        private bool IsMeaningfulNotSchema(
            OpenApiSemanticSchema schema,
            HashSet<NormalizedSpecNodeIdentity> visited)
        {
            switch (schema.Kind)
            {
                case OpenApiSemanticSchemaKind.Any:
                    return false;
                case OpenApiSemanticSchemaKind.Union:
                    return schema.UnionMembers.All(member => IsMeaningfulNotSchema(member, visited));
                case OpenApiSemanticSchemaKind.Reference:
                    return schema.ReferenceIdentity.IsEmpty ||
                           !visited.Add(schema.ReferenceIdentity) ||
                           !_schemas.TryGetValue(schema.ReferenceIdentity, out OpenApiSemanticSchema? target) ||
                           IsMeaningfulNotSchema(target, visited);
                default:
                    return true;
            }
        }
    }
}
