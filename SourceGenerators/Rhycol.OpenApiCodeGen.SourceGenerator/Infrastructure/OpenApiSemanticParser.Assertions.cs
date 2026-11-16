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

        // The targets as the part of an allOf (see MapAssertion): a base with a discriminator leaves its oneOf / anyOf out.
        private readonly Dictionary<NormalizedSpecNodeIdentity, OpenApiSemanticAssertion> _assertionBaseTargets =
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

            return MapAssertion(node, false);
        }

        /// <summary>
        /// <paramref name="allOfPart"/>: the node is a part of an allOf. A node with a discriminator that is a part of an allOf is the
        /// base of an inheritance hierarchy: its oneOf / anyOf lists its subtypes (the schemas that extend it), which a subtype only
        /// inherits the properties of (the typed model does the same), so they are not asserted again (it would be a cycle).
        /// </summary>
        private OpenApiSemanticAssertion MapAssertion(SpecNode node, bool allOfPart)
        {

            if (_minorVersion == 1 && (node.ValueKind == SpecValueKind.True || node.ValueKind == SpecValueKind.False))
            {
                return new OpenApiSemanticAssertion { AlwaysFalse = node.ValueKind == SpecValueKind.False };
            }

            RequireKind(node, SpecValueKind.Object, "A schema must be an object.");
            ValidateAssertionKeywords(node);
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                OpenApiSemanticAssertion target = MapAssertionTarget(reference, referenceNode, allOfPart);
                if (_minorVersion == 0)
                {
                    // OpenAPI 3.0: the siblings are ignored, except those the parser keeps (nullable, readOnly, writeOnly).
                    return WrapReference30(node, target);
                }

                var withSiblings = new OpenApiSemanticAssertion { Reference = target };
                FillAssertion(withSiblings, node, allOfPart);
                return withSiblings.HasNoOwnKeyword && !withSiblings.ReadOnly && !withSiblings.WriteOnly
                    ? target
                    : withSiblings;
            }

            var result = new OpenApiSemanticAssertion();
            FillAssertion(result, node, allOfPart);
            return result;
        }

        // The mapper has no types to fall back on: a keyword it does not evaluate (if / then / else, unevaluated*, a keyword of the
        // other dialect, an unknown one) would be ignored without a trace, so it is rejected like the typed path rejects it (OACG101).
        // The siblings of a 3.0 $ref are ignored by the parser and are not checked.
        private void ValidateAssertionKeywords(SpecNode node)
        {
            if (!IsMinor30ReferenceNode(node))
            {
                ValidateUnsupportedSchemaKeywords(node);
            }
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

        private OpenApiSemanticAssertion MapAssertionTarget(string reference, SpecNode referenceNode, bool allOfPart = false)
        {
            ResolvedSpecReference resolved = ResolveReference(reference, referenceNode);
            // Only a base with a discriminator differs when it is the part of an allOf; the other schemas share one node.
            bool leaveOutAlternatives = allOfPart && HasDiscriminatedAlternatives(resolved.Node);
            Dictionary<NormalizedSpecNodeIdentity, OpenApiSemanticAssertion> targets =
                leaveOutAlternatives ? _assertionBaseTargets : _assertionTargets;
            if (targets.TryGetValue(resolved.Identity, out OpenApiSemanticAssertion? existing))
            {
                return existing;
            }

            // Registered before the body is mapped: a $ref back to it (a recursive schema) finds the node.
            var target = new OpenApiSemanticAssertion
            {
                Name = JsonPointerResolver.GetSchemaPointerName(resolved.Pointer, referenceNode),
                Location = CreateLocation(resolved.Node, resolved.DocumentId)
            };
            targets.Add(resolved.Identity, target);
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
                    ValidateAssertionKeywords(resolved.Node);
                    if (TryGetReference(resolved.Node, out string innerReference, out SpecNode innerReferenceNode))
                    {
                        // A $ref whose target is a $ref: 3.0 keeps nullable, readOnly and writeOnly, 3.1 keeps every sibling.
                        OpenApiSemanticAssertion inner = MapAssertionTarget(innerReference, innerReferenceNode, allOfPart);
                        if (_minorVersion == 1)
                        {
                            target.Reference = inner;
                            FillAssertion(target, resolved.Node, allOfPart);
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
                        FillAssertion(target, resolved.Node, allOfPart);
                    }
                }
            }
            finally
            {
                _currentDocumentId = previousDocumentId;
            }

            return target;
        }

        private static bool HasDiscriminatedAlternatives(SpecNode node)
        {
            return node.ValueKind == SpecValueKind.Object &&
                   node.TryGetProperty("discriminator", out _) &&
                   (node.TryGetProperty("oneOf", out _) || node.TryGetProperty("anyOf", out _));
        }

        private void FillAssertion(OpenApiSemanticAssertion result, SpecNode node, bool allOfPart = false)
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
            if (itemsNode is null && _minorVersion == 0 && typeNode is not null &&
                typeNode.ValueKind == SpecValueKind.String &&
                string.Equals(typeNode.GetString(), "array", StringComparison.Ordinal))
            {
                // OpenAPI 3.0: items MUST be present if the type is array (3.1 makes it optional).
                throw Invalid(node, "Required field 'items' is missing (OpenAPI 3.0: items MUST be present if the type is array).");
            }

            if (itemsNode is not null)
            {
                result.Items = MapAssertion(itemsNode);
            }

            IReadOnlyList<OpenApiSemanticAssertion> allOf = MapAssertionList(node, "allOf", allOfPart: true);
            if (allOf.Count == 1 && IsNullableSingleReferenceIdiom(node))
            {
                // allOf: [ { $ref } ] with nullable next to it is the reference that may be null (the parser reads it so): null or the target.
                allOf = new[]
                {
                    new OpenApiSemanticAssertion
                    {
                        AnyOf = new[] { new OpenApiSemanticAssertion { Types = OpenApiSemanticAssertionTypes.Null }, allOf[0] },
                        AnnotationTarget = allOf[0]
                    }
                };
            }

            result.AllOf = allOf;
            if (!(allOfPart && HasDiscriminatedAlternatives(node)))
            {
                result.AnyOf = MapAssertionList(node, "anyOf");
                result.OneOf = MapAssertionList(node, "oneOf");
            }

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
        /// the properties its alternatives declare and the node does not become optional properties of the node. The type of such a
        /// property is an upper bound of the values the node accepts, so it is typed only when every alternative declares the
        /// property and every declaration has a type and says the same (a declaration that only pins null, such as `{enum: [null]}`,
        /// may differ); otherwise it is untyped (any JSON value). The type carries none of the keywords of an alternative: the
        /// alternatives stay assertions. A closed node gets no new property: the alternatives can only select among the declared ones.
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
            int alternativeCount = 0;
            foreach (string keyword in new[] { "oneOf", "anyOf" })
            {
                SpecNode? listNode = GetProperty(node, keyword);
                if (listNode is null || listNode.ValueKind != SpecValueKind.Array)
                {
                    continue;
                }

                foreach (SpecNode member in listNode.EnumerateArray())
                {
                    alternativeCount++;
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
                        if (declared.Contains(property.Name))
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
                List<SpecNode> declarations = candidates[name];
                string propertyName = suggestedName + ToPascalFragment(name);
                OpenApiSemanticSchema chosen = declarations.Count == alternativeCount
                    ? TryTypeLiftedProperty(declarations, propertyName) ??
                      CreateAnySchema(declarations[0], propertyName, OpenApiSemanticAnnotations.Empty)
                    : CreateAnySchema(declarations[0], propertyName, OpenApiSemanticAnnotations.Empty);
                lifted.Add(new OpenApiSemanticProperty(name, false, chosen, CreateLocation(declarations[0])));
            }

            return lifted.Count == 0 ? shape : shape.WithProperties(shape.Properties.Concat(lifted).ToArray());
        }

        // The type that every declaration of a lifted property agrees on, or null (untyped).
        private OpenApiSemanticSchema? TryTypeLiftedProperty(IReadOnlyList<SpecNode> declarations, string propertyName)
        {
            var typed = new List<SpecNode>();
            foreach (SpecNode declaration in declarations)
            {
                if (declaration.ValueKind != SpecValueKind.Object)
                {
                    return null;
                }

                if (IsNullOnlyDeclaration(declaration))
                {
                    continue;
                }

                if (!declaration.TryGetProperty("type", out _) && !declaration.TryGetProperty("$ref", out _))
                {
                    // Only an enum or constraints: nothing says what the type is, so the property cannot be typed.
                    return null;
                }

                typed.Add(declaration);
            }

            if (typed.Count == 0)
            {
                return null;
            }

            OpenApiSemanticSchema[] parsed;
            try
            {
                parsed = typed
                    .Select(candidate => ParseSchema(candidate, propertyName, new HashSet<NormalizedSpecNodeIdentity>()))
                    .ToArray();
            }
            catch (OpenApiSemanticException)
            {
                // A declaration the typed model cannot hold: the property stays untyped.
                return null;
            }

            string signature = GetSchemaSignature(parsed[0].WithNullable(false));
            if (!parsed.All(item => string.Equals(
                    GetSchemaSignature(item.WithNullable(false)), signature, StringComparison.Ordinal)))
            {
                return null;
            }

            // Nullable: an alternative may pin the property to null, which the assertions then allow or reject.
            return parsed[0].WithNullable(true).WithoutConstraintSets().WithoutAssertions();
        }

        // {enum: [null]}, {nullable: true, enum: [null]} and {type: null}: a declaration that pins the property to null.
        private static bool IsNullOnlyDeclaration(SpecNode declaration)
        {
            if (declaration.TryGetProperty("$ref", out _))
            {
                return false;
            }

            if (declaration.TryGetProperty("type", out SpecNode typeNode))
            {
                return typeNode.ValueKind == SpecValueKind.String &&
                       string.Equals(typeNode.GetString(), "null", StringComparison.Ordinal);
            }

            if (!declaration.TryGetProperty("enum", out SpecNode enumNode) || enumNode.ValueKind != SpecValueKind.Array)
            {
                return false;
            }

            int count = 0;
            foreach (SpecNode value in enumNode.EnumerateArray())
            {
                if (value.ValueKind != SpecValueKind.Null)
                {
                    return false;
                }

                count++;
            }

            return count > 0;
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
                FlagProperties = MapFlagProperties(node),
                AssertsAlternatives = true,
                Origin = AssertionOrigin(node, "alternatives"),
                Location = CreateLocation(node)
            };
        }

        // The `required` of an alternative skips a readOnly property in a request (a writeOnly one in a response): the alternatives
        // do not see the properties of the node they sit next to, so their flags are handed over.
        private IReadOnlyList<OpenApiSemanticAssertionProperty> MapFlagProperties(SpecNode node)
        {
            SpecNode? propertiesNode = GetProperty(node, "properties");
            if (propertiesNode is null || propertiesNode.ValueKind != SpecValueKind.Object)
            {
                return Array.Empty<OpenApiSemanticAssertionProperty>();
            }

            var flagged = new List<OpenApiSemanticAssertionProperty>();
            foreach (SpecProperty property in propertiesNode.EnumerateObject())
            {
                OpenApiSemanticAssertion child = MapAssertion(property.Value);
                if (child.EffectiveReadOnly || child.EffectiveWriteOnly)
                {
                    flagged.Add(new OpenApiSemanticAssertionProperty(
                        property.Name, child, child.EffectiveReadOnly, child.EffectiveWriteOnly));
                }
            }

            return flagged;
        }

        /// <summary>
        /// The alternatives of a union as JSON Schema counts them (oneOf: exactly one match, anyOf: at least one), for the
        /// contract and the request validator. A nullable union accepts null, as its type does.
        /// </summary>
        private OpenApiSemanticAssertion CreateUnionAssertion(
            SpecNode node,
            bool nullable,
            IReadOnlyList<SpecNode> memberNodes,
            OpenApiSemanticDiscriminator? discriminator,
            bool isOneOf)
        {
            OpenApiSemanticAssertion alternatives = discriminator is not null && isOneOf
                ? CreateDiscriminatedAssertion(node, memberNodes, discriminator)
                : CreateAlternativesAssertion(node);
            alternatives.AssertsAlternatives = true;
            if (!nullable)
            {
                return alternatives;
            }

            return new OpenApiSemanticAssertion
            {
                AnyOf = new[] { new OpenApiSemanticAssertion { Types = OpenApiSemanticAssertionTypes.Null }, alternatives },
                AssertsAlternatives = true,
                Origin = AssertionOrigin(node, "union"),
                Location = CreateLocation(node)
            };
        }

        /// <summary>
        /// A discriminated oneOf: the discriminator selects the member (the typed contract checks that member, including the
        /// discriminator property the generator adds to a closed member), and no other member may match. Written as
        /// "the discriminator value selects member i and none of the other members matches", one branch per member (plus a
        /// branch without the discriminator condition for the 3.2 default member).
        /// </summary>
        private OpenApiSemanticAssertion CreateDiscriminatedAssertion(
            SpecNode node,
            IReadOnlyList<SpecNode> memberNodes,
            OpenApiSemanticDiscriminator discriminator)
        {
            OpenApiSemanticAssertion[] members = memberNodes.Select(MapAssertion).ToArray();
            var branches = new List<OpenApiSemanticAssertion>();
            for (int index = 0; index < members.Length; index++)
            {
                IReadOnlyList<string> values = discriminator.MemberValues[index];
                if (values.Count == 0)
                {
                    continue;
                }

                // The discriminator condition comes first: a branch of another member fails right there, so a value is
                // checked against its own member and the "no other member matches" conditions of one branch only.
                var conditions = new List<OpenApiSemanticAssertion>();
                conditions.Add(new OpenApiSemanticAssertion
                {
                    Properties = new[]
                    {
                        new OpenApiSemanticAssertionProperty(
                            discriminator.PropertyName,
                            new OpenApiSemanticAssertion
                            {
                                Enum = values.Select(static value => new OpenApiSemanticAssertionValue(
                                    OpenApiSemanticAssertionValueKind.String, JsonQuote(value))).ToArray()
                            },
                            false,
                            false)
                    },
                    Required = new[] { discriminator.PropertyName }
                });
                for (int other = 0; other < members.Length; other++)
                {
                    if (other != index)
                    {
                        conditions.Add(new OpenApiSemanticAssertion { Not = members[other] });
                    }
                }

                branches.Add(new OpenApiSemanticAssertion { AllOf = conditions });
            }

            if (discriminator.DefaultMemberIndex >= 0)
            {
                // A value that no mapping selects goes to the default member, which takes whatever the mapped members do not claim.
                branches.Add(new OpenApiSemanticAssertion
                {
                    Not = new OpenApiSemanticAssertion
                    {
                        Properties = new[]
                        {
                            new OpenApiSemanticAssertionProperty(
                                discriminator.PropertyName,
                                new OpenApiSemanticAssertion
                                {
                                    Enum = discriminator.MemberValues.SelectMany(static values => values).Select(
                                        static value => new OpenApiSemanticAssertionValue(
                                            OpenApiSemanticAssertionValueKind.String, JsonQuote(value))).ToArray()
                                },
                                false,
                                false)
                        },
                        Required = new[] { discriminator.PropertyName }
                    }
                });
            }

            return new OpenApiSemanticAssertion
            {
                AnyOf = branches,
                Origin = AssertionOrigin(node, "discriminated"),
                Location = CreateLocation(node)
            };
        }

        private static string JsonQuote(string value)
        {
            var text = new System.Text.StringBuilder("\"");
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"':
                        text.Append("\\\"");
                        break;
                    case '\\':
                        text.Append("\\\\");
                        break;
                    default:
                        if (character < 0x20)
                        {
                            text.Append("\\u").Append(((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(character);
                        }

                        break;
                }
            }

            return text.Append('"').ToString();
        }

        /// <summary>
        /// The whole schema as written (allOf parts, oneOf / anyOf, not, required, ...). Used where the typed shape is only
        /// an upper bound of the valid values. A wrapper, so that the shared node of a $ref keeps its own origin.
        /// </summary>
        private OpenApiSemanticAssertion CreateWholeAssertion(SpecNode node, bool assertsAlternatives = false)
        {
            return new OpenApiSemanticAssertion
            {
                AllOf = new[] { MapAssertion(node) },
                AssertsAlternatives = assertsAlternatives,
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

        // The condition under which the parser reads allOf: [ { $ref } ] as a reference (ParseAllOf) and nullable keeps it nullable.
        private bool IsNullableSingleReferenceIdiom(SpecNode node)
        {
            SpecNode? allOfNode = GetProperty(node, "allOf");
            if (allOfNode is null || allOfNode.ValueKind != SpecValueKind.Array)
            {
                return false;
            }

            SpecNode[] parts = allOfNode.EnumerateArray().ToArray();
            if (parts.Length != 1 || !IsSingleReferenceIdiom(node, parts[0]))
            {
                return false;
            }

            if (_minorVersion == 0)
            {
                return ParseNullable(node);
            }

            SpecNode? typeNode = GetProperty(node, "type");
            return typeNode is not null &&
                   typeNode.ValueKind == SpecValueKind.Array &&
                   typeNode.EnumerateArray().Any(static item =>
                       item.ValueKind == SpecValueKind.String &&
                       string.Equals(item.GetString(), "null", StringComparison.Ordinal));
        }

        private IReadOnlyList<OpenApiSemanticAssertion> MapAssertionList(SpecNode node, string keyword, bool allOfPart = false)
        {
            SpecNode? listNode = GetProperty(node, keyword);
            if (listNode is null)
            {
                return Array.Empty<OpenApiSemanticAssertion>();
            }

            RequireKind(listNode, SpecValueKind.Array, "The schema '" + keyword + "' field must be an array.");
            return listNode.EnumerateArray().Select(member => MapAssertion(member, allOfPart)).ToArray();
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
