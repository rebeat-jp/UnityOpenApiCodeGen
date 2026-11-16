using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>The JSON types a schema allows (a set of flags). Number also accepts the integers.</summary>
    [Flags]
    internal enum OpenApiSemanticAssertionTypes
    {
        None = 0,
        Null = 1,
        Boolean = 2,
        /// <summary>A number whose value is integral (1.0 counts).</summary>
        Integer = 4,
        Number = 8,
        String = 16,
        Array = 32,
        Object = 64,
        All = Null | Boolean | Integer | Number | String | Array | Object
    }

    internal enum OpenApiSemanticAssertionValueKind
    {
        Null,
        Boolean,
        String,
        Integer,
        Number,
        Array,
        Object
    }

    /// <summary>
    /// A raw JSON literal of enum / const. <see cref="RawJson"/> is a JSON text (a string keeps its quotes), so
    /// objects, arrays and numbers keep exactly what the document said; the kind tells which one it is.
    /// </summary>
    internal readonly struct OpenApiSemanticAssertionValue : IEquatable<OpenApiSemanticAssertionValue>
    {
        internal OpenApiSemanticAssertionValue(OpenApiSemanticAssertionValueKind kind, string rawJson)
        {
            Kind = kind;
            RawJson = rawJson ?? string.Empty;
        }

        internal OpenApiSemanticAssertionValueKind Kind { get; }

        internal string RawJson { get; }

        public bool Equals(OpenApiSemanticAssertionValue other)
        {
            return Kind == other.Kind && string.Equals(RawJson, other.RawJson, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is OpenApiSemanticAssertionValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(RawJson);
        }
    }

    /// <summary>A declared property of an assertion node. ReadOnly / WriteOnly are the effective flags (a $ref is followed).</summary>
    internal sealed class OpenApiSemanticAssertionProperty
    {
        internal OpenApiSemanticAssertionProperty(
            string name,
            OpenApiSemanticAssertion schema,
            bool readOnly,
            bool writeOnly)
        {
            Name = name;
            Schema = schema;
            ReadOnly = readOnly;
            WriteOnly = writeOnly;
        }

        internal string Name { get; }

        internal OpenApiSemanticAssertion Schema { get; }

        /// <summary>readOnly: a request does not have to carry the property, so `required` does not apply to a request.</summary>
        internal bool ReadOnly { get; }

        /// <summary>writeOnly: a response does not have to carry the property, so `required` does not apply to a response.</summary>
        internal bool WriteOnly { get; }
    }

    internal sealed class OpenApiSemanticAssertionPatternProperty
    {
        internal OpenApiSemanticAssertionPatternProperty(string pattern, bool ecmaScript, OpenApiSemanticAssertion schema)
        {
            Pattern = pattern;
            EcmaScript = ecmaScript;
            Schema = schema;
        }

        internal string Pattern { get; }

        internal bool EcmaScript { get; }

        internal OpenApiSemanticAssertion Schema { get; }
    }

    /// <summary>
    /// A schema subtree kept in the meaning of JSON Schema, for the run-time assertions of the generated client.
    /// Unlike <see cref="OpenApiSemanticSchema"/> it has no type of its own: every keyword is a condition that
    /// applies to the JSON types it is defined for (properties to objects, minLength to strings, ...), and a
    /// schema without a keyword accepts everything. oneOf / anyOf / allOf / not are conditions as well.
    /// A node is completed by <c>OpenApiSemanticParser.MapAssertion</c> and is not changed afterwards; the
    /// nodes of the components that a $ref points to are shared (so a recursive schema is a cycle of nodes).
    /// </summary>
    internal sealed class OpenApiSemanticAssertion
    {
        /// <summary>The component name for a node that a $ref points to; empty otherwise (only for diagnostics and comments).</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>The allowed types; null when the schema has no type keyword (no restriction, null included).</summary>
        internal OpenApiSemanticAssertionTypes? Types { get; set; }

        /// <summary>The values of enum; null when there is no enum.</summary>
        internal IReadOnlyList<OpenApiSemanticAssertionValue>? Enum { get; set; }

        /// <summary>The value of const (OpenAPI 3.1).</summary>
        internal OpenApiSemanticAssertionValue? Const { get; set; }

        /// <summary>format as written (only date, date-time, uuid, byte, int32 and int64 are asserted, like the typed contracts do).</summary>
        internal string Format { get; set; } = string.Empty;

        /// <summary>
        /// The scalar validation keywords (min/maxLength, pattern, the number bounds, multipleOf, min/maxItems,
        /// uniqueItems, min/maxProperties); the keywords that carry schemas are fields of this class.
        /// </summary>
        internal OpenApiSemanticConstraints? Constraints { get; set; }

        internal IReadOnlyList<OpenApiSemanticAssertionProperty> Properties { get; set; } =
            Array.Empty<OpenApiSemanticAssertionProperty>();

        internal IReadOnlyList<string> Required { get; set; } = Array.Empty<string>();

        /// <summary>Open (omitted or true), Closed (false) or Typed (AdditionalPropertiesSchema).</summary>
        internal OpenApiSemanticAdditionalProperties AdditionalProperties { get; set; } =
            OpenApiSemanticAdditionalProperties.Open;

        internal OpenApiSemanticAssertion? AdditionalPropertiesSchema { get; set; }

        /// <summary>items: every element, or the elements after <see cref="PrefixItems"/> when there are some.</summary>
        internal OpenApiSemanticAssertion? Items { get; set; }

        internal IReadOnlyList<OpenApiSemanticAssertion> PrefixItems { get; set; } =
            Array.Empty<OpenApiSemanticAssertion>();

        internal IReadOnlyList<OpenApiSemanticAssertionPatternProperty> PatternProperties { get; set; } =
            Array.Empty<OpenApiSemanticAssertionPatternProperty>();

        internal OpenApiSemanticAssertion? PropertyNames { get; set; }

        internal OpenApiSemanticAssertion? Contains { get; set; }

        internal long? MinContains { get; set; }

        internal long? MaxContains { get; set; }

        internal IReadOnlyList<OpenApiSemanticDependentRequired> DependentRequired { get; set; } =
            Array.Empty<OpenApiSemanticDependentRequired>();

        internal IReadOnlyList<OpenApiSemanticAssertion> AllOf { get; set; } = Array.Empty<OpenApiSemanticAssertion>();

        internal IReadOnlyList<OpenApiSemanticAssertion> AnyOf { get; set; } = Array.Empty<OpenApiSemanticAssertion>();

        internal IReadOnlyList<OpenApiSemanticAssertion> OneOf { get; set; } = Array.Empty<OpenApiSemanticAssertion>();

        internal OpenApiSemanticAssertion? Not { get; set; }

        /// <summary>
        /// The node a $ref points to (also required to hold). In OpenAPI 3.0 the siblings of $ref are ignored, so a
        /// 3.0 reference is the target node itself; this field is only set for a 3.1 $ref that has siblings.
        /// </summary>
        internal OpenApiSemanticAssertion? Reference { get; set; }

        /// <summary>
        /// The node whose readOnly / writeOnly this node inherits without also requiring it to hold (a 3.0 schema
        /// {$ref, nullable: true} is "null or the target", so the target is no <see cref="Reference"/>).
        /// </summary>
        internal OpenApiSemanticAssertion? AnnotationTarget { get; set; }

        /// <summary>The boolean schema false (OpenAPI 3.1): no value is valid.</summary>
        internal bool AlwaysFalse { get; set; }

        /// <summary>readOnly / writeOnly of the schema itself.</summary>
        internal bool ReadOnly { get; set; }

        internal bool WriteOnly { get; set; }

        /// <summary>The effective readOnly flag: the schema's own or that of the schema it references.</summary>
        internal bool EffectiveReadOnly => Effective(static node => node.ReadOnly);

        internal bool EffectiveWriteOnly => Effective(static node => node.WriteOnly);

        private bool Effective(Func<OpenApiSemanticAssertion, bool> flag)
        {
            OpenApiSemanticAssertion? current = this;
            for (int hops = 0; current is not null && hops < 32; hops++)
            {
                if (flag(current))
                {
                    return true;
                }

                current = current.Reference ?? current.AnnotationTarget;
            }

            return false;
        }

        /// <summary>True when the node carries no keyword of its own (only, perhaps, annotations and a reference).</summary>
        internal bool HasNoOwnKeyword =>
            Types is null && Enum is null && !Const.HasValue && Format.Length == 0 && Constraints is null &&
            Properties.Count == 0 && Required.Count == 0 &&
            AdditionalProperties == OpenApiSemanticAdditionalProperties.Open &&
            Items is null && PrefixItems.Count == 0 && PatternProperties.Count == 0 &&
            PropertyNames is null && Contains is null && DependentRequired.Count == 0 &&
            AllOf.Count == 0 && AnyOf.Count == 0 && OneOf.Count == 0 && Not is null && !AlwaysFalse;
    }
}
