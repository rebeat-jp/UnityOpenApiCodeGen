using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// OpenAPI ドキュメントモデル。
    /// OpenAPI document model.
    /// </summary>
    internal sealed class OpenApiDocument
    {
        public string? OpenApi { get; }
        public OpenApiInfo Info { get; }
        public IReadOnlyDictionary<string, OpenApiPathItem> Paths { get; }
        public OpenApiComponents Components { get; }

        public OpenApiDocument(
            string? openApi,
            OpenApiInfo info,
            IReadOnlyDictionary<string, OpenApiPathItem> paths,
            OpenApiComponents components)
        {
            OpenApi = openApi;
            Info = info;
            Paths = paths;
            Components = components;
        }
    }

    /// <summary>
    /// OpenAPI の info メタデータ。
    /// OpenAPI info metadata.
    /// </summary>
    internal sealed class OpenApiInfo
    {
        public string? Title { get; }
        public string? Version { get; }

        public OpenApiInfo(string? title, string? version)
        {
            Title = title;
            Version = version;
        }
    }

    /// <summary>
    /// components セクションのコンテナ。
    /// Container for components.
    /// </summary>
    internal sealed class OpenApiComponents
    {
        public IReadOnlyDictionary<string, OpenApiSchema> Schemas { get; }

        public OpenApiComponents(IReadOnlyDictionary<string, OpenApiSchema> schemas)
        {
            Schemas = schemas;
        }
    }

    /// <summary>
    /// パス項目とオペレーション一覧。
    /// Path item with operations.
    /// </summary>
    internal sealed class OpenApiPathItem
    {
        public IReadOnlyDictionary<string, OpenApiOperation> Operations { get; }

        public OpenApiPathItem(IReadOnlyDictionary<string, OpenApiOperation> operations)
        {
            Operations = operations;
        }
    }

    /// <summary>
    /// OpenAPI のオペレーション定義。
    /// OpenAPI operation definition.
    /// </summary>
    internal sealed class OpenApiOperation
    {
        public string? OperationId { get; }
        public string? Summary { get; }
        public IReadOnlyList<OpenApiParameter> Parameters { get; }
        public OpenApiRequestBody? RequestBody { get; }
        public IReadOnlyDictionary<string, OpenApiResponse> Responses { get; }

        public OpenApiOperation(
            string? operationId,
            string? summary,
            IReadOnlyList<OpenApiParameter> parameters,
            OpenApiRequestBody? requestBody,
            IReadOnlyDictionary<string, OpenApiResponse> responses)
        {
            OperationId = operationId;
            Summary = summary;
            Parameters = parameters;
            RequestBody = requestBody;
            Responses = responses;
        }
    }

    /// <summary>
    /// OpenAPI のパラメータ定義。
    /// OpenAPI parameter definition.
    /// </summary>
    internal sealed class OpenApiParameter
    {
        public string? Name { get; }
        public string? In { get; }
        public bool Required { get; }
        public OpenApiSchema Schema { get; }

        public OpenApiParameter(string? name, string? @in, bool required, OpenApiSchema schema)
        {
            Name = name;
            In = @in;
            Required = required;
            Schema = schema;
        }
    }

    /// <summary>
    /// リクエストボディ定義。
    /// Request body definition.
    /// </summary>
    internal sealed class OpenApiRequestBody
    {
        public bool Required { get; }
        public IReadOnlyDictionary<string, OpenApiMediaType> Content { get; }

        public OpenApiRequestBody(bool required, IReadOnlyDictionary<string, OpenApiMediaType> content)
        {
            Required = required;
            Content = content;
        }
    }

    /// <summary>
    /// レスポンス定義。
    /// Response definition.
    /// </summary>
    internal sealed class OpenApiResponse
    {
        public string? Description { get; }
        public IReadOnlyDictionary<string, OpenApiMediaType> Content { get; }

        public OpenApiResponse(string? description, IReadOnlyDictionary<string, OpenApiMediaType> content)
        {
            Description = description;
            Content = content;
        }
    }

    /// <summary>
    /// メディアタイプ定義。
    /// Media type definition.
    /// </summary>
    internal sealed class OpenApiMediaType
    {
        public OpenApiSchema Schema { get; }

        public OpenApiMediaType(OpenApiSchema schema)
        {
            Schema = schema;
        }
    }

    /// <summary>
    /// JSON 値をそのまま保持する。
    /// Holds a raw JSON value.
    /// </summary>
    internal sealed class OpenApiAny
    {
        public string RawJson { get; }

        public OpenApiAny(string rawJson)
        {
            RawJson = rawJson;
        }
    }

    /// <summary>
    /// JSON Schema (OpenAPI 3.1) 定義。
    /// JSON Schema (OpenAPI 3.1) definition.
    /// </summary>
    internal sealed class OpenApiSchema
    {
        public bool? BooleanSchema { get; }
        public string? Ref { get; }
        public string? Schema { get; }
        public string? Id { get; }
        public string? Anchor { get; }
        public string? DynamicRef { get; }
        public string? DynamicAnchor { get; }
        public string? Title { get; }
        public string? Description { get; }
        public string? Type { get; }
        public IReadOnlyList<string> Types { get; }
        public string? Format { get; }
        public OpenApiAny? Const { get; }
        public IReadOnlyList<OpenApiAny> EnumValues { get; }
        public OpenApiAny? Default { get; }
        public IReadOnlyList<OpenApiAny> Examples { get; }
        public double? MultipleOf { get; }
        public double? Maximum { get; }
        public double? ExclusiveMaximum { get; }
        public double? Minimum { get; }
        public double? ExclusiveMinimum { get; }
        public long? MaxLength { get; }
        public long? MinLength { get; }
        public string? Pattern { get; }
        public long? MaxItems { get; }
        public long? MinItems { get; }
        public bool? UniqueItems { get; }
        public long? MaxContains { get; }
        public long? MinContains { get; }
        public long? MaxProperties { get; }
        public long? MinProperties { get; }
        public IReadOnlyDictionary<string, OpenApiSchema> Properties { get; }
        public IReadOnlyList<string> Required { get; }
        public IReadOnlyDictionary<string, OpenApiSchema> PatternProperties { get; }
        public OpenApiSchema? AdditionalPropertiesSchema { get; }
        public bool? AdditionalPropertiesAllowed { get; }
        public OpenApiSchema? Items { get; }
        public bool? ItemsAllowed { get; }
        public IReadOnlyList<OpenApiSchema> PrefixItems { get; }
        public OpenApiSchema? Contains { get; }
        public OpenApiSchema? PropertyNames { get; }
        public OpenApiSchema? UnevaluatedItems { get; }
        public bool? UnevaluatedItemsAllowed { get; }
        public OpenApiSchema? UnevaluatedProperties { get; }
        public bool? UnevaluatedPropertiesAllowed { get; }
        public IReadOnlyDictionary<string, OpenApiSchema> DependentSchemas { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> DependentRequired { get; }
        public OpenApiSchema? If { get; }
        public OpenApiSchema? Then { get; }
        public OpenApiSchema? Else { get; }
        public IReadOnlyList<OpenApiSchema> AllOf { get; }
        public IReadOnlyList<OpenApiSchema> AnyOf { get; }
        public IReadOnlyList<OpenApiSchema> OneOf { get; }
        public OpenApiSchema? Not { get; }
        public IReadOnlyDictionary<string, OpenApiSchema> Defs { get; }
        public IReadOnlyDictionary<string, bool> Vocabulary { get; }
        public string? Comment { get; }
        public bool? ReadOnly { get; }
        public bool? WriteOnly { get; }
        public bool? Deprecated { get; }
        public string? ContentEncoding { get; }
        public string? ContentMediaType { get; }
        public OpenApiSchema? ContentSchema { get; }

        public OpenApiSchema(
            bool? booleanSchema = null,
            string? @ref = null,
            string? schema = null,
            string? id = null,
            string? anchor = null,
            string? dynamicRef = null,
            string? dynamicAnchor = null,
            string? title = null,
            string? description = null,
            string? type = null,
            IReadOnlyList<string>? types = null,
            string? format = null,
            OpenApiAny? @const = null,
            IReadOnlyList<OpenApiAny>? enumValues = null,
            OpenApiAny? @default = null,
            IReadOnlyList<OpenApiAny>? examples = null,
            double? multipleOf = null,
            double? maximum = null,
            double? exclusiveMaximum = null,
            double? minimum = null,
            double? exclusiveMinimum = null,
            long? maxLength = null,
            long? minLength = null,
            string? pattern = null,
            long? maxItems = null,
            long? minItems = null,
            bool? uniqueItems = null,
            long? maxContains = null,
            long? minContains = null,
            long? maxProperties = null,
            long? minProperties = null,
            IReadOnlyDictionary<string, OpenApiSchema>? properties = null,
            IReadOnlyList<string>? required = null,
            IReadOnlyDictionary<string, OpenApiSchema>? patternProperties = null,
            OpenApiSchema? additionalPropertiesSchema = null,
            bool? additionalPropertiesAllowed = null,
            OpenApiSchema? items = null,
            bool? itemsAllowed = null,
            IReadOnlyList<OpenApiSchema>? prefixItems = null,
            OpenApiSchema? contains = null,
            OpenApiSchema? propertyNames = null,
            OpenApiSchema? unevaluatedItems = null,
            bool? unevaluatedItemsAllowed = null,
            OpenApiSchema? unevaluatedProperties = null,
            bool? unevaluatedPropertiesAllowed = null,
            IReadOnlyDictionary<string, OpenApiSchema>? dependentSchemas = null,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? dependentRequired = null,
            OpenApiSchema? @if = null,
            OpenApiSchema? then = null,
            OpenApiSchema? @else = null,
            IReadOnlyList<OpenApiSchema>? allOf = null,
            IReadOnlyList<OpenApiSchema>? anyOf = null,
            IReadOnlyList<OpenApiSchema>? oneOf = null,
            OpenApiSchema? not = null,
            IReadOnlyDictionary<string, OpenApiSchema>? defs = null,
            IReadOnlyDictionary<string, bool>? vocabulary = null,
            string? comment = null,
            bool? readOnly = null,
            bool? writeOnly = null,
            bool? deprecated = null,
            string? contentEncoding = null,
            string? contentMediaType = null,
            OpenApiSchema? contentSchema = null)
        {
            BooleanSchema = booleanSchema;
            Ref = @ref;
            Schema = schema;
            Id = id;
            Anchor = anchor;
            DynamicRef = dynamicRef;
            DynamicAnchor = dynamicAnchor;
            Title = title;
            Description = description;
            Type = type;
            Types = types ?? new List<string>();
            Format = format;
            Const = @const;
            EnumValues = enumValues ?? new List<OpenApiAny>();
            Default = @default;
            Examples = examples ?? new List<OpenApiAny>();
            MultipleOf = multipleOf;
            Maximum = maximum;
            ExclusiveMaximum = exclusiveMaximum;
            Minimum = minimum;
            ExclusiveMinimum = exclusiveMinimum;
            MaxLength = maxLength;
            MinLength = minLength;
            Pattern = pattern;
            MaxItems = maxItems;
            MinItems = minItems;
            UniqueItems = uniqueItems;
            MaxContains = maxContains;
            MinContains = minContains;
            MaxProperties = maxProperties;
            MinProperties = minProperties;
            Properties = properties ?? new Dictionary<string, OpenApiSchema>();
            Required = required ?? new List<string>();
            PatternProperties = patternProperties ?? new Dictionary<string, OpenApiSchema>();
            AdditionalPropertiesSchema = additionalPropertiesSchema;
            AdditionalPropertiesAllowed = additionalPropertiesAllowed;
            Items = items;
            ItemsAllowed = itemsAllowed;
            PrefixItems = prefixItems ?? new List<OpenApiSchema>();
            Contains = contains;
            PropertyNames = propertyNames;
            UnevaluatedItems = unevaluatedItems;
            UnevaluatedItemsAllowed = unevaluatedItemsAllowed;
            UnevaluatedProperties = unevaluatedProperties;
            UnevaluatedPropertiesAllowed = unevaluatedPropertiesAllowed;
            DependentSchemas = dependentSchemas ?? new Dictionary<string, OpenApiSchema>();
            DependentRequired = dependentRequired ?? new Dictionary<string, IReadOnlyList<string>>();
            If = @if;
            Then = then;
            Else = @else;
            AllOf = allOf ?? new List<OpenApiSchema>();
            AnyOf = anyOf ?? new List<OpenApiSchema>();
            OneOf = oneOf ?? new List<OpenApiSchema>();
            Not = not;
            Defs = defs ?? new Dictionary<string, OpenApiSchema>();
            Vocabulary = vocabulary ?? new Dictionary<string, bool>();
            Comment = comment;
            ReadOnly = readOnly;
            WriteOnly = writeOnly;
            Deprecated = deprecated;
            ContentEncoding = contentEncoding;
            ContentMediaType = contentMediaType;
            ContentSchema = contentSchema;
        }
    }
}
