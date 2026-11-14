using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// 選択した組み合わせでは生成できない機能をスペックが必要とするときの例外。OACG010 で報告される。
    /// Thrown when the spec needs a feature the selected library combination cannot generate yet.
    /// Reported as OACG010.
    /// </summary>
    internal sealed class LibrarySelectionNotSupportedException : Exception
    {
        internal LibrarySelectionNotSupportedException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// System.Text.Json の生成物が（まだ）持たない機能を、モデルから判定する。
    /// composition（ポリモーフィック converter）と constraint の実行時検証は Newtonsoft 版のみ。
    /// Finds, from the model, the features that the System.Text.Json output does not have yet:
    /// the polymorphic converters of composition and the run-time validation of constraints.
    /// </summary>
    internal static class SystemTextJsonFeatureGate
    {
        internal static IReadOnlyList<string> FindUnsupported(OpenApiGenerationModel model)
        {
            if (model is null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var scan = new Scan();
            if (model.Unions.Count > 0)
            {
                scan.Composition ??= "schema '" + model.Unions[0].Name + "'";
            }

            foreach (GeneratedDtoModel dto in model.Dtos)
            {
                if (dto.ConstraintSets.Count > 0)
                {
                    scan.Constraint ??= "schema '" + dto.Name + "'";
                }
            }

            foreach (KeyValuePair<GeneratedTypeModel, string> root in EnumerateRoots(model))
            {
                scan.Visit(root.Key, root.Value);
            }

            var unsupported = new List<string>();
            if (scan.Composition is not null)
            {
                unsupported.Add(
                    "composition (allOf / oneOf / anyOf / discriminator) that needs polymorphic converters (" +
                    scan.Composition + ")");
            }

            if (scan.Constraint is not null)
            {
                unsupported.Add(
                    "run-time validation of constraint keywords (minLength, pattern, minimum, minItems, ...) (" +
                    scan.Constraint + ")");
            }

            if (scan.Not is not null)
            {
                unsupported.Add("run-time validation of the not keyword (" + scan.Not + ")");
            }

            return unsupported;
        }

        /// <summary>Throws when the model needs a feature the System.Text.Json output does not have.</summary>
        internal static void EnsureSupported(OpenApiGenerationModel model)
        {
            IReadOnlyList<string> unsupported = FindUnsupported(model);
            if (unsupported.Count > 0)
            {
                throw new LibrarySelectionNotSupportedException(
                    "JsonSerializer=SystemTextJson does not support " + string.Join("; ", unsupported) +
                    " yet. Use NewtonsoftJson for this spec");
            }
        }

        /// <summary>The kinds of every type the model refers to (nested item / value types included).</summary>
        internal static ISet<GeneratedTypeKind> CollectKinds(OpenApiGenerationModel model)
        {
            var kinds = new HashSet<GeneratedTypeKind>();
            var visited = new HashSet<GeneratedTypeModel>();
            foreach (KeyValuePair<GeneratedTypeModel, string> root in EnumerateRoots(model))
            {
                Collect(root.Key, kinds, visited);
            }

            return kinds;
        }

        private static void Collect(
            GeneratedTypeModel type,
            HashSet<GeneratedTypeKind> kinds,
            HashSet<GeneratedTypeModel> visited)
        {
            if (!visited.Add(type))
            {
                return;
            }

            kinds.Add(type.Kind);
            if (type.ItemType is not null)
            {
                Collect(type.ItemType, kinds, visited);
            }

            if (type.ValueType is not null)
            {
                Collect(type.ValueType, kinds, visited);
            }
        }

        // The types the model refers to, with a description of where each is used.
        private static IEnumerable<KeyValuePair<GeneratedTypeModel, string>> EnumerateRoots(OpenApiGenerationModel model)
        {
            foreach (GeneratedDtoModel dto in model.Dtos)
            {
                foreach (GeneratedDtoPropertyModel property in dto.Properties)
                {
                    yield return Root(property.Type, "property '" + dto.Name + "." + property.Name + "'");
                }

                if (dto.ExtensionDataValueType is not null)
                {
                    yield return Root(dto.ExtensionDataValueType, "additionalProperties of '" + dto.Name + "'");
                }
            }

            foreach (GeneratedOperationModel operation in model.Operations)
            {
                string where = "operation '" + operation.Name + "'";
                foreach (GeneratedParameterModel parameter in operation.Parameters)
                {
                    yield return Root(parameter.Type, "parameter '" + parameter.WireName + "' of " + where);
                }

                if (operation.RequestBody is GeneratedRequestBodyModel body)
                {
                    foreach (GeneratedBodyVariantModel variant in body.Variants)
                    {
                        yield return Root(variant.Type, "the request body of " + where);
                        foreach (GeneratedFormFieldModel field in variant.Fields)
                        {
                            yield return Root(field.Type, "form field '" + field.WireName + "' of " + where);
                        }
                    }
                }

                if (operation.ResponseType is not null)
                {
                    yield return Root(operation.ResponseType, "the response of " + where);
                }

                if (operation.ResponsePlan is GeneratedResponsePlanModel plan)
                {
                    foreach (GeneratedSuccessResponseModel success in plan.Successes)
                    {
                        if (success.Type is not null)
                        {
                            yield return Root(success.Type, "the response of " + where);
                        }

                        foreach (GeneratedResponseHeaderModel header in success.Headers)
                        {
                            yield return Root(header.Type, "response header '" + header.WireName + "' of " + where);
                        }
                    }
                }

                foreach (GeneratedErrorResponseModel error in operation.ErrorResponses)
                {
                    if (error.Type is not null)
                    {
                        yield return Root(error.Type, "the error response of " + where);
                    }
                }
            }
        }

        private static KeyValuePair<GeneratedTypeModel, string> Root(GeneratedTypeModel type, string where)
        {
            return new KeyValuePair<GeneratedTypeModel, string>(type, where);
        }

        private sealed class Scan
        {
            private readonly HashSet<GeneratedTypeModel> _visited = new HashSet<GeneratedTypeModel>();

            internal string? Composition { get; set; }

            internal string? Constraint { get; set; }

            internal string? Not { get; set; }

            internal void Visit(GeneratedTypeModel type, string where)
            {
                if (!_visited.Add(type))
                {
                    return;
                }

                if (type.Kind == GeneratedTypeKind.Union || type.UnionMembers.Count > 0)
                {
                    Composition ??= where;
                }

                if (type.ConstraintSets.Count > 0)
                {
                    Constraint ??= where;
                }

                if (type.NotTypes.Count > 0)
                {
                    Not ??= where;
                }

                if (type.ItemType is not null)
                {
                    Visit(type.ItemType, where);
                }

                if (type.ValueType is not null)
                {
                    Visit(type.ValueType, where);
                }

                foreach (GeneratedTypeModel member in type.UnionMembers)
                {
                    Visit(member, where);
                }

                foreach (GeneratedTypeModel notType in type.NotTypes)
                {
                    Visit(notType, where);
                }
            }
        }
    }
}
