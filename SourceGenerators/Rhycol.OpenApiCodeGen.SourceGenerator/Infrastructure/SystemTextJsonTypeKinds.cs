using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// System.Text.Json の生成物が必要とする型の種類をモデルから集める（converter の登録に使う）。
    /// composition と constraint は Newtonsoft.Json と同等に生成されるため、ここでは拒否しない。
    /// Collects, from the model, the kinds of the types that the System.Text.Json output needs (the
    /// converters to register). Composition and constraints are generated like the Newtonsoft.Json output,
    /// so nothing is rejected here any more.
    /// </summary>
    internal static class SystemTextJsonTypeKinds
    {
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
            foreach (GeneratedUnionModel union in model.Unions)
            {
                foreach (GeneratedUnionMemberModel member in union.Members)
                {
                    yield return Root(member.Type, "member '" + member.AccessorName + "' of the union '" + union.Name + "'");
                }
            }

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
    }
}
