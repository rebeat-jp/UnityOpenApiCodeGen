using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis.CSharp;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed partial class OpenApiGenerationModelBuilder
    {
        private readonly OpenApiSemanticDocument _document;
        private readonly GeneratorOptions _options;
        private readonly Dictionary<NormalizedSpecNodeIdentity, string> _componentTypeNames =
            new Dictionary<NormalizedSpecNodeIdentity, string>();
        private readonly Dictionary<NormalizedSpecNodeIdentity, string> _schemaTypeNames =
            new Dictionary<NormalizedSpecNodeIdentity, string>();
        private readonly HashSet<string> _usedTypeNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<GeneratedDtoModel> _dtos = new List<GeneratedDtoModel>();
        private readonly List<GeneratedEnumModel> _enums = new List<GeneratedEnumModel>();
        private readonly List<GeneratedUnionModel> _unions = new List<GeneratedUnionModel>();
        // Discriminator values that select a DTO, per DTO (see CollectDiscriminatorValues).
        private readonly Dictionary<NormalizedSpecNodeIdentity, List<KeyValuePair<string, IReadOnlyList<string>>>> _discriminatorValues =
            new Dictionary<NormalizedSpecNodeIdentity, List<KeyValuePair<string, IReadOnlyList<string>>>>();
        private readonly HashSet<NormalizedSpecNodeIdentity> _buildingSchemas =
            new HashSet<NormalizedSpecNodeIdentity>();
        private readonly List<GeneratedTypeNameCollision> _typeNameCollisions = new List<GeneratedTypeNameCollision>();
        private readonly Dictionary<string, TypeNameAllocation> _typeNameAllocations =
            new Dictionary<string, TypeNameAllocation>(StringComparer.Ordinal);

        private OpenApiGenerationModelBuilder(OpenApiSemanticDocument document, GeneratorOptions options)
        {
            _document = document;
            _options = options;
            ReserveTypeName(options.ApiName, document.Location, emitsDeclaration: true);
            ReserveTypeName(options.ApiName + "Exception", document.Location, emitsDeclaration: true);
            foreach (string memberName in new[]
                     {
                         "Servers", "BuildServerUrl", "ExpandServerUrl",
                         "OperationServerUrls", "ResolveOperationServerUrl", "OperationNames", "GetOperationServers", "BuildOperationServerUrl"
                     })
            {
                // Generated client members; a schema type with the same name would be shadowed by them.
                if (!_typeNameAllocations.ContainsKey(memberName))
                {
                    ReserveTypeName(memberName, document.Location, emitsDeclaration: false);
                }
            }
        }

        internal static OpenApiGenerationModel Build(
            OpenApiSemanticDocument document,
            GeneratorOptions options)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var builder = new OpenApiGenerationModelBuilder(document, options);
            return builder.BuildCore();
        }

        private OpenApiGenerationModel BuildCore()
        {
            AllocateComponentTypeNames();
            CollectDiscriminatorValues();
            foreach (KeyValuePair<NormalizedSpecNodeIdentity, OpenApiSemanticSchema> pair in _document.Schemas
                         .OrderBy(static value => value.Key.DocumentId, StringComparer.Ordinal)
                         .ThenBy(static value => value.Key.Pointer, StringComparer.Ordinal))
            {
                EnsureDeclaration(pair.Value, _componentTypeNames[pair.Key]);
            }

            IReadOnlyList<GeneratedOperationModel> operations = BuildOperations();
            return new OpenApiGenerationModel(
                _options.ApiName,
                _options.GeneratedNamespace,
                _document.BaseUrl,
                _dtos.OrderBy(static value => value.Name, StringComparer.Ordinal).ToArray(),
                _enums.OrderBy(static value => value.Name, StringComparer.Ordinal).ToArray(),
                operations,
                _typeNameCollisions.ToArray(),
                _document.Warnings,
                _unions.OrderBy(static value => value.Name, StringComparer.Ordinal).ToArray(),
                _document.Servers);
        }

        private void AllocateComponentTypeNames()
        {
            foreach (KeyValuePair<NormalizedSpecNodeIdentity, OpenApiSemanticSchema> schema in _document.Schemas
                         .OrderBy(static value => string.Equals(
                             value.Value.Location.DocumentId,
                             "root",
                             StringComparison.Ordinal)
                             ? 0
                             : 1)
                         .ThenBy(static value => value.Key.DocumentId, StringComparer.Ordinal)
                         .ThenBy(static value => value.Key.Pointer, StringComparer.Ordinal))
            {
                string requestedName = ToIdentifier(schema.Value.SuggestedName, pascalCase: true);
                string allocatedName = AllocateSchemaTypeName(
                    requestedName,
                    schema.Value.Location,
                    EmitsDeclaration(schema.Value));
                _componentTypeNames.Add(schema.Key, allocatedName);
            }
        }

        private IReadOnlyList<GeneratedOperationModel> BuildOperations()
        {
            var usedMethodNames = new HashSet<string>(StringComparer.Ordinal)
            {
                _options.ApiName,
                "_httpClient",
                "_baseUrl",
                "DateOnlyJsonConverterInstance",
                "CreateRequestUri",
                "CombineAbsoluteUri",
                "CombinePaths",
                "CombineQueries",
                "AppendQuery",
                "SplitPathAndQuery",
                "ValidatePathSegments",
                "CreateJsonContent",
                "ConvertToString",
                "DateOnlyJsonConverter",
                "ReadJsonResponseBodyAsync",
                "HasPreamble",
                "Servers",
                "BuildServerUrl",
                "ExpandServerUrl",
                "OperationServerUrls",
                "ResolveOperationServerUrl",
                "OperationNames",
                "GetOperationServers",
                "BuildOperationServerUrl"
            };
            var result = new List<GeneratedOperationModel>();
            foreach (OpenApiSemanticOperation operation in _document.Operations
                         .OrderBy(static value => value.Path, StringComparer.Ordinal)
                         .ThenBy(static value => value.HttpMethod, StringComparer.Ordinal))
            {
                string methodName = AllocateUniqueName(
                    ToIdentifier(operation.OperationId, pascalCase: false),
                    usedMethodNames);
                var usedParameterNames = new HashSet<string>(StringComparer.Ordinal)
                {
                    "cancellationToken",
                    "relativePath",
                    "queryParts",
                    "cookieParts",
                    "requestUri",
                    "request",
                    "response",
                    "responseBody",
                    "validatedResponseBody",
                    "defaultResponseSettings",
                    "responseSerializer",
                    "deserializedResponse",
                    "requestJson",
                    "_httpClient",
                    "CreateRequestUri",
                    "ConvertToString",
                    "ReadJsonResponseBodyAsync",
                    "HasPreamble"
                };
                var parameters = new List<GeneratedParameterModel>();
                foreach (OpenApiSemanticParameter parameter in operation.Parameters
                             .OrderBy(static value => value.Identity, StringComparer.Ordinal))
                {
                    string parameterName = AllocateUniqueName(
                        ToIdentifier(parameter.Name, pascalCase: false),
                        usedParameterNames);
                    GeneratedTypeModel resolvedType = ResolveType(parameter.Schema);
                    GeneratedTypeModel type = resolvedType.WithNullable(
                        resolvedType.Nullable || parameter.Schema.Nullable || !parameter.Required);
                    parameters.Add(new GeneratedParameterModel(
                        parameterName,
                        parameter.Name,
                        parameter.LocationName,
                        parameter.Required,
                        type,
                        parameter.Style,
                        parameter.Explode,
                        parameter.AllowReserved,
                        parameter.ContentMediaType,
                        parameter.Description,
                        parameter.Deprecated));
                }

                GeneratedRequestBodyModel? requestBody = null;
                if (operation.RequestBody is not null)
                {
                    string parameterName = AllocateUniqueName("body", usedParameterNames);
                    GeneratedTypeModel resolvedType = ResolveType(operation.RequestBody.Schema);
                    GeneratedTypeModel type = resolvedType.WithNullable(
                        resolvedType.Nullable ||
                        operation.RequestBody.Schema.Nullable ||
                        !operation.RequestBody.Required);
                    string? specifiedParameterName = !operation.RequestBody.Required &&
                                                     (resolvedType.Nullable || operation.RequestBody.Schema.Nullable)
                        ? AllocateUniqueName(parameterName + "Specified", usedParameterNames)
                        : null;
                    requestBody = new GeneratedRequestBodyModel(
                        parameterName,
                        operation.RequestBody.Required,
                        operation.RequestBody.MediaType,
                        type,
                        specifiedParameterName);
                }

                GeneratedTypeModel? responseType = null;
                if (operation.ResponseSchema is not null)
                {
                    GeneratedTypeModel resolvedType = ResolveType(operation.ResponseSchema);
                    responseType = resolvedType.WithNullable(
                        resolvedType.Nullable || operation.ResponseSchema.Nullable);
                }
                result.Add(new GeneratedOperationModel(
                    methodName,
                    operation.Summary,
                    operation.HttpMethod,
                    operation.Path,
                    parameters,
                    requestBody,
                    responseType,
                    operation.SuccessStatusCodes,
                    operation.ResponseMediaTypes,
                    operation.Description,
                    operation.Deprecated,
                    operation.Servers));
            }

            return result;
        }

        private GeneratedTypeModel ResolveType(OpenApiSemanticSchema schema)
        {
            GeneratedTypeModel type = ResolveTypeCore(schema);
            foreach (OpenApiSemanticSchema notSchema in schema.NotSchemas)
            {
                type = type.WithNot(ResolveType(notSchema));
            }

            return type;
        }

        private GeneratedTypeModel ResolveTypeCore(OpenApiSemanticSchema schema)
        {
            switch (schema.Kind)
            {
                case OpenApiSemanticSchemaKind.String:
                    return AttachConst(ResolveStringType(schema), schema);
                case OpenApiSemanticSchemaKind.Integer:
                    return AttachConst(
                        new GeneratedTypeModel(
                            string.Equals(schema.Format, "int64", StringComparison.Ordinal)
                                ? GeneratedTypeKind.Int64
                                : GeneratedTypeKind.Int32,
                            schema.Nullable),
                        schema);
                case OpenApiSemanticSchemaKind.Number:
                    return AttachConst(
                        new GeneratedTypeModel(ResolveNumberKind(schema.Format), schema.Nullable),
                        schema);
                case OpenApiSemanticSchemaKind.Boolean:
                    return AttachConst(new GeneratedTypeModel(GeneratedTypeKind.Boolean, schema.Nullable), schema);
                case OpenApiSemanticSchemaKind.Any:
                    // JToken can hold JSON null, so it is always nullable.
                    return new GeneratedTypeModel(GeneratedTypeKind.Any, true);
                case OpenApiSemanticSchemaKind.FreeObject:
                    return new GeneratedTypeModel(GeneratedTypeKind.FreeObject, schema.Nullable);
                case OpenApiSemanticSchemaKind.Map:
                    return new GeneratedTypeModel(
                        GeneratedTypeKind.Map,
                        schema.Nullable,
                        valueType: ResolveType(schema.ValueSchema!));
                case OpenApiSemanticSchemaKind.Array:
                    return new GeneratedTypeModel(
                        GeneratedTypeKind.Array,
                        schema.Nullable,
                        itemType: ResolveType(schema.ItemSchema!));
                case OpenApiSemanticSchemaKind.Reference:
                    NormalizedSpecNodeIdentity referenceIdentity = schema.ReferenceIdentity;
                    if (referenceIdentity.IsEmpty)
                    {
                        referenceIdentity = new NormalizedSpecNodeIdentity(
                            schema.Location.DocumentId,
                            "/components/schemas/" + schema.ReferenceName);
                    }

                    if (!_document.Schemas.TryGetValue(referenceIdentity, out OpenApiSemanticSchema? referencedSchema))
                    {
                        throw new InvalidOperationException(
                            "Semantic model is missing referenced schema '" + schema.ReferenceName + "'.");
                    }

                    if (referencedSchema.Kind == OpenApiSemanticSchemaKind.Object ||
                        referencedSchema.Kind == OpenApiSemanticSchemaKind.Enum ||
                        referencedSchema.Kind == OpenApiSemanticSchemaKind.Union)
                    {
                        GeneratedTypeModel named = new GeneratedTypeModel(
                            referencedSchema.Kind == OpenApiSemanticSchemaKind.Enum
                                ? GeneratedTypeKind.NamedEnum
                                : referencedSchema.Kind == OpenApiSemanticSchemaKind.Union
                                    ? GeneratedTypeKind.Union
                                    : GeneratedTypeKind.Named,
                            schema.Nullable || referencedSchema.Nullable,
                            _componentTypeNames[referenceIdentity]);
                        foreach (OpenApiSemanticSchema notSchema in referencedSchema.NotSchemas)
                        {
                            named = named.WithNot(ResolveType(notSchema));
                        }

                        return named;
                    }

                    GeneratedTypeModel resolvedType = ResolveType(referencedSchema);
                    return resolvedType.WithNullable(
                        resolvedType.Nullable || schema.Nullable || referencedSchema.Nullable);
                case OpenApiSemanticSchemaKind.Object:
                case OpenApiSemanticSchemaKind.Enum:
                case OpenApiSemanticSchemaKind.Union:
                    string typeName = GetOrAllocateSchemaTypeName(schema);
                    EnsureDeclaration(schema, typeName);
                    return new GeneratedTypeModel(
                        schema.Kind == OpenApiSemanticSchemaKind.Enum
                            ? GeneratedTypeKind.NamedEnum
                            : schema.Kind == OpenApiSemanticSchemaKind.Union
                                ? GeneratedTypeKind.Union
                                : GeneratedTypeKind.Named,
                        schema.Nullable,
                        typeName);
                default:
                    throw new InvalidOperationException("Unknown semantic schema kind.");
            }
        }

        private GeneratedTypeModel ResolveStringType(OpenApiSemanticSchema schema)
        {
            GeneratedTypeKind kind;
            if (string.Equals(schema.Format, "date-time", StringComparison.Ordinal))
            {
                kind = GeneratedTypeKind.DateTimeOffset;
            }
            else if (string.Equals(schema.Format, "date", StringComparison.Ordinal))
            {
                kind = GeneratedTypeKind.DateTime;
            }
            else if (string.Equals(schema.Format, "uuid", StringComparison.Ordinal))
            {
                kind = GeneratedTypeKind.Guid;
            }
            else if (string.Equals(schema.Format, "byte", StringComparison.Ordinal))
            {
                // format: byte is a base64 string; Json.NET reads and writes byte[] as base64.
                kind = GeneratedTypeKind.Binary;
            }
            else
            {
                kind = GeneratedTypeKind.String;
            }

            return new GeneratedTypeModel(kind, schema.Nullable);
        }

        private static GeneratedTypeModel AttachConst(GeneratedTypeModel type, OpenApiSemanticSchema schema)
        {
            return schema.Annotations.Const.HasValue ? type.WithConst(schema.Annotations.Const) : type;
        }

        /// <summary>
        /// Property-level annotations. readOnly / writeOnly declared on a referenced schema also
        /// apply to the property that references it; the other annotations describe the property
        /// itself and are not inherited.
        /// </summary>
        private OpenApiSemanticAnnotations GetPropertyAnnotations(OpenApiSemanticSchema schema)
        {
            OpenApiSemanticAnnotations own = schema.Annotations;
            bool readOnly = own.ReadOnly;
            bool writeOnly = own.WriteOnly;
            var visited = new HashSet<NormalizedSpecNodeIdentity>();
            OpenApiSemanticSchema current = schema;
            while (current.Kind == OpenApiSemanticSchemaKind.Reference &&
                   !current.ReferenceIdentity.IsEmpty &&
                   visited.Add(current.ReferenceIdentity) &&
                   _document.Schemas.TryGetValue(current.ReferenceIdentity, out OpenApiSemanticSchema? referenced))
            {
                readOnly |= referenced.Annotations.ReadOnly;
                writeOnly |= referenced.Annotations.WriteOnly;
                current = referenced;
            }

            return readOnly == own.ReadOnly && writeOnly == own.WriteOnly
                ? own
                : new OpenApiSemanticAnnotations(
                    own.Description,
                    own.Deprecated,
                    readOnly,
                    writeOnly,
                    own.Default,
                    own.Const);
        }

        private static GeneratedTypeKind ResolveNumberKind(string format)
        {
            if (string.Equals(format, "float", StringComparison.Ordinal))
            {
                return GeneratedTypeKind.Single;
            }

            if (string.Equals(format, "decimal", StringComparison.Ordinal))
            {
                return GeneratedTypeKind.Decimal;
            }

            return GeneratedTypeKind.Double;
        }

        private void EnsureDeclaration(OpenApiSemanticSchema schema, string typeName)
        {
            NormalizedSpecNodeIdentity key = GetSchemaKey(schema);
            if (!_buildingSchemas.Add(key))
            {
                return;
            }

            try
            {
                if (_dtos.Any(value => value.Name == typeName) ||
                    _enums.Any(value => value.Name == typeName) ||
                    _unions.Any(value => value.Name == typeName))
                {
                    return;
                }

                if (schema.Kind == OpenApiSemanticSchemaKind.Union)
                {
                    _unions.Add(BuildUnion(schema, typeName));
                    return;
                }

                if (schema.Kind == OpenApiSemanticSchemaKind.Enum)
                {
                    _enums.Add(BuildEnum(schema, typeName));
                    return;
                }

                if (schema.Kind != OpenApiSemanticSchemaKind.Object)
                {
                    return;
                }

                var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal)
                {
                    typeName
                };
                GetDiscriminatorInjections(
                    key,
                    schema,
                    out Dictionary<string, OpenApiSemanticLiteral?> discriminatorConsts,
                    out List<OpenApiSemanticProperty> injectedProperties);
                OpenApiSemanticProperty[] orderedProperties = schema.Properties
                    .Concat(injectedProperties)
                    .OrderBy(static value => value.WireName, StringComparer.Ordinal)
                    .ToArray();
                var propertyTypes = orderedProperties.Select(property => new
                {
                    Property = property,
                    ResolvedType = ResolveType(property.Schema)
                }).ToArray();
                var allocatedProperties = new Dictionary<OpenApiSemanticProperty, GeneratedDtoPropertyModel>();
                foreach (var item in propertyTypes
                             .OrderByDescending(static value => !value.Property.Required && value.ResolvedType.Nullable))
                {
                    OpenApiSemanticProperty property = item.Property;
                    GeneratedTypeModel resolvedType = item.ResolvedType;
                    bool useSpecified = !property.Required && resolvedType.Nullable;
                    string requestedName = ToIdentifier(property.WireName, pascalCase: true);
                    string propertyName = useSpecified
                        ? AllocateUniqueSpecifiedPropertyName(requestedName, usedPropertyNames)
                        : AllocateUniqueName(requestedName, usedPropertyNames);
                    GeneratedTypeModel type = resolvedType.WithNullable(
                        resolvedType.Nullable || !property.Required);
                    allocatedProperties.Add(property, new GeneratedDtoPropertyModel(
                        propertyName,
                        property.WireName,
                        property.Required,
                        useSpecified,
                        type,
                        GetPropertyAnnotations(property.Schema)));
                }

                // Json.NET treats any CLR member named FooSpecified as Foo's presence flag,
                // even when that member came from the schema instead of our generated marker.
                foreach (OpenApiSemanticProperty property in orderedProperties)
                {
                    GeneratedDtoPropertyModel member = allocatedProperties[property];
                    if (!allocatedProperties.Values.Any(other =>
                            !ReferenceEquals(other, member) &&
                            string.Equals(member.Name, other.Name + "Specified", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    string safeName = AllocateJsonNetSafePropertyName(
                        member, usedPropertyNames, allocatedProperties.Values);
                    allocatedProperties[property] = new GeneratedDtoPropertyModel(
                        safeName,
                        member.WireName,
                        member.Required,
                        member.UseSpecified,
                        member.Type,
                        member.Annotations);
                }

                // A readOnly property gets a ShouldSerialize<Name>() method; make sure no other
                // member (or the type itself) already owns that name.
                foreach (OpenApiSemanticProperty property in orderedProperties)
                {
                    GeneratedDtoPropertyModel member = allocatedProperties[property];
                    if (!member.Annotations.ReadOnly)
                    {
                        continue;
                    }

                    string readOnlyName = member.Name;
                    if (usedPropertyNames.Contains("ShouldSerialize" + readOnlyName))
                    {
                        readOnlyName = AllocateShouldSerializeSafeName(
                            member, usedPropertyNames, allocatedProperties.Values);
                        allocatedProperties[property] = new GeneratedDtoPropertyModel(
                            readOnlyName,
                            member.WireName,
                            member.Required,
                            member.UseSpecified,
                            member.Type,
                            member.Annotations);
                    }

                    usedPropertyNames.Add("ShouldSerialize" + readOnlyName);
                }

                string? extensionDataName = null;
                GeneratedTypeModel? extensionDataValueType = null;
                if (schema.AdditionalProperties != OpenApiSemanticAdditionalProperties.Closed)
                {
                    // Undeclared members are kept in an IDictionary<string, JToken> member.
                    extensionDataName = AllocateUniqueName("AdditionalProperties", usedPropertyNames);
                    if (schema.ValueSchema is not null)
                    {
                        extensionDataValueType = ResolveType(schema.ValueSchema);
                    }
                }

                _dtos.Add(new GeneratedDtoModel(
                    typeName,
                    orderedProperties.Select(value => WithDiscriminatorInitialValue(allocatedProperties[value], discriminatorConsts)).ToArray(),
                    schema.Annotations,
                    extensionDataName,
                    extensionDataValueType));
            }
            finally
            {
                _buildingSchemas.Remove(key);
            }
        }

        private GeneratedEnumModel BuildEnum(OpenApiSemanticSchema schema, string typeName)
        {
            var usedNames = new HashSet<string>(StringComparer.Ordinal)
            {
                typeName
            };
            bool isInteger = schema.TypedEnumValues.Count > 0 &&
                             schema.TypedEnumValues.All(static value =>
                                 value.Kind == OpenApiSemanticLiteralKind.Integer);

            // Each value keeps its position so x-enum-varnames / x-enum-descriptions line up.
            var indexed = new List<(OpenApiSemanticLiteral Value, int Index)>();
            for (int index = 0; index < schema.TypedEnumValues.Count; index++)
            {
                indexed.Add((schema.TypedEnumValues[index], index));
            }

            IEnumerable<(OpenApiSemanticLiteral Value, int Index)> ordered = isInteger
                ? indexed.OrderBy(static item => long.Parse(
                    item.Value.RawText,
                    System.Globalization.CultureInfo.InvariantCulture))
                : indexed.OrderBy(static item => item.Value.RawText, StringComparer.Ordinal);
            var members = new List<GeneratedEnumMemberModel>();
            foreach ((OpenApiSemanticLiteral value, int index) in ordered)
            {
                string? declaredName = index < schema.EnumMemberNames.Count
                    ? schema.EnumMemberNames[index]
                    : null;
                string? description = index < schema.EnumMemberDescriptions.Count
                    ? schema.EnumMemberDescriptions[index]
                    : null;
                string requestedName;
                if (!string.IsNullOrWhiteSpace(declaredName))
                {
                    requestedName = ToIdentifier(declaredName!, pascalCase: true);
                }
                else if (isInteger)
                {
                    requestedName = value.RawText.StartsWith("-", StringComparison.Ordinal)
                        ? "ValueMinus" + value.RawText.Substring(1)
                        : "Value" + value.RawText;
                }
                else
                {
                    requestedName = ToIdentifier(value.RawText, pascalCase: true);
                }

                string name = AllocateUniqueName(requestedName, usedNames);
                members.Add(new GeneratedEnumMemberModel(name, value.RawText, value, description));
            }

            return new GeneratedEnumModel(typeName, members, schema.Annotations);
        }

        private string GetOrAllocateSchemaTypeName(OpenApiSemanticSchema schema)
        {
            NormalizedSpecNodeIdentity key = GetSchemaKey(schema);
            if (_schemaTypeNames.TryGetValue(key, out string? existing))
            {
                return existing;
            }

            string name = AllocateSchemaTypeName(
                ToIdentifier(schema.SuggestedName, pascalCase: true),
                schema.Location,
                emitsDeclaration: true);
            _schemaTypeNames.Add(key, name);
            return name;
        }

        private static NormalizedSpecNodeIdentity GetSchemaKey(OpenApiSemanticSchema schema)
        {
            return schema.Identity.IsEmpty
                ? new NormalizedSpecNodeIdentity(schema.Location.DocumentId, schema.Location.LogicalPath)
                : schema.Identity;
        }

        private string AllocateSchemaTypeName(
            string requestedName,
            OpenApiSourceLocation location,
            bool emitsDeclaration)
        {
            string allocatedName = AllocateUniqueName(requestedName, _usedTypeNames);
            if (!string.Equals(requestedName, allocatedName, StringComparison.Ordinal) &&
                emitsDeclaration &&
                _typeNameAllocations.TryGetValue(requestedName, out TypeNameAllocation? owner) &&
                owner.EmitsDeclaration)
            {
                _typeNameCollisions.Add(new GeneratedTypeNameCollision(
                    requestedName,
                    allocatedName,
                    location,
                    owner.Location));
            }

            _typeNameAllocations.Add(allocatedName, new TypeNameAllocation(location, emitsDeclaration));
            return allocatedName;
        }

        private void ReserveTypeName(
            string name,
            OpenApiSourceLocation location,
            bool emitsDeclaration)
        {
            _usedTypeNames.Add(name);
            _typeNameAllocations.Add(name, new TypeNameAllocation(location, emitsDeclaration));
        }

        private static bool EmitsDeclaration(OpenApiSemanticSchema schema)
        {
            return schema.Kind == OpenApiSemanticSchemaKind.Object ||
                   schema.Kind == OpenApiSemanticSchemaKind.Enum ||
                   schema.Kind == OpenApiSemanticSchemaKind.Union;
        }

        private static string AllocateUniqueName(string name, HashSet<string> usedNames)
        {
            string baseName = string.IsNullOrEmpty(name) ? "Value" : name;
            if (usedNames.Add(baseName))
            {
                return baseName;
            }

            int suffix = 2;
            while (!usedNames.Add(baseName + suffix))
            {
                suffix++;
            }

            return baseName + suffix;
        }

        private static string AllocateUniqueSpecifiedPropertyName(
            string name,
            HashSet<string> usedNames)
        {
            string baseName = string.IsNullOrEmpty(name) ? "Value" : name;
            string candidate = baseName;
            int suffix = 2;
            while (usedNames.Contains(candidate) || usedNames.Contains(candidate + "Specified"))
            {
                candidate = baseName + suffix++;
            }

            usedNames.Add(candidate);
            // Json.NET discovers this exact suffix when deciding whether to write a property.
            usedNames.Add(candidate + "Specified");
            return candidate;
        }

        private static string AllocateJsonNetSafePropertyName(
            GeneratedDtoPropertyModel member,
            HashSet<string> usedNames,
            IEnumerable<GeneratedDtoPropertyModel> members)
        {
            string baseName = member.Name;
            string candidate = baseName;
            int suffix = 2;
            while (usedNames.Contains(candidate) ||
                   (member.UseSpecified && usedNames.Contains(candidate + "Specified")) ||
                   members.Any(other =>
                       !ReferenceEquals(other, member) &&
                       (string.Equals(candidate, other.Name + "Specified", StringComparison.Ordinal) ||
                        string.Equals(other.Name, candidate + "Specified", StringComparison.Ordinal))))
            {
                candidate = baseName + suffix++;
            }

            usedNames.Add(candidate);
            if (member.UseSpecified)
            {
                usedNames.Add(candidate + "Specified");
            }
            return candidate;
        }

        private static string AllocateShouldSerializeSafeName(
            GeneratedDtoPropertyModel member,
            HashSet<string> usedNames,
            IEnumerable<GeneratedDtoPropertyModel> members)
        {
            string baseName = member.Name;
            string candidate = baseName + 2;
            int suffix = 3;
            while (usedNames.Contains(candidate) ||
                   usedNames.Contains("ShouldSerialize" + candidate) ||
                   (member.UseSpecified && usedNames.Contains(candidate + "Specified")) ||
                   members.Any(other =>
                       !ReferenceEquals(other, member) &&
                       (string.Equals(candidate, other.Name + "Specified", StringComparison.Ordinal) ||
                        string.Equals(other.Name, candidate + "Specified", StringComparison.Ordinal))))
            {
                candidate = baseName + suffix++;
            }

            usedNames.Add(candidate);
            if (member.UseSpecified)
            {
                usedNames.Add(candidate + "Specified");
            }

            return candidate;
        }

        private sealed class TypeNameAllocation
        {
            internal TypeNameAllocation(OpenApiSourceLocation location, bool emitsDeclaration)
            {
                Location = location;
                EmitsDeclaration = emitsDeclaration;
            }

            internal OpenApiSourceLocation Location { get; }

            internal bool EmitsDeclaration { get; }
        }

        internal static string ToIdentifier(string value, bool pascalCase)
        {
            var builder = new StringBuilder(value?.Length ?? 0);
            bool uppercaseNext = pascalCase;
            if (value is not null)
            {
                foreach (char character in value)
                {
                    if (!char.IsLetterOrDigit(character) && character != '_')
                    {
                        uppercaseNext = pascalCase;
                        continue;
                    }

                    char output = uppercaseNext ? char.ToUpperInvariant(character) : character;
                    if (builder.Length == 0 && char.IsDigit(output))
                    {
                        builder.Append('_');
                    }

                    builder.Append(output);
                    uppercaseNext = false;
                }
            }

            if (builder.Length == 0)
            {
                builder.Append("Value");
            }

            string result = builder.ToString();
            return SyntaxFacts.GetKeywordKind(result) == SyntaxKind.None ? result : "@" + result;
        }
    }
}
