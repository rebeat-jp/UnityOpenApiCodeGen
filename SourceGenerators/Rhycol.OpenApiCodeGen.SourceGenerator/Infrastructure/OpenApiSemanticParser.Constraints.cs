using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Validation keywords (P6: G29, G30): minLength / maxLength / pattern, minimum / maximum / exclusive
    /// bounds / multipleOf, minItems / maxItems / uniqueItems, minProperties / maxProperties and, in
    /// OpenAPI 3.1, patternProperties / propertyNames / contains / minContains / maxContains /
    /// prefixItems / dependentRequired. They are parsed into <see cref="OpenApiSemanticConstraints"/>
    /// and checked at run time; they never change the generated type. A keyword that does not
    /// apply to the type of the schema (minLength on an integer) is ignored, as JSON Schema says.
    /// </summary>
    internal sealed partial class OpenApiSemanticParser
    {
        // Set by ParseObjectComposition, which already collected the constraint sets of every part
        // (including the node itself); ParseSchema reads and clears it right after the shape is parsed.
        private bool _constraintsComposed;

        private static readonly string[] ObjectConstraintKeywords =
        {
            "minProperties", "maxProperties", "patternProperties", "propertyNames", "dependentRequired"
        };

        /// <summary>
        /// Adds the constraint set of the keywords of <paramref name="node"/> to the schema that was
        /// parsed from it.
        /// </summary>
        private OpenApiSemanticSchema ApplyConstraintKeywords(SpecNode node, OpenApiSemanticSchema schema)
        {
            if (node.ValueKind != SpecValueKind.Object)
            {
                return schema;
            }

            OpenApiSemanticConstraints? set = ParseConstraintSet(node, schema.SuggestedName, objectOnly: false);
            if (set is not null && schema.Kind == OpenApiSemanticSchemaKind.Binary)
            {
                // A file part / binary payload has no JSON value to check.
                SpecNode keywordNode = node.EnumerateObject()
                    .First(property => IsConstraintKeyword(property.Name)).Value;
                AddIgnoredWarning(
                    keywordNode,
                    "Validation keywords on a binary (format: binary) schema cannot be checked and were ignored.");
                return schema;
            }

            return set is null ? schema : schema.WithConstraintSets(new[] { set });
        }

        // Every validation keyword of the dialect (3.1 adds the applicators).
        private bool IsConstraintKeyword(string name)
        {
            switch (name)
            {
                case "minLength":
                case "maxLength":
                case "pattern":
                case "minimum":
                case "maximum":
                case "exclusiveMinimum":
                case "exclusiveMaximum":
                case "multipleOf":
                case "minItems":
                case "maxItems":
                case "uniqueItems":
                case "minProperties":
                case "maxProperties":
                    return true;
                case "patternProperties":
                case "propertyNames":
                case "contains":
                case "minContains":
                case "maxContains":
                case "prefixItems":
                case "dependentRequired":
                    return _minorVersion == 1;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Parses the validation keywords of a schema node. With <paramref name="objectOnly"/> only the
        /// object level keywords are read (the parts of an allOf composition). Returns null when the
        /// node has none.
        /// </summary>
        private OpenApiSemanticConstraints? ParseConstraintSet(
            SpecNode node,
            string suggestedName,
            bool objectOnly)
        {
            var set = new OpenApiSemanticConstraints();
            if (!objectOnly)
            {
                set.MinLength = ReadCount(node, "minLength");
                set.MaxLength = ReadCount(node, "maxLength");
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
            }

            set.MinProperties = ReadCount(node, "minProperties");
            set.MaxProperties = ReadCount(node, "maxProperties");
            if (_minorVersion == 1)
            {
                ParsePatternProperties(node, suggestedName, set);
                SpecNode? propertyNamesNode = GetProperty(node, "propertyNames");
                if (propertyNamesNode is not null)
                {
                    set.PropertyNames = ParseConstraintSchema(propertyNamesNode, suggestedName + "PropertyName");
                }

                ParseDependentRequired(node, set);
                if (!objectOnly)
                {
                    SpecNode? containsNode = GetProperty(node, "contains");
                    if (containsNode is not null)
                    {
                        set.Contains = ParseConstraintSchema(containsNode, suggestedName + "Contains");
                        set.MinContains = ReadCount(node, "minContains");
                        set.MaxContains = ReadCount(node, "maxContains");
                    }
                    else
                    {
                        // minContains / maxContains without contains have no effect; their shape is still checked.
                        ReadCount(node, "minContains");
                        ReadCount(node, "maxContains");
                    }

                    ParsePrefixItems(node, suggestedName, set);
                }
            }

            return set.IsEmpty ? null : set;
        }

        /// <summary>The object level keywords of an allOf part, or null when it has none.</summary>
        private OpenApiSemanticConstraints? ParseObjectConstraintSet(SpecNode node, string suggestedName)
        {
            if (!ObjectConstraintKeywords.Any(keyword => node.TryGetProperty(keyword, out _)))
            {
                return null;
            }

            return ParseConstraintSet(node, suggestedName, objectOnly: true);
        }

        private static IEnumerable<OpenApiSemanticSchema> EnumerateConstraintSchemas(OpenApiSemanticConstraints set)
        {
            foreach (OpenApiSemanticPatternProperty entry in set.PatternProperties)
            {
                yield return entry.Schema;
            }

            if (set.PropertyNames is not null)
            {
                yield return set.PropertyNames;
            }

            if (set.Contains is not null)
            {
                yield return set.Contains;
            }

            foreach (OpenApiSemanticSchema prefix in set.PrefixItems)
            {
                yield return prefix;
            }

            if (set.ItemsAfterPrefix is not null)
            {
                yield return set.ItemsAfterPrefix;
            }
        }

        private OpenApiSemanticSchema ParseConstraintSchema(SpecNode node, string suggestedName)
        {
            return ParseSchema(node, suggestedName, new HashSet<NormalizedSpecNodeIdentity>());
        }

        private static long? ReadCount(SpecNode node, string keyword)
        {
            SpecNode? value = GetProperty(node, keyword);
            if (value is null)
            {
                return null;
            }

            if (value.ValueKind == SpecValueKind.Number)
            {
                if (value.TryGetInt64(out long integer) && integer >= 0)
                {
                    return integer;
                }

                // JSON Schema accepts any number with an integer value (3.0).
                if (!value.IsInteger &&
                    value.TryGetDouble(out double number) &&
                    number >= 0 &&
                    number <= 9007199254740992d &&
                    Math.Floor(number) == number)
                {
                    return (long)number;
                }
            }

            throw Invalid(value, "The schema '" + keyword + "' field must be a non-negative integer.");
        }

        private static string ReadNumberLexeme(SpecNode node, string keyword)
        {
            if (node.ValueKind != SpecValueKind.Number || !node.TryGetDouble(out _))
            {
                throw Invalid(node, "The schema '" + keyword + "' field must be a finite number.");
            }

            return node.GetRawJson();
        }

        private void ParseNumberBounds(SpecNode node, OpenApiSemanticConstraints set)
        {
            SpecNode? minimumNode = GetProperty(node, "minimum");
            SpecNode? maximumNode = GetProperty(node, "maximum");
            SpecNode? exclusiveMinimumNode = GetProperty(node, "exclusiveMinimum");
            SpecNode? exclusiveMaximumNode = GetProperty(node, "exclusiveMaximum");
            string? minimum = minimumNode is null ? null : ReadNumberLexeme(minimumNode, "minimum");
            string? maximum = maximumNode is null ? null : ReadNumberLexeme(maximumNode, "maximum");
            if (_minorVersion == 0)
            {
                // OpenAPI 3.0: exclusiveMinimum / exclusiveMaximum are booleans that turn the bound inclusive or exclusive.
                bool exclusiveMinimum = ReadLegacyExclusive(exclusiveMinimumNode, "exclusiveMinimum");
                bool exclusiveMaximum = ReadLegacyExclusive(exclusiveMaximumNode, "exclusiveMaximum");
                if (exclusiveMinimum)
                {
                    set.ExclusiveMinimum = minimum;
                }
                else
                {
                    set.Minimum = minimum;
                }

                if (exclusiveMaximum)
                {
                    set.ExclusiveMaximum = maximum;
                }
                else
                {
                    set.Maximum = maximum;
                }
            }
            else
            {
                // OpenAPI 3.1: the exclusive bounds are numbers of their own.
                set.Minimum = minimum;
                set.Maximum = maximum;
                set.ExclusiveMinimum = exclusiveMinimumNode is null
                    ? null
                    : ReadNumberLexeme(exclusiveMinimumNode, "exclusiveMinimum");
                set.ExclusiveMaximum = exclusiveMaximumNode is null
                    ? null
                    : ReadNumberLexeme(exclusiveMaximumNode, "exclusiveMaximum");
            }

            SpecNode? multipleOfNode = GetProperty(node, "multipleOf");
            if (multipleOfNode is not null)
            {
                string lexeme = ReadNumberLexeme(multipleOfNode, "multipleOf");
                multipleOfNode.TryGetDouble(out double divisor);
                if (divisor <= 0)
                {
                    throw Invalid(multipleOfNode, "The schema 'multipleOf' field must be greater than zero.");
                }

                set.MultipleOf = lexeme;
            }
        }

        private static bool ReadLegacyExclusive(SpecNode? node, string keyword)
        {
            if (node is null)
            {
                return false;
            }

            if (node.ValueKind != SpecValueKind.True && node.ValueKind != SpecValueKind.False)
            {
                throw Invalid(node, "The schema '" + keyword + "' field must be a boolean in OpenAPI 3.0.");
            }

            return node.ValueKind == SpecValueKind.True;
        }

        private void ParsePatternProperties(SpecNode node, string suggestedName, OpenApiSemanticConstraints set)
        {
            SpecNode? patternPropertiesNode = GetProperty(node, "patternProperties");
            if (patternPropertiesNode is null)
            {
                return;
            }

            RequireKind(
                patternPropertiesNode,
                SpecValueKind.Object,
                "The schema 'patternProperties' field must be an object.");
            var entries = new List<OpenApiSemanticPatternProperty>();
            int index = 1;
            foreach (SpecProperty entry in patternPropertiesNode.EnumerateObject())
            {
                bool ecmaScript = CompilePattern(entry.Value, entry.Name, "patternProperties");
                OpenApiSemanticSchema schema = ParseConstraintSchema(
                    entry.Value,
                    suggestedName + "PatternValue" + (index++).ToString(CultureInfo.InvariantCulture));
                entries.Add(new OpenApiSemanticPatternProperty(entry.Name, ecmaScript, schema));
            }

            set.PatternProperties = entries;
        }

        private static void ParseDependentRequired(SpecNode node, OpenApiSemanticConstraints set)
        {
            SpecNode? dependentNode = GetProperty(node, "dependentRequired");
            if (dependentNode is null)
            {
                return;
            }

            RequireKind(
                dependentNode,
                SpecValueKind.Object,
                "The schema 'dependentRequired' field must be an object.");
            var entries = new List<OpenApiSemanticDependentRequired>();
            foreach (SpecProperty entry in dependentNode.EnumerateObject())
            {
                entries.Add(new OpenApiSemanticDependentRequired(
                    entry.Name,
                    ParseStringArray(
                        entry.Value,
                        "The dependentRequired value of '" + entry.Name + "' must be an array of strings.")));
            }

            set.DependentRequired = entries;
        }

        private void ParsePrefixItems(SpecNode node, string suggestedName, OpenApiSemanticConstraints set)
        {
            SpecNode? prefixNode = GetProperty(node, "prefixItems");
            if (prefixNode is null)
            {
                return;
            }

            RequireKind(prefixNode, SpecValueKind.Array, "The schema 'prefixItems' field must be an array of schemas.");
            var prefix = new List<OpenApiSemanticSchema>();
            int index = 1;
            foreach (SpecNode entry in prefixNode.EnumerateArray())
            {
                prefix.Add(ParseConstraintSchema(
                    entry,
                    suggestedName + "Prefix" + (index++).ToString(CultureInfo.InvariantCulture)));
            }

            set.PrefixItems = prefix;

            // 2020-12: items applies to the elements after the prefix; items: false closes the tuple.
            SpecNode? itemsNode = GetProperty(node, "items");
            if (itemsNode is null || itemsNode.ValueKind == SpecValueKind.True)
            {
                return;
            }

            if (itemsNode.ValueKind == SpecValueKind.False)
            {
                set.MaxItems = set.MaxItems.HasValue ? Math.Min(set.MaxItems.Value, prefix.Count) : prefix.Count;
                return;
            }

            set.ItemsAfterPrefix = ParseConstraintSchema(itemsNode, suggestedName + "Item");
        }

        // Syntax that ECMA-262 does not have: atomic groups, inline options, conditionals, comments, \A \z \Z \G
        // and character class subtraction. Such a pattern is always evaluated in the .NET default mode.
        private static readonly Regex DotNetOnlySyntax = new Regex(
            @"\(\?(?:>|[imnsx-]|\(|#)|\\[AzZG]|-\[",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Checks that a pattern is a valid regular expression and returns whether the generated client
        /// evaluates it with RegexOptions.ECMAScript (\d \w \s \b are ASCII-only as in ECMA-262). A pattern
        /// that uses syntax ECMA-262 lacks, or that the ECMAScript mode rejects, is evaluated with the .NET
        /// default mode and CultureInvariant (the two options cannot be combined); that is reported as OACG108
        /// because \d \w \s \b become Unicode-aware. A pattern that no mode accepts is an invalid document.
        /// </summary>
        private bool CompilePattern(SpecNode node, string pattern, string keyword)
        {
            string error = string.Empty;
            if (!DotNetOnlySyntax.IsMatch(pattern))
            {
                try
                {
                    new Regex(pattern, RegexOptions.ECMAScript, TimeSpan.FromSeconds(1));
                    return true;
                }
                catch (ArgumentException)
                {
                }
            }

            try
            {
                new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
            }

            if (error.Length > 0)
            {
                throw Invalid(
                    node,
                    "The schema " + keyword + " '" + ClipForMessage(pattern) +
                    "' is not a valid regular expression: " + error);
            }

            AddIgnoredWarning(
                node,
                "The " + keyword + " '" + ClipForMessage(pattern) +
                "' uses syntax that is not available in ECMAScript mode, so it is evaluated in the .NET default mode " +
                "(\\d, \\w and \\s are Unicode-aware).");
            return false;
        }

        private static string ClipForMessage(string value)
        {
            return value.Length <= 80 ? value : value.Substring(0, 80) + "...";
        }

        // ---- signature -----------------------------------------------------------------------------

        /// <summary>
        /// The constraint sets of a schema and of the schemas it refers to, as a signature fragment.
        /// Two properties that differ only in a constraint must not look identical.
        /// </summary>
        private string GetConstraintChainSignature(
            OpenApiSemanticSchema schema,
            HashSet<NormalizedSpecNodeIdentity> active)
        {
            var parts = new List<string>();
            var added = new List<NormalizedSpecNodeIdentity>();
            var visited = new HashSet<NormalizedSpecNodeIdentity>();
            var chain = new List<OpenApiSemanticSchema> { schema };
            try
            {
                OpenApiSemanticSchema current = schema;
                while (current.Kind == OpenApiSemanticSchemaKind.Reference)
                {
                    NormalizedSpecNodeIdentity identity = current.ReferenceIdentity;
                    if (identity.IsEmpty ||
                        !visited.Add(identity) ||
                        !_schemas.TryGetValue(identity, out OpenApiSemanticSchema? referenced))
                    {
                        break;
                    }

                    if (active.Add(identity))
                    {
                        added.Add(identity);
                    }

                    chain.Add(referenced);
                    current = referenced;
                }

                foreach (OpenApiSemanticSchema member in chain)
                {
                    foreach (OpenApiSemanticConstraints set in member.ConstraintSets)
                    {
                        parts.Add(GetConstraintSignature(set, active));
                    }
                }
            }
            finally
            {
                foreach (NormalizedSpecNodeIdentity identity in added)
                {
                    active.Remove(identity);
                }
            }

            return string.Join(";", parts);
        }

        private string GetConstraintSignature(
            OpenApiSemanticConstraints set,
            HashSet<NormalizedSpecNodeIdentity> active)
        {
            string text(object? value) => value is null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            var parts = new List<string>
            {
                text(set.MinLength), text(set.MaxLength), set.Pattern ?? string.Empty,
                text(set.Minimum), text(set.Maximum), text(set.ExclusiveMinimum), text(set.ExclusiveMaximum),
                text(set.MultipleOf), text(set.MinItems), text(set.MaxItems), set.UniqueItems ? "u" : "-",
                text(set.MinProperties), text(set.MaxProperties), text(set.MinContains), text(set.MaxContains)
            };
            foreach (OpenApiSemanticPatternProperty entry in set.PatternProperties)
            {
                parts.Add("pp:" + entry.Pattern + "=" + GetSchemaSignature(entry.Schema, active));
            }

            parts.Add(set.PropertyNames is null ? string.Empty : "pn:" + GetSchemaSignature(set.PropertyNames, active));
            parts.Add(set.Contains is null ? string.Empty : "c:" + GetSchemaSignature(set.Contains, active));
            foreach (OpenApiSemanticSchema prefix in set.PrefixItems)
            {
                parts.Add("px:" + GetSchemaSignature(prefix, active));
            }

            parts.Add(set.ItemsAfterPrefix is null ? string.Empty : "ia:" + GetSchemaSignature(set.ItemsAfterPrefix, active));
            foreach (OpenApiSemanticDependentRequired entry in set.DependentRequired)
            {
                parts.Add("dr:" + entry.Property + "=" + string.Join(",", entry.Required));
            }

            return "{" + string.Join("|", parts) + "}";
        }
    }
}
