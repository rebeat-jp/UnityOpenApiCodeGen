using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>patternProperties entry of the generation model: values of matching property names must satisfy Type.</summary>
    internal sealed class GeneratedPatternProperty : IEquatable<GeneratedPatternProperty>
    {
        internal GeneratedPatternProperty(string pattern, bool ecmaScript, GeneratedTypeModel type)
        {
            Pattern = pattern;
            EcmaScript = ecmaScript;
            Type = type;
        }

        internal string Pattern { get; }

        internal bool EcmaScript { get; }


        internal GeneratedTypeModel Type { get; }

        public bool Equals(GeneratedPatternProperty? other)
        {
            return other is not null &&
                   EcmaScript == other.EcmaScript &&
                   string.Equals(Pattern, other.Pattern, StringComparison.Ordinal) &&
                   Type.Equals(other.Type);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as GeneratedPatternProperty);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(Pattern) * 397) ^ Type.GetHashCode();
            }
        }
    }

    /// <summary>dependentRequired entry: when Property is present, every name in Required must be present.</summary>
    internal sealed class GeneratedDependentRequired : IEquatable<GeneratedDependentRequired>
    {
        internal GeneratedDependentRequired(string property, IReadOnlyList<string> required)
        {
            Property = property;
            Required = required;
        }

        internal string Property { get; }

        internal IReadOnlyList<string> Required { get; }

        public bool Equals(GeneratedDependentRequired? other)
        {
            return other is not null &&
                   string.Equals(Property, other.Property, StringComparison.Ordinal) &&
                   Required.SequenceEqual(other.Required, StringComparer.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as GeneratedDependentRequired);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Property);
        }
    }

    /// <summary>
    /// One set of validation keywords as the generated client checks them (minLength, pattern, minimum,
    /// ...). Number limits keep their JSON lexeme; the emitter writes a decimal or a double literal.
    /// A set belongs to the type (non-DTO types) or to the DTO model and never changes the C# type.
    /// Equality is by value because the response contract is generated per distinct type.
    /// </summary>
    internal sealed class GeneratedConstraints : IEquatable<GeneratedConstraints>
    {
        internal long? MinLength { get; set; }

        internal long? MaxLength { get; set; }

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

        internal IReadOnlyList<GeneratedPatternProperty> PatternProperties { get; set; } =
            Array.Empty<GeneratedPatternProperty>();

        internal GeneratedTypeModel? PropertyNames { get; set; }

        internal GeneratedTypeModel? Contains { get; set; }

        internal long? MinContains { get; set; }

        internal long? MaxContains { get; set; }

        internal IReadOnlyList<GeneratedTypeModel> PrefixItems { get; set; } =
            Array.Empty<GeneratedTypeModel>();

        internal GeneratedTypeModel? ItemsAfterPrefix { get; set; }

        internal IReadOnlyList<GeneratedDependentRequired> DependentRequired { get; set; } =
            Array.Empty<GeneratedDependentRequired>();

        /// <summary>The types of the sub-schemas (contains, propertyNames, patternProperties, prefixItems).</summary>
        internal IEnumerable<GeneratedTypeModel> SubTypes()
        {
            foreach (GeneratedPatternProperty entry in PatternProperties)
            {
                yield return entry.Type;
            }

            if (PropertyNames is not null)
            {
                yield return PropertyNames;
            }

            if (Contains is not null)
            {
                yield return Contains;
            }

            foreach (GeneratedTypeModel prefix in PrefixItems)
            {
                yield return prefix;
            }

            if (ItemsAfterPrefix is not null)
            {
                yield return ItemsAfterPrefix;
            }
        }

        public bool Equals(GeneratedConstraints? other)
        {
            return other is not null &&
                   MinLength == other.MinLength && MaxLength == other.MaxLength &&
                   PatternEcmaScript == other.PatternEcmaScript &&
                   string.Equals(Pattern, other.Pattern, StringComparison.Ordinal) &&
                   string.Equals(Minimum, other.Minimum, StringComparison.Ordinal) &&
                   string.Equals(Maximum, other.Maximum, StringComparison.Ordinal) &&
                   string.Equals(ExclusiveMinimum, other.ExclusiveMinimum, StringComparison.Ordinal) &&
                   string.Equals(ExclusiveMaximum, other.ExclusiveMaximum, StringComparison.Ordinal) &&
                   string.Equals(MultipleOf, other.MultipleOf, StringComparison.Ordinal) &&
                   MinItems == other.MinItems && MaxItems == other.MaxItems &&
                   UniqueItems == other.UniqueItems &&
                   MinProperties == other.MinProperties && MaxProperties == other.MaxProperties &&
                   MinContains == other.MinContains && MaxContains == other.MaxContains &&
                   PatternProperties.SequenceEqual(other.PatternProperties) &&
                   Equals(PropertyNames, other.PropertyNames) &&
                   Equals(Contains, other.Contains) &&
                   PrefixItems.SequenceEqual(other.PrefixItems) &&
                   Equals(ItemsAfterPrefix, other.ItemsAfterPrefix) &&
                   DependentRequired.SequenceEqual(other.DependentRequired);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as GeneratedConstraints);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = MinLength.GetHashCode();
                hashCode = (hashCode * 397) ^ MaxLength.GetHashCode();
                hashCode = (hashCode * 397) ^ (Pattern is null ? 0 : StringComparer.Ordinal.GetHashCode(Pattern));
                hashCode = (hashCode * 397) ^ (Minimum is null ? 0 : StringComparer.Ordinal.GetHashCode(Minimum));
                hashCode = (hashCode * 397) ^ (Maximum is null ? 0 : StringComparer.Ordinal.GetHashCode(Maximum));
                hashCode = (hashCode * 397) ^ MinItems.GetHashCode();
                hashCode = (hashCode * 397) ^ MaxItems.GetHashCode();
                hashCode = (hashCode * 397) ^ MinProperties.GetHashCode();
                hashCode = (hashCode * 397) ^ PatternProperties.Count;
                hashCode = (hashCode * 397) ^ PrefixItems.Count;
                return hashCode;
            }
        }
    }
}
