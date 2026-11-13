using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>Union wrappers (oneOf / anyOf) and discriminator values of their member DTOs.</summary>
    internal sealed partial class OpenApiGenerationModelBuilder
    {
        // Taken by the wrapper's static factory for the raw JSON form (FromJToken).
        private static readonly string[] ReservedUnionAccessorNames = { "JToken" };

        private GeneratedUnionModel BuildUnion(OpenApiSemanticSchema schema, string typeName)
        {
            var usedAccessors = new HashSet<string>(ReservedUnionAccessorNames, StringComparer.Ordinal);
            var members = new List<GeneratedUnionMemberModel>(schema.UnionMembers.Count);
            for (int index = 0; index < schema.UnionMembers.Count; index++)
            {
                GeneratedTypeModel type = ResolveType(schema.UnionMembers[index]);
                string accessor = AllocateUniqueName(GetAccessorName(type), usedAccessors);
                members.Add(new GeneratedUnionMemberModel(
                    accessor,
                    type,
                    schema.Discriminator?.MemberValues[index]));
            }

            return new GeneratedUnionModel(
                typeName,
                schema.UnionIsOneOf,
                schema.Discriminator?.PropertyName,
                members,
                schema.Annotations);
        }

        private static string GetAccessorName(GeneratedTypeModel type)
        {
            switch (type.Kind)
            {
                case GeneratedTypeKind.Named:
                case GeneratedTypeKind.NamedEnum:
                case GeneratedTypeKind.Union:
                    string name = type.Name.TrimStart('@');
                    return name.Length == 0 ? "Value" : char.ToUpperInvariant(name[0]) + name.Substring(1);
                case GeneratedTypeKind.String:
                    return "String";
                case GeneratedTypeKind.Int32:
                    return "Int32";
                case GeneratedTypeKind.Int64:
                    return "Int64";
                case GeneratedTypeKind.Single:
                    return "Single";
                case GeneratedTypeKind.Double:
                    return "Double";
                case GeneratedTypeKind.Decimal:
                    return "Decimal";
                case GeneratedTypeKind.Boolean:
                    return "Boolean";
                case GeneratedTypeKind.DateTime:
                    return "Date";
                case GeneratedTypeKind.DateTimeOffset:
                    return "DateTime";
                case GeneratedTypeKind.Guid:
                    return "Guid";
                case GeneratedTypeKind.Binary:
                    return "Bytes";
                case GeneratedTypeKind.FreeObject:
                    return "Object";
                case GeneratedTypeKind.Any:
                    return "Token";
                case GeneratedTypeKind.Array:
                    return GetAccessorName(type.ItemType!) + "List";
                case GeneratedTypeKind.Map:
                    return GetAccessorName(type.ValueType!) + "Map";
                default:
                    return "Value";
            }
        }

        // ---- discriminator values ---------------------------------------------------------------

        /// <summary>
        /// Finds every discriminated union and records, for each member DTO, the discriminator
        /// property and the wire values that select it. A DTO that does not declare the property
        /// gets it (the discriminator is part of the object on the wire), and a string property
        /// that has a single selecting value is initialised with it.
        /// </summary>
        private void CollectDiscriminatorValues()
        {
            foreach (OpenApiSemanticSchema schema in _document.Schemas.Values)
            {
                WalkForDiscriminators(schema);
            }

            foreach (OpenApiSemanticOperation operation in _document.Operations)
            {
                foreach (OpenApiSemanticParameter parameter in operation.Parameters)
                {
                    WalkForDiscriminators(parameter.Schema);
                }

                if (operation.RequestBody is not null)
                {
                    WalkForDiscriminators(operation.RequestBody.Schema);
                }

                if (operation.ResponseSchema is not null)
                {
                    WalkForDiscriminators(operation.ResponseSchema);
                }
            }
        }

        private void WalkForDiscriminators(OpenApiSemanticSchema schema)
        {
            switch (schema.Kind)
            {
                case OpenApiSemanticSchemaKind.Array:
                    if (schema.ItemSchema is not null)
                    {
                        WalkForDiscriminators(schema.ItemSchema);
                    }

                    return;
                case OpenApiSemanticSchemaKind.Map:
                    if (schema.ValueSchema is not null)
                    {
                        WalkForDiscriminators(schema.ValueSchema);
                    }

                    return;
                case OpenApiSemanticSchemaKind.Object:
                    foreach (OpenApiSemanticProperty property in schema.Properties)
                    {
                        WalkForDiscriminators(property.Schema);
                    }

                    if (schema.ValueSchema is not null)
                    {
                        WalkForDiscriminators(schema.ValueSchema);
                    }

                    return;
                case OpenApiSemanticSchemaKind.Union:
                    for (int index = 0; index < schema.UnionMembers.Count; index++)
                    {
                        OpenApiSemanticSchema member = schema.UnionMembers[index];
                        WalkForDiscriminators(member);
                        NormalizedSpecNodeIdentity? key = FindObjectKey(member);
                        if (schema.Discriminator is null || key is null)
                        {
                            continue;
                        }

                        if (!_discriminatorValues.TryGetValue(key.Value, out var list))
                        {
                            list = new List<KeyValuePair<string, IReadOnlyList<string>>>();
                            _discriminatorValues.Add(key.Value, list);
                        }

                        list.Add(new KeyValuePair<string, IReadOnlyList<string>>(
                            schema.Discriminator.PropertyName,
                            schema.Discriminator.MemberValues[index]));
                    }

                    return;
                default:
                    return;
            }
        }

        private NormalizedSpecNodeIdentity? FindObjectKey(OpenApiSemanticSchema member)
        {
            var visited = new HashSet<NormalizedSpecNodeIdentity>();
            OpenApiSemanticSchema current = member;
            while (current.Kind == OpenApiSemanticSchemaKind.Reference)
            {
                NormalizedSpecNodeIdentity identity = current.ReferenceIdentity;
                if (identity.IsEmpty ||
                    !visited.Add(identity) ||
                    !_document.Schemas.TryGetValue(identity, out OpenApiSemanticSchema? next))
                {
                    return null;
                }

                current = next;
            }

            return current.Kind == OpenApiSemanticSchemaKind.Object ? GetSchemaKey(current) : null;
        }

        private void GetDiscriminatorInjections(
            NormalizedSpecNodeIdentity key,
            OpenApiSemanticSchema schema,
            out Dictionary<string, OpenApiSemanticLiteral?> consts,
            out List<OpenApiSemanticProperty> injected)
        {
            consts = new Dictionary<string, OpenApiSemanticLiteral?>(StringComparer.Ordinal);
            injected = new List<OpenApiSemanticProperty>();
            if (!_discriminatorValues.TryGetValue(key, out var entries))
            {
                return;
            }

            foreach (IGrouping<string, KeyValuePair<string, IReadOnlyList<string>>> group in entries
                         .GroupBy(static entry => entry.Key, StringComparer.Ordinal))
            {
                // One value that is the same in every union that contains the DTO can be a const;
                // otherwise the discriminator property is a plain string that the union writes.
                string[] distinct = group
                    .SelectMany(static entry => entry.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                OpenApiSemanticLiteral? constValue = distinct.Length == 1
                    ? OpenApiSemanticLiteral.FromString(distinct[0])
                    : (OpenApiSemanticLiteral?)null;
                consts[group.Key] = constValue;
                if (schema.Properties.Any(property => string.Equals(property.WireName, group.Key, StringComparison.Ordinal)))
                {
                    continue;
                }

                // Injected members are never required: whether the discriminator is present and
                // what it says is decided by the union (converter and contract), not by the DTO,
                // so the DTO stays valid when it is used without its union.
                injected.Add(new OpenApiSemanticProperty(
                    group.Key,
                    false,
                    new OpenApiSemanticSchema(
                        OpenApiSemanticSchemaKind.String,
                        group.Key,
                        string.Empty,
                        false,
                        string.Empty,
                        Array.Empty<OpenApiSemanticProperty>(),
                        null,
                        Array.Empty<string>(),
                        schema.Location),
                    schema.Location));
            }
        }

        // The selecting value is only an initial value (what a request sends); it is not validated.
        private static GeneratedDtoPropertyModel WithDiscriminatorInitialValue(
            GeneratedDtoPropertyModel property,
            Dictionary<string, OpenApiSemanticLiteral?> consts)
        {
            if (!consts.TryGetValue(property.WireName, out OpenApiSemanticLiteral? literal) ||
                !literal.HasValue ||
                property.Type.Kind != GeneratedTypeKind.String ||
                property.Type.ConstValue.HasValue)
            {
                return property;
            }

            return new GeneratedDtoPropertyModel(
                property.Name,
                property.WireName,
                property.Required,
                property.UseSpecified,
                property.Type,
                property.Annotations,
                literal);
        }
    }
}
