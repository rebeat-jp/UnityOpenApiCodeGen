using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis.CSharp;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class OpenApiGenerationModelBuilder
    {
        private readonly OpenApiSemanticDocument _document;
        private readonly GeneratorOptions _options;
        private readonly Dictionary<string, string> _componentTypeNames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _schemaTypeNames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _usedTypeNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<GeneratedDtoModel> _dtos = new List<GeneratedDtoModel>();
        private readonly List<GeneratedEnumModel> _enums = new List<GeneratedEnumModel>();
        private readonly HashSet<string> _buildingSchemas = new HashSet<string>(StringComparer.Ordinal);

        private OpenApiGenerationModelBuilder(OpenApiSemanticDocument document, GeneratorOptions options)
        {
            _document = document;
            _options = options;
            _usedTypeNames.Add(options.ApiName);
            _usedTypeNames.Add(options.ApiName + "Exception");
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
            foreach (KeyValuePair<string, OpenApiSemanticSchema> pair in _document.Schemas
                         .OrderBy(static value => value.Key, StringComparer.Ordinal))
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
                operations);
        }

        private void AllocateComponentTypeNames()
        {
            foreach (string componentName in _document.Schemas.Keys.OrderBy(
                         static value => value,
                         StringComparer.Ordinal))
            {
                _componentTypeNames.Add(
                    componentName,
                    AllocateUniqueTypeName(ToIdentifier(componentName, pascalCase: true)));
            }
        }

        private IReadOnlyList<GeneratedOperationModel> BuildOperations()
        {
            var usedMethodNames = new HashSet<string>(StringComparer.Ordinal)
            {
                _options.ApiName
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
                    "requestUri",
                    "request",
                    "response",
                    "responseBody",
                    "requestJson",
                    "_httpClient",
                    "CreateRequestUri",
                    "ConvertToString"
                };
                var parameters = new List<GeneratedParameterModel>();
                foreach (OpenApiSemanticParameter parameter in operation.Parameters
                             .OrderBy(static value => value.Identity, StringComparer.Ordinal))
                {
                    string parameterName = AllocateUniqueName(
                        ToIdentifier(parameter.Name, pascalCase: false),
                        usedParameterNames);
                    GeneratedTypeModel type = ResolveType(parameter.Schema)
                        .WithNullable(parameter.Schema.Nullable || !parameter.Required);
                    parameters.Add(new GeneratedParameterModel(
                        parameterName,
                        parameter.Name,
                        parameter.LocationName,
                        parameter.Required,
                        type));
                }

                GeneratedRequestBodyModel? requestBody = null;
                if (operation.RequestBody is not null)
                {
                    string parameterName = AllocateUniqueName("body", usedParameterNames);
                    GeneratedTypeModel type = ResolveType(operation.RequestBody.Schema)
                        .WithNullable(operation.RequestBody.Schema.Nullable || !operation.RequestBody.Required);
                    requestBody = new GeneratedRequestBodyModel(
                        parameterName,
                        operation.RequestBody.Required,
                        operation.RequestBody.MediaType,
                        type);
                }

                GeneratedTypeModel? responseType = operation.ResponseSchema is null
                    ? null
                    : ResolveType(operation.ResponseSchema)
                        .WithNullable(operation.ResponseSchema.Nullable);
                result.Add(new GeneratedOperationModel(
                    methodName,
                    operation.Summary,
                    operation.HttpMethod,
                    operation.Path,
                    parameters,
                    requestBody,
                    responseType,
                    operation.SuccessStatusCodes));
            }

            return result;
        }

        private GeneratedTypeModel ResolveType(OpenApiSemanticSchema schema)
        {
            switch (schema.Kind)
            {
                case OpenApiSemanticSchemaKind.String:
                    return ResolveStringType(schema);
                case OpenApiSemanticSchemaKind.Integer:
                    return new GeneratedTypeModel(
                        string.Equals(schema.Format, "int64", StringComparison.OrdinalIgnoreCase)
                            ? GeneratedTypeKind.Int64
                            : GeneratedTypeKind.Int32,
                        schema.Nullable);
                case OpenApiSemanticSchemaKind.Number:
                    return new GeneratedTypeModel(ResolveNumberKind(schema.Format), schema.Nullable);
                case OpenApiSemanticSchemaKind.Boolean:
                    return new GeneratedTypeModel(GeneratedTypeKind.Boolean, schema.Nullable);
                case OpenApiSemanticSchemaKind.Array:
                    return new GeneratedTypeModel(
                        GeneratedTypeKind.Array,
                        schema.Nullable,
                        itemType: ResolveType(schema.ItemSchema!));
                case OpenApiSemanticSchemaKind.Reference:
                    if (!_document.Schemas.TryGetValue(
                            schema.ReferenceName,
                            out OpenApiSemanticSchema? referencedSchema))
                    {
                        throw new InvalidOperationException(
                            "Semantic model is missing referenced schema '" + schema.ReferenceName + "'.");
                    }

                    if (referencedSchema.Kind == OpenApiSemanticSchemaKind.Object ||
                        referencedSchema.Kind == OpenApiSemanticSchemaKind.Enum)
                    {
                        return new GeneratedTypeModel(
                            referencedSchema.Kind == OpenApiSemanticSchemaKind.Enum
                                ? GeneratedTypeKind.NamedEnum
                                : GeneratedTypeKind.Named,
                            schema.Nullable,
                            _componentTypeNames[schema.ReferenceName]);
                    }

                    return ResolveType(referencedSchema).WithNullable(
                        schema.Nullable || referencedSchema.Nullable);
                case OpenApiSemanticSchemaKind.Object:
                case OpenApiSemanticSchemaKind.Enum:
                    string typeName = GetOrAllocateSchemaTypeName(schema);
                    EnsureDeclaration(schema, typeName);
                    return new GeneratedTypeModel(
                        schema.Kind == OpenApiSemanticSchemaKind.Enum
                            ? GeneratedTypeKind.NamedEnum
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
            if (string.Equals(schema.Format, "date-time", StringComparison.OrdinalIgnoreCase))
            {
                kind = GeneratedTypeKind.DateTimeOffset;
            }
            else if (string.Equals(schema.Format, "date", StringComparison.OrdinalIgnoreCase))
            {
                kind = GeneratedTypeKind.DateTime;
            }
            else if (string.Equals(schema.Format, "uuid", StringComparison.OrdinalIgnoreCase))
            {
                kind = GeneratedTypeKind.Guid;
            }
            else
            {
                kind = GeneratedTypeKind.String;
            }

            return new GeneratedTypeModel(kind, schema.Nullable);
        }

        private static GeneratedTypeKind ResolveNumberKind(string format)
        {
            if (string.Equals(format, "float", StringComparison.OrdinalIgnoreCase))
            {
                return GeneratedTypeKind.Single;
            }

            if (string.Equals(format, "decimal", StringComparison.OrdinalIgnoreCase))
            {
                return GeneratedTypeKind.Decimal;
            }

            return GeneratedTypeKind.Double;
        }

        private void EnsureDeclaration(OpenApiSemanticSchema schema, string typeName)
        {
            string key = GetSchemaKey(schema);
            if (!_buildingSchemas.Add(key))
            {
                return;
            }

            try
            {
                if (_dtos.Any(value => value.Name == typeName) ||
                    _enums.Any(value => value.Name == typeName))
                {
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
                var properties = new List<GeneratedDtoPropertyModel>();
                foreach (OpenApiSemanticProperty property in schema.Properties
                             .OrderBy(static value => value.WireName, StringComparer.Ordinal))
                {
                    string propertyName = AllocateUniqueName(
                        ToIdentifier(property.WireName, pascalCase: true),
                        usedPropertyNames);
                    GeneratedTypeModel type = ResolveType(property.Schema)
                        .WithNullable(property.Schema.Nullable || !property.Required);
                    properties.Add(new GeneratedDtoPropertyModel(
                        propertyName,
                        property.WireName,
                        property.Required,
                        type));
                }

                _dtos.Add(new GeneratedDtoModel(typeName, properties));
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
            var members = new List<GeneratedEnumMemberModel>();
            foreach (string wireValue in schema.EnumValues.OrderBy(
                         static value => value,
                         StringComparer.Ordinal))
            {
                string name = AllocateUniqueName(ToIdentifier(wireValue, pascalCase: true), usedNames);
                members.Add(new GeneratedEnumMemberModel(name, wireValue));
            }

            return new GeneratedEnumModel(typeName, members);
        }

        private string GetOrAllocateSchemaTypeName(OpenApiSemanticSchema schema)
        {
            string key = GetSchemaKey(schema);
            if (_schemaTypeNames.TryGetValue(key, out string? existing))
            {
                return existing;
            }

            string name = AllocateUniqueTypeName(ToIdentifier(schema.SuggestedName, pascalCase: true));
            _schemaTypeNames.Add(key, name);
            return name;
        }

        private static string GetSchemaKey(OpenApiSemanticSchema schema)
        {
            return string.IsNullOrEmpty(schema.Location.LogicalPath)
                ? schema.SuggestedName
                : schema.Location.LogicalPath;
        }

        private string AllocateUniqueTypeName(string name)
        {
            return AllocateUniqueName(name, _usedTypeNames);
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
