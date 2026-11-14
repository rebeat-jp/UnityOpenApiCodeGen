using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// The XML documentation sentences of the validation keywords (for example
    /// "Must be 3..50 characters."). The sentences are plain text; the caller escapes them.
    /// </summary>
    internal static class GeneratedConstraintDocumentation
    {
        internal static IReadOnlyList<string> Describe(IReadOnlyList<GeneratedConstraints> sets)
        {
            var lines = new List<string>();
            foreach (GeneratedConstraints set in sets)
            {
                AddRange(lines, set.MinLength, set.MaxLength, "Must be ", " characters.", "at least ", "at most ");
                if (set.Pattern is not null)
                {
                    lines.Add("Must match the pattern " + ConstraintValidationEmitter.Clip(set.Pattern) + ".");
                }

                if (set.Minimum is not null)
                {
                    lines.Add("Must be at least " + set.Minimum + ".");
                }

                if (set.ExclusiveMinimum is not null)
                {
                    lines.Add("Must be greater than " + set.ExclusiveMinimum + ".");
                }

                if (set.Maximum is not null)
                {
                    lines.Add("Must be at most " + set.Maximum + ".");
                }

                if (set.ExclusiveMaximum is not null)
                {
                    lines.Add("Must be less than " + set.ExclusiveMaximum + ".");
                }

                if (set.MultipleOf is not null)
                {
                    lines.Add("Must be a multiple of " + set.MultipleOf + ".");
                }

                AddRange(lines, set.MinItems, set.MaxItems, "Must contain ", " items.", "at least ", "at most ");
                if (set.UniqueItems)
                {
                    lines.Add("Items must be unique.");
                }

                AddRange(lines, set.MinProperties, set.MaxProperties, "Must have ", " properties.", "at least ", "at most ");
                foreach (GeneratedPatternProperty entry in set.PatternProperties)
                {
                    lines.Add("Values of properties whose name matches " + ConstraintValidationEmitter.Clip(entry.Pattern) +
                              " are validated against the patternProperties schema.");
                }

                if (set.PropertyNames is not null)
                {
                    lines.Add("Property names must satisfy the propertyNames schema.");
                }

                if (set.Contains is not null)
                {
                    long min = set.MinContains ?? 1;
                    lines.Add("At least " + min.ToString(CultureInfo.InvariantCulture) +
                              " item(s) must satisfy the contains schema" +
                              (set.MaxContains.HasValue
                                  ? " and at most " + set.MaxContains.Value.ToString(CultureInfo.InvariantCulture)
                                  : string.Empty) + ".");
                }

                if (set.PrefixItems.Count > 0)
                {
                    lines.Add("Items are validated by position (prefixItems).");
                }

                foreach (GeneratedDependentRequired entry in set.DependentRequired)
                {
                    lines.Add("When " + entry.Property + " is present, " + string.Join(", ", entry.Required) +
                              " must be present as well.");
                }
            }

            return lines;
        }

        private static void AddRange(
            List<string> lines,
            long? min,
            long? max,
            string prefix,
            string suffix,
            string minWord,
            string maxWord)
        {
            if (min.HasValue && max.HasValue)
            {
                string range = min.Value == max.Value
                    ? "exactly " + Text(min.Value)
                    : Text(min.Value) + ".." + Text(max.Value);
                lines.Add(prefix + range + suffix);
            }
            else if (min.HasValue)
            {
                lines.Add(prefix + minWord + Text(min.Value) + suffix);
            }
            else if (max.HasValue)
            {
                lines.Add(prefix + maxWord + Text(max.Value) + suffix);
            }
        }

        private static string Text(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
