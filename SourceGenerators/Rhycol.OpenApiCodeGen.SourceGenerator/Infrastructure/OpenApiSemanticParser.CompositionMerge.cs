using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Same-named properties of the parts of an allOf composition that the enum narrowing
    /// (<c>TryMergeNarrowedProperty</c>) does not cover. The declarations are combined into one type that is an upper
    /// bound of every value that satisfies all of them; whether a value does is decided by the assertion of the
    /// composition (see <c>ObjectComposition.NeedsAssertion</c>), never by the choice made here.
    /// </summary>
    internal sealed partial class OpenApiSemanticParser
    {
        // A deeper combination (recursive schemas) keeps the first declaration.
        private const int MaxMergeDepth = 8;

        private enum MergeJsonKind
        {
            None,
            String,
            Integer,
            Number,
            Boolean,
            Array,
            Object
        }

        private static MergeJsonKind JsonKindOf(OpenApiSemanticSchema effective)
        {
            switch (effective.Kind)
            {
                case OpenApiSemanticSchemaKind.String:
                    return MergeJsonKind.String;
                case OpenApiSemanticSchemaKind.Integer:
                    return MergeJsonKind.Integer;
                case OpenApiSemanticSchemaKind.Number:
                    return MergeJsonKind.Number;
                case OpenApiSemanticSchemaKind.Boolean:
                    return MergeJsonKind.Boolean;
                case OpenApiSemanticSchemaKind.Array:
                    return MergeJsonKind.Array;
                case OpenApiSemanticSchemaKind.Object:
                case OpenApiSemanticSchemaKind.FreeObject:
                case OpenApiSemanticSchemaKind.Map:
                    return MergeJsonKind.Object;
                case OpenApiSemanticSchemaKind.Enum:
                    IReadOnlyList<OpenApiSemanticLiteral> values = effective.TypedEnumValues;
                    if (values.Count == 0 || values.Any(value => value.Kind != values[0].Kind))
                    {
                        return MergeJsonKind.None;
                    }

                    return values[0].Kind == OpenApiSemanticLiteralKind.String
                        ? MergeJsonKind.String
                        : values[0].Kind == OpenApiSemanticLiteralKind.Integer ? MergeJsonKind.Integer : MergeJsonKind.None;
                default:
                    return MergeJsonKind.None;
            }
        }

        private OpenApiSemanticSchema MergeDeclarations(
            OpenApiSemanticSchema existing,
            OpenApiSemanticSchema incoming,
            SpecNode node,
            string name,
            int depth)
        {
            OpenApiSemanticSchema a = GetEffectiveSchema(existing);
            OpenApiSemanticSchema b = GetEffectiveSchema(incoming);

            // A declaration without a type (JToken) restricts nothing, null included.
            if (a.Kind == OpenApiSemanticSchemaKind.Any)
            {
                return incoming;
            }

            if (b.Kind == OpenApiSemanticSchemaKind.Any)
            {
                return existing;
            }

            MergeJsonKind kindA = JsonKindOf(a);
            MergeJsonKind kindB = JsonKindOf(b);
            if (kindA == MergeJsonKind.None || kindB == MergeJsonKind.None || depth >= MaxMergeDepth)
            {
                // A union, a recursive reference, ...: values that satisfy both satisfy the first one, so its type is kept.
                return existing;
            }

            // null is valid only when every declaration that has a type allows it.
            bool nullable = IsSchemaNullable(existing) && IsSchemaNullable(incoming);
            if (kindA != kindB)
            {
                bool integerAndNumber =
                    (kindA == MergeJsonKind.Integer && kindB == MergeJsonKind.Number) ||
                    (kindA == MergeJsonKind.Number && kindB == MergeJsonKind.Integer);
                if (integerAndNumber)
                {
                    return WithMergedNullable(kindA == MergeJsonKind.Integer ? existing : incoming, nullable);
                }

                AddIgnoredWarning(
                    node,
                    "The parts of an allOf composition declare this property with JSON types that have no value in common, " +
                    "so it is generated as an untyped value (no value can be valid).");
                return CreateAnySchema(node, name, OpenApiSemanticAnnotations.Empty);
            }

            switch (kindA)
            {
                case MergeJsonKind.Array:
                    return MergeArrays(existing, incoming, a, b, node, name, depth, nullable);
                case MergeJsonKind.Object:
                    return MergeObjects(existing, incoming, a, b, node, name, depth, nullable);
                default:
                    return MergeScalars(existing, incoming, a, b, kindA, node, nullable);
            }
        }

        private OpenApiSemanticSchema MergeScalars(
            OpenApiSemanticSchema existing,
            OpenApiSemanticSchema incoming,
            OpenApiSemanticSchema a,
            OpenApiSemanticSchema b,
            MergeJsonKind kind,
            SpecNode node,
            bool nullable)
        {
            OpenApiSemanticSchema chosen;
            OpenApiSemanticSchema other;
            if (a.Kind == OpenApiSemanticSchemaKind.Enum || b.Kind == OpenApiSemanticSchemaKind.Enum)
            {
                // The enum already is the narrowest type; its values are judged by the assertion.
                return WithMergedNullable(a.Kind == OpenApiSemanticSchemaKind.Enum ? existing : incoming, nullable);
            }

            if (string.Equals(a.Format, b.Format, StringComparison.Ordinal) || b.Format.Length == 0)
            {
                chosen = existing;
                other = incoming;
            }
            else if (a.Format.Length == 0)
            {
                chosen = incoming;
                other = existing;
            }
            else if (kind == MergeJsonKind.Integer &&
                     (string.Equals(a.Format, "int32", StringComparison.Ordinal) ||
                      string.Equals(b.Format, "int32", StringComparison.Ordinal)))
            {
                // Two integer formats: the narrower one holds every valid value.
                bool existingNarrower = string.Equals(a.Format, "int32", StringComparison.Ordinal);
                chosen = existingNarrower ? existing : incoming;
                other = existingNarrower ? incoming : existing;
            }
            else
            {
                AddIgnoredWarning(
                    node,
                    "The parts of an allOf composition declare this property with the formats '" + a.Format + "' and '" +
                    b.Format + "', so it is generated as its base type.");
                chosen = a.WithFormat(string.Empty);
                other = incoming;
            }

            return WithMergedNullable(AddMissingConstraintSets(chosen, other), nullable);
        }

        private OpenApiSemanticSchema MergeArrays(
            OpenApiSemanticSchema existing,
            OpenApiSemanticSchema incoming,
            OpenApiSemanticSchema a,
            OpenApiSemanticSchema b,
            SpecNode node,
            string name,
            int depth,
            bool nullable)
        {
            OpenApiSemanticSchema? itemsA = a.ItemSchema;
            OpenApiSemanticSchema? itemsB = b.ItemSchema;
            if (itemsA is null || itemsB is null)
            {
                return WithMergedNullable(existing, nullable);
            }

            string signatureA = GetSchemaSignature(itemsA);
            string signatureB = GetSchemaSignature(itemsB);
            OpenApiSemanticSchema items = string.Equals(signatureA, signatureB, StringComparison.Ordinal)
                ? itemsA
                : MergeDeclarations(itemsA, itemsB, node, name + "Item", depth + 1);
            string signature = GetSchemaSignature(items);
            OpenApiSemanticSchema chosen;
            OpenApiSemanticSchema other;
            if (string.Equals(signature, signatureA, StringComparison.Ordinal))
            {
                chosen = existing;
                other = incoming;
            }
            else if (string.Equals(signature, signatureB, StringComparison.Ordinal))
            {
                chosen = incoming;
                other = existing;
            }
            else
            {
                chosen = a.WithItemSchema(items);
                other = incoming;
            }

            return WithMergedNullable(AddMissingConstraintSets(chosen, other), nullable);
        }

        private OpenApiSemanticSchema MergeObjects(
            OpenApiSemanticSchema existing,
            OpenApiSemanticSchema incoming,
            OpenApiSemanticSchema a,
            OpenApiSemanticSchema b,
            SpecNode node,
            string name,
            int depth,
            bool nullable)
        {
            if (a.Kind != OpenApiSemanticSchemaKind.Object || b.Kind != OpenApiSemanticSchemaKind.Object)
            {
                // A free-form object or a map says nothing about the members that the DTO declares.
                OpenApiSemanticSchema declared = a.Kind == OpenApiSemanticSchemaKind.Object ||
                                                 b.Kind == OpenApiSemanticSchemaKind.FreeObject
                    ? existing
                    : b.Kind == OpenApiSemanticSchemaKind.Object || a.Kind == OpenApiSemanticSchemaKind.FreeObject
                        ? incoming
                        : existing;
                return WithMergedNullable(declared, nullable);
            }

            var properties = new List<OpenApiSemanticProperty>();
            foreach (string propertyName in a.Properties.Select(static property => property.WireName)
                         .Concat(b.Properties.Select(static property => property.WireName))
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(static value => value, StringComparer.Ordinal))
            {
                OpenApiSemanticProperty? first = a.Properties.FirstOrDefault(
                    property => string.Equals(property.WireName, propertyName, StringComparison.Ordinal));
                OpenApiSemanticProperty? second = b.Properties.FirstOrDefault(
                    property => string.Equals(property.WireName, propertyName, StringComparison.Ordinal));
                if (first is null || second is null)
                {
                    properties.Add((first ?? second)!);
                    continue;
                }

                OpenApiSemanticSchema schema = string.Equals(
                    GetSchemaSignature(first.Schema), GetSchemaSignature(second.Schema), StringComparison.Ordinal)
                    ? first.Schema
                    : MergeDeclarations(
                        first.Schema, second.Schema, node, name + ToPascalFragment(propertyName), depth + 1);
                properties.Add(new OpenApiSemanticProperty(
                    propertyName, first.Required || second.Required, schema, first.Location));
            }

            OpenApiSemanticAdditionalProperties mode = a.AdditionalProperties == OpenApiSemanticAdditionalProperties.Closed ||
                                                       b.AdditionalProperties == OpenApiSemanticAdditionalProperties.Closed
                ? OpenApiSemanticAdditionalProperties.Closed
                : a.AdditionalProperties == OpenApiSemanticAdditionalProperties.Typed ||
                  b.AdditionalProperties == OpenApiSemanticAdditionalProperties.Typed
                    ? OpenApiSemanticAdditionalProperties.Typed
                    : OpenApiSemanticAdditionalProperties.Open;
            OpenApiSemanticSchema? valueSchema = mode == OpenApiSemanticAdditionalProperties.Typed
                ? a.ValueSchema ?? b.ValueSchema
                : null;

            // The result is a type that already exists when its shape is the same (the same members, requiredness and member
            // types; extra constraints are asserted): a named schema first, the derived one (incoming) before the first one.
            bool incomingNamed = incoming.Kind == OpenApiSemanticSchemaKind.Reference;
            bool existingNamed = existing.Kind == OpenApiSemanticSchemaKind.Reference;
            bool incomingFits = HasObjectShape(b, properties, mode, valueSchema);
            bool existingFits = HasObjectShape(a, properties, mode, valueSchema);
            if (incomingFits && (incomingNamed || !(existingFits && existingNamed)))
            {
                return WithMergedNullable(incoming, nullable);
            }

            if (existingFits)
            {
                return WithMergedNullable(existing, nullable);
            }

            return CreateObjectShape(
                node, name, string.Empty, nullable, OpenApiSemanticAnnotations.Empty, properties, mode, valueSchema);
        }

        private bool HasObjectShape(
            OpenApiSemanticSchema target,
            IReadOnlyList<OpenApiSemanticProperty> properties,
            OpenApiSemanticAdditionalProperties mode,
            OpenApiSemanticSchema? valueSchema)
        {
            if (target.Properties.Count != properties.Count ||
                target.AdditionalProperties != mode ||
                !string.Equals(GetSchemaSignature(target.ValueSchema), GetSchemaSignature(valueSchema), StringComparison.Ordinal))
            {
                return false;
            }

            foreach (OpenApiSemanticProperty property in properties)
            {
                OpenApiSemanticProperty? own = target.Properties.FirstOrDefault(
                    candidate => string.Equals(candidate.WireName, property.WireName, StringComparison.Ordinal));
                if (own is null ||
                    own.Required != property.Required ||
                    !string.Equals(
                        GetSchemaShapeSignature(own.Schema, new HashSet<NormalizedSpecNodeIdentity>()),
                        GetSchemaShapeSignature(property.Schema, new HashSet<NormalizedSpecNodeIdentity>()),
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static OpenApiSemanticSchema WithMergedNullable(OpenApiSemanticSchema schema, bool nullable)
        {
            return schema.Nullable && !nullable ? schema.WithNullable(false) : schema;
        }

        // The constraint sets of the other declaration that the kept one does not have yet (each set has to hold).
        private OpenApiSemanticSchema AddMissingConstraintSets(OpenApiSemanticSchema chosen, OpenApiSemanticSchema other)
        {
            if (other.ConstraintSets.Count == 0)
            {
                return chosen;
            }

            var known = new HashSet<string>(
                chosen.ConstraintSets.Select(set => GetConstraintSignature(set, new HashSet<NormalizedSpecNodeIdentity>())),
                StringComparer.Ordinal);
            OpenApiSemanticConstraints[] extra = other.ConstraintSets
                .Where(set => known.Add(GetConstraintSignature(set, new HashSet<NormalizedSpecNodeIdentity>())))
                .ToArray();
            return chosen.WithConstraintSets(extra);
        }
    }
}
