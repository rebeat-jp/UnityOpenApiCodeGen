using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Swagger 2.0 ドキュメントモデル。
    /// Swagger 2.0 document model.
    /// </summary>
    internal sealed class SwaggerDocument
    {
        public string? Swagger { get; }
        public SwaggerInfo Info { get; }
        public string? Host { get; }
        public string? BasePath { get; }
        public IReadOnlyList<string> Schemes { get; }
        public IReadOnlyList<string> Consumes { get; }
        public IReadOnlyList<string> Produces { get; }
        public IReadOnlyDictionary<string, SwaggerPathItem> Paths { get; }
        public IReadOnlyDictionary<string, SwaggerSchema> Definitions { get; }

        public SwaggerDocument(
            string? swagger,
            SwaggerInfo info,
            string? host,
            string? basePath,
            IReadOnlyList<string> schemes,
            IReadOnlyList<string> consumes,
            IReadOnlyList<string> produces,
            IReadOnlyDictionary<string, SwaggerPathItem> paths,
            IReadOnlyDictionary<string, SwaggerSchema> definitions)
        {
            Swagger = swagger;
            Info = info;
            Host = host;
            BasePath = basePath;
            Schemes = schemes;
            Consumes = consumes;
            Produces = produces;
            Paths = paths;
            Definitions = definitions;
        }
    }

    /// <summary>
    /// Swagger 2.0 の info メタデータ。
    /// Swagger 2.0 info metadata.
    /// </summary>
    internal sealed class SwaggerInfo
    {
        public string? Title { get; }
        public string? Version { get; }
        public string? Description { get; }

        public SwaggerInfo(string? title, string? version, string? description)
        {
            Title = title;
            Version = version;
            Description = description;
        }
    }

    /// <summary>
    /// HTTP オペレーションを持つパス項目。
    /// Path item with HTTP operations.
    /// </summary>
    internal sealed class SwaggerPathItem
    {
        public IReadOnlyDictionary<string, SwaggerOperation> Operations { get; }

        public SwaggerPathItem(IReadOnlyDictionary<string, SwaggerOperation> operations)
        {
            Operations = operations;
        }
    }

    /// <summary>
    /// Swagger のオペレーション定義。
    /// Swagger operation definition.
    /// </summary>
    internal sealed class SwaggerOperation
    {
        public string? OperationId { get; }
        public string? Summary { get; }
        public string? Description { get; }
        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyList<string> Consumes { get; }
        public IReadOnlyList<string> Produces { get; }
        public IReadOnlyList<SwaggerParameter> Parameters { get; }
        public IReadOnlyDictionary<string, SwaggerResponse> Responses { get; }

        public SwaggerOperation(
            string? operationId,
            string? summary,
            string? description,
            IReadOnlyList<string> tags,
            IReadOnlyList<string> consumes,
            IReadOnlyList<string> produces,
            IReadOnlyList<SwaggerParameter> parameters,
            IReadOnlyDictionary<string, SwaggerResponse> responses)
        {
            OperationId = operationId;
            Summary = summary;
            Description = description;
            Tags = tags;
            Consumes = consumes;
            Produces = produces;
            Parameters = parameters;
            Responses = responses;
        }
    }

    /// <summary>
    /// Swagger のパラメータ定義。
    /// Swagger parameter definition.
    /// </summary>
    internal sealed class SwaggerParameter
    {
        public string? Name { get; }
        public string? In { get; }
        public string? Description { get; }
        public bool Required { get; }
        public string? Type { get; }
        public string? Format { get; }
        public SwaggerSchema? Schema { get; }
        public SwaggerSchema? Items { get; }
        public IReadOnlyList<OpenApiAny> EnumValues { get; }
        public OpenApiAny? Default { get; }
        public string? CollectionFormat { get; }

        public SwaggerParameter(
            string? name,
            string? @in,
            string? description,
            bool required,
            string? type,
            string? format,
            SwaggerSchema? schema,
            SwaggerSchema? items,
            IReadOnlyList<OpenApiAny> enumValues,
            OpenApiAny? @default,
            string? collectionFormat)
        {
            Name = name;
            In = @in;
            Description = description;
            Required = required;
            Type = type;
            Format = format;
            Schema = schema;
            Items = items;
            EnumValues = enumValues;
            Default = @default;
            CollectionFormat = collectionFormat;
        }
    }

    /// <summary>
    /// Swagger のレスポンス定義。
    /// Swagger response definition.
    /// </summary>
    internal sealed class SwaggerResponse
    {
        public string? Description { get; }
        public SwaggerSchema? Schema { get; }
        public IReadOnlyDictionary<string, SwaggerHeader> Headers { get; }

        public SwaggerResponse(string? description, SwaggerSchema? schema, IReadOnlyDictionary<string, SwaggerHeader> headers)
        {
            Description = description;
            Schema = schema;
            Headers = headers;
        }
    }

    /// <summary>
    /// Swagger のヘッダー定義。
    /// Swagger header definition.
    /// </summary>
    internal sealed class SwaggerHeader
    {
        public string? Description { get; }
        public string? Type { get; }
        public string? Format { get; }
        public SwaggerSchema? Items { get; }
        public OpenApiAny? Default { get; }

        public SwaggerHeader(string? description, string? type, string? format, SwaggerSchema? items, OpenApiAny? @default)
        {
            Description = description;
            Type = type;
            Format = format;
            Items = items;
            Default = @default;
        }
    }

    /// <summary>
    /// Swagger のスキーマ定義（JSON Schema draft-04 準拠）。
    /// Swagger schema definition (JSON Schema draft-04).
    /// </summary>
    internal sealed class SwaggerSchema
    {
        public string? Schema { get; }
        public string? Id { get; }
        public string? Ref { get; }
        public string? Title { get; }
        public string? Description { get; }
        public string? Type { get; }
        public IReadOnlyList<string> Types { get; }
        public string? Format { get; }
        public SwaggerSchema? Items { get; }
        public IReadOnlyList<SwaggerSchema> ItemSchemas { get; }
        public SwaggerSchema? AdditionalItemsSchema { get; }
        public bool? AdditionalItemsAllowed { get; }
        public IReadOnlyDictionary<string, SwaggerSchema> Properties { get; }
        public IReadOnlyList<string> Required { get; }
        public IReadOnlyDictionary<string, SwaggerSchema> PatternProperties { get; }
        public IReadOnlyList<OpenApiAny> EnumValues { get; }
        public IReadOnlyList<SwaggerSchema> AllOf { get; }
        public IReadOnlyList<SwaggerSchema> AnyOf { get; }
        public IReadOnlyList<SwaggerSchema> OneOf { get; }
        public SwaggerSchema? Not { get; }
        public IReadOnlyDictionary<string, SwaggerSchema> Definitions { get; }
        public SwaggerSchema? AdditionalPropertiesSchema { get; }
        public bool? AdditionalPropertiesAllowed { get; }
        public IReadOnlyDictionary<string, SwaggerSchema> DependenciesSchemas { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> DependenciesRequired { get; }
        public double? MultipleOf { get; }
        public double? Maximum { get; }
        public double? Minimum { get; }
        public bool? ExclusiveMaximum { get; }
        public bool? ExclusiveMinimum { get; }
        public long? MaxLength { get; }
        public long? MinLength { get; }
        public string? Pattern { get; }
        public long? MaxItems { get; }
        public long? MinItems { get; }
        public bool? UniqueItems { get; }
        public long? MaxProperties { get; }
        public long? MinProperties { get; }
        public bool? ReadOnly { get; }
        public OpenApiAny? Default { get; }
        public OpenApiAny? Example { get; }

        public SwaggerSchema(
            string? schema,
            string? id,
            string? @ref,
            string? title,
            string? description,
            string? type,
            IReadOnlyList<string> types,
            string? format,
            SwaggerSchema? items,
            IReadOnlyList<SwaggerSchema> itemSchemas,
            SwaggerSchema? additionalItemsSchema,
            bool? additionalItemsAllowed,
            IReadOnlyDictionary<string, SwaggerSchema> properties,
            IReadOnlyList<string> required,
            IReadOnlyDictionary<string, SwaggerSchema> patternProperties,
            IReadOnlyList<OpenApiAny> enumValues,
            IReadOnlyList<SwaggerSchema> allOf,
            IReadOnlyList<SwaggerSchema> anyOf,
            IReadOnlyList<SwaggerSchema> oneOf,
            SwaggerSchema? not,
            IReadOnlyDictionary<string, SwaggerSchema> definitions,
            SwaggerSchema? additionalPropertiesSchema,
            bool? additionalPropertiesAllowed,
            IReadOnlyDictionary<string, SwaggerSchema> dependenciesSchemas,
            IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesRequired,
            double? multipleOf,
            double? maximum,
            double? minimum,
            bool? exclusiveMaximum,
            bool? exclusiveMinimum,
            long? maxLength,
            long? minLength,
            string? pattern,
            long? maxItems,
            long? minItems,
            bool? uniqueItems,
            long? maxProperties,
            long? minProperties,
            bool? readOnly,
            OpenApiAny? @default,
            OpenApiAny? example)
        {
            Schema = schema;
            Id = id;
            Ref = @ref;
            Title = title;
            Description = description;
            Type = type;
            Types = types;
            Format = format;
            Items = items;
            ItemSchemas = itemSchemas;
            AdditionalItemsSchema = additionalItemsSchema;
            AdditionalItemsAllowed = additionalItemsAllowed;
            Properties = properties;
            Required = required;
            PatternProperties = patternProperties;
            EnumValues = enumValues;
            AllOf = allOf;
            AnyOf = anyOf;
            OneOf = oneOf;
            Not = not;
            Definitions = definitions;
            AdditionalPropertiesSchema = additionalPropertiesSchema;
            AdditionalPropertiesAllowed = additionalPropertiesAllowed;
            DependenciesSchemas = dependenciesSchemas;
            DependenciesRequired = dependenciesRequired;
            MultipleOf = multipleOf;
            Maximum = maximum;
            Minimum = minimum;
            ExclusiveMaximum = exclusiveMaximum;
            ExclusiveMinimum = exclusiveMinimum;
            MaxLength = maxLength;
            MinLength = minLength;
            Pattern = pattern;
            MaxItems = maxItems;
            MinItems = minItems;
            UniqueItems = uniqueItems;
            MaxProperties = maxProperties;
            MinProperties = minProperties;
            ReadOnly = readOnly;
            Default = @default;
            Example = example;
        }
    }
}
