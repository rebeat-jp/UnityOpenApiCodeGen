using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>patternProperties entry: a property-name regular expression and the schema of the matching values.</summary>
    internal sealed class OpenApiSemanticPatternProperty
    {
        internal OpenApiSemanticPatternProperty(string pattern, bool ecmaScript, OpenApiSemanticSchema schema)
        {
            Pattern = pattern;
            EcmaScript = ecmaScript;
            Schema = schema;
        }

        internal string Pattern { get; }

        /// <summary>True when the pattern is evaluated with RegexOptions.ECMAScript, false for the .NET default mode.</summary>
        internal bool EcmaScript { get; }

        internal OpenApiSemanticSchema Schema { get; }
    }

    /// <summary>dependentRequired entry: when Property is present, every name in Required must be present too.</summary>
    internal sealed class OpenApiSemanticDependentRequired
    {
        internal OpenApiSemanticDependentRequired(string property, IReadOnlyList<string> required)
        {
            Property = property;
            Required = required;
        }

        internal string Property { get; }

        internal IReadOnlyList<string> Required { get; }
    }

    /// <summary>
    /// One set of validation keywords of a schema (minLength, pattern, minimum, ...). They are checked at
    /// run time and never change the generated type. A schema carries a list of sets: the sets of
    /// $ref targets and allOf parts are kept side by side (every set must hold) instead of being merged.
    /// Number limits keep their JSON lexeme so the generator can pick decimal or double.
    /// </summary>
    internal sealed class OpenApiSemanticConstraints
    {
        internal long? MinLength { get; set; }

        internal long? MaxLength { get; set; }

        /// <summary>A regular expression; it is evaluated with RegexOptions.ECMAScript when the run time accepts it there, otherwise with the .NET semantics.</summary>
        internal string? Pattern { get; set; }

        internal bool PatternEcmaScript { get; set; }

        internal string? Minimum { get; set; }

        internal string? Maximum { get; set; }

        internal string? ExclusiveMinimum { get; set; }

        internal string? ExclusiveMaximum { get; set; }

        internal string? MultipleOf { get; set; }

        internal long? MinItems { get; set; }

        internal long? MaxItems { get; set; }

        internal bool UniqueItems { get; set; }

        internal long? MinProperties { get; set; }

        internal long? MaxProperties { get; set; }

        internal IReadOnlyList<OpenApiSemanticPatternProperty> PatternProperties { get; set; } =
            Array.Empty<OpenApiSemanticPatternProperty>();

        internal OpenApiSemanticSchema? PropertyNames { get; set; }

        internal OpenApiSemanticSchema? Contains { get; set; }

        internal long? MinContains { get; set; }

        internal long? MaxContains { get; set; }

        /// <summary>Position-wise schemas of prefixItems (the array then has untyped elements).</summary>
        internal IReadOnlyList<OpenApiSemanticSchema> PrefixItems { get; set; } =
            Array.Empty<OpenApiSemanticSchema>();

        /// <summary>The items schema that applies to the elements after the prefixItems.</summary>
        internal OpenApiSemanticSchema? ItemsAfterPrefix { get; set; }

        internal IReadOnlyList<OpenApiSemanticDependentRequired> DependentRequired { get; set; } =
            Array.Empty<OpenApiSemanticDependentRequired>();

        /// <summary>
        /// The values an enum / string / integer value may take (an allOf part narrowed the property to a
        /// subset of the values of the type it keeps). The wire value must be one of them.
        /// </summary>
        internal IReadOnlyList<OpenApiSemanticLiteral> AllowedValues { get; set; } =
            Array.Empty<OpenApiSemanticLiteral>();

        internal bool IsEmpty => AllowedValues.Count == 0 && HasOnlyAllowedValues;

        /// <summary>
        /// True when the set holds no constraint other than <see cref="AllowedValues"/> (which may be empty).
        /// </summary>
        internal bool HasOnlyAllowedValues =>
            !MinLength.HasValue && !MaxLength.HasValue && Pattern is null &&
            Minimum is null && Maximum is null && ExclusiveMinimum is null && ExclusiveMaximum is null &&
            MultipleOf is null && !MinItems.HasValue && !MaxItems.HasValue && !UniqueItems &&
            !MinProperties.HasValue && !MaxProperties.HasValue && PatternProperties.Count == 0 &&
            PropertyNames is null && Contains is null && PrefixItems.Count == 0 &&
            ItemsAfterPrefix is null && DependentRequired.Count == 0;
    }
}
