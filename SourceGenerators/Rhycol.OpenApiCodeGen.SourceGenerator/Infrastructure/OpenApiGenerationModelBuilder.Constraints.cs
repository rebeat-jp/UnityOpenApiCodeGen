using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>Maps the validation keyword sets of a schema (P6) to the generation model.</summary>
    internal sealed partial class OpenApiGenerationModelBuilder
    {
        /// <summary>
        /// Attaches the constraint sets of <paramref name="schema"/> to its type. A DTO (Object schema)
        /// keeps its own sets in its model; a $ref site and every other schema carry theirs on the type.
        /// </summary>
        private GeneratedTypeModel AttachConstraints(GeneratedTypeModel type, OpenApiSemanticSchema schema)
        {
            // An enum is checked on its wire value; a union takes no sibling keywords (the parser rejects
            // them) and a file part has no JSON value (the parser warns).
            if (schema.ConstraintSets.Count == 0 ||
                schema.Kind == OpenApiSemanticSchemaKind.Object ||
                schema.Kind == OpenApiSemanticSchemaKind.Union ||
                type.Kind == GeneratedTypeKind.File ||
                type.Kind == GeneratedTypeKind.Union)
            {
                return type;
            }

            return type.WithConstraints(ConvertConstraintSets(schema.ConstraintSets));
        }

        private IReadOnlyList<GeneratedConstraints> ConvertConstraintSets(
            IReadOnlyList<OpenApiSemanticConstraints> sets)
        {
            return sets.Select(ConvertConstraints).ToArray();
        }

        private GeneratedConstraints ConvertConstraints(OpenApiSemanticConstraints set)
        {
            var result = new GeneratedConstraints
            {
                MinLength = set.MinLength,
                MaxLength = set.MaxLength,
                Pattern = set.Pattern,
                PatternEcmaScript = set.PatternEcmaScript,
                Minimum = set.Minimum,
                Maximum = set.Maximum,
                ExclusiveMinimum = set.ExclusiveMinimum,
                ExclusiveMaximum = set.ExclusiveMaximum,
                MultipleOf = set.MultipleOf,
                MinItems = set.MinItems,
                MaxItems = set.MaxItems,
                UniqueItems = set.UniqueItems,
                MinProperties = set.MinProperties,
                MaxProperties = set.MaxProperties,
                MinContains = set.MinContains,
                MaxContains = set.MaxContains,
                DependentRequired = set.DependentRequired
                    .Select(static entry => new GeneratedDependentRequired(entry.Property, entry.Required))
                    .ToArray()
            };
            result.PatternProperties = set.PatternProperties
                .Select(entry => new GeneratedPatternProperty(
                    entry.Pattern, entry.EcmaScript, ResolveType(entry.Schema)))
                .ToArray();
            result.PropertyNames = set.PropertyNames is null ? null : ResolveType(set.PropertyNames);
            result.Contains = set.Contains is null ? null : ResolveType(set.Contains);
            result.PrefixItems = set.PrefixItems.Select(ResolveType).ToArray();
            result.ItemsAfterPrefix = set.ItemsAfterPrefix is null ? null : ResolveType(set.ItemsAfterPrefix);
            return result;
        }
    }
}
