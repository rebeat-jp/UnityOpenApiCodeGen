using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Swagger 2.0 から CSharpFile を生成する。
    /// Generates CSharpFile from Swagger 2.0 document.
    /// </summary>
    internal sealed class SwaggerCSharpClientFileGenerator : ICSharpFileGenerator<SwaggerDocument>
    {
        /// <summary>
        /// Swagger ドキュメントから CSharpFile を生成する。
        /// Generates CSharpFile from Swagger document.
        /// </summary>
        public CSharpFile Generate(SwaggerDocument document, ApiClientGenerateOption option)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (option is null)
            {
                throw new ArgumentNullException(nameof(option));
            }

            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var methodMembers = new List<string>();
            var usesCollections = false;
            var usesTasks = false;
            var usesText = false;
            var usesJson = false;

            foreach (var path in document.Paths)
            {
                foreach (var operation in path.Value.Operations)
                {
                    var methodName = operation.Value.OperationId ?? $"{operation.Key}_{path.Key}";
                    methodName = SanitizeMethodName(methodName);
                    methodName = EnsureUniqueName(methodName, usedNames);

                    var method = CreateMethod(
                        methodName,
                        path.Key,
                        operation.Key,
                        operation.Value,
                        document.Consumes,
                        option,
                        ref usesCollections,
                        ref usesTasks,
                        ref usesText,
                        ref usesJson);

                    // Roslyn syntax を文字列化して CSharpFile 側のメンバーとして保持する。
                    methodMembers.Add(method.NormalizeWhitespace().ToFullString());
                }
            }

            var fileBuilder = new CSharpSyntaxGenerator(
                $"{option.ApiName}.g.cs",
                string.IsNullOrWhiteSpace(option.Namespace)
                    ? "Rhycol.OpenApiCodeGen.Generated"
                    : option.Namespace);

            fileBuilder.AddUsing("System");
            if (option.HttpLibraryType == HttpLibrary.HttpClient)
            {
                fileBuilder.AddUsing("System.Net.Http");
            }

            if (usesCollections)
            {
                fileBuilder.AddUsing("System.Collections.Generic");
            }

            if (usesTasks)
            {
                fileBuilder.AddUsing("System.Threading.Tasks");
            }

            if (usesText)
            {
                fileBuilder.AddUsing("System.Text");
            }

            if (usesJson)
            {
                fileBuilder.AddUsing(option.JsonLibraryType == JsonLibrary.NewtonsoftJson
                    ? "Newtonsoft.Json"
                    : "System.Text.Json");
            }

            fileBuilder.AddClass(option.ApiName, builder =>
            {
                builder.AddModifiers("public", "partial");

                if (option.HttpLibraryType == HttpLibrary.HttpClient)
                {
                    var members = HttpClientApiSyntaxGenerator.CreateClientMembers(
                        option.ApiName,
                        CreateDocumentationTrivia);
                    foreach (var member in members)
                    {
                        // HttpClient 用のフィールド/コンストラクタをメンバーとして追加する。
                        builder.AddMember(member.NormalizeWhitespace().ToFullString());
                    }
                }

                foreach (var member in methodMembers)
                {
                    builder.AddMember(member);
                }
            });

            return fileBuilder.Build();
        }

        /// <summary>
        /// Swagger operation からメソッド構文を生成する。
        /// Builds a method syntax from a Swagger operation.
        /// </summary>
        private static MethodDeclarationSyntax CreateMethod(
            string methodName,
            string path,
            string httpMethod,
            SwaggerOperation operation,
            IReadOnlyList<string> globalConsumes,
            ApiClientGenerateOption option,
            ref bool usesCollections,
            ref bool usesTasks,
            ref bool usesText,
            ref bool usesJson)
        {
            var parameters = new List<ParameterSyntax>();
            var paramDocs = new List<(string Name, string? Summary)>();
            var usedParamNames = new HashSet<string>(StringComparer.Ordinal);
            var httpParameters = new List<(string Name, string? Location, bool Required, bool IsValueType, bool IsNullable)>();

            string? bodyParamName = null;
            SwaggerSchema? bodySchema = null;
            var consumes = operation.Consumes.Count > 0 ? operation.Consumes : globalConsumes;

            foreach (var parameter in operation.Parameters)
            {
                var rawName = parameter.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    continue;
                }

                if (string.Equals(parameter.In, "body", StringComparison.OrdinalIgnoreCase))
                {
                    bodySchema = parameter.Schema ?? ConvertParameterSchema(parameter);
                    if (bodySchema is null)
                    {
                        continue;
                    }

                    bodyParamName = EnsureUniqueName("body", usedParamNames);
                    var typeInfo = ResolveTypeInfo(bodySchema, ref usesCollections);
                    var paramType = ApplyNullability(typeInfo.Type, parameter.Required, typeInfo.IsValueType, option);
                    parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(bodyParamName)).WithType(paramType));
                    paramDocs.Add((bodyParamName, parameter.Description));
                    continue;
                }

                var paramName = SanitizeParameterName(rawName);
                paramName = EnsureUniqueName(paramName, usedParamNames);

                var schema = parameter.Schema ?? ConvertParameterSchema(parameter) ?? CreateSchema(type: "object");
                var type = ResolveTypeInfo(schema, ref usesCollections);
                var paramTypeSyntax = ApplyNullability(type.Type, parameter.Required, type.IsValueType, option);
                parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName)).WithType(paramTypeSyntax));
                paramDocs.Add((paramName, parameter.Description));
                var isNullable = !parameter.Required && option.UseNullableReferenceTypes;
                httpParameters.Add((paramName, parameter.In, parameter.Required, type.IsValueType, isNullable));
            }

            var returnSchema = GetResponseSchema(operation.Responses);
            var returnType = returnSchema is null
                ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword))
                : ResolveTypeInfo(returnSchema, ref usesCollections).Type;

            var isHttpClient = option.HttpLibraryType == HttpLibrary.HttpClient;
            var methodReturnType = returnType;
            var modifiers = new List<SyntaxToken> { SyntaxFactory.Token(SyntaxKind.PublicKeyword) };
            var statements = new List<StatementSyntax>();

            if (isHttpClient)
            {
                usesTasks = true;
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));

                if (returnSchema is null)
                {
                    methodReturnType = SyntaxFactory.ParseTypeName("Task");
                }
                else
                {
                    methodReturnType = SyntaxFactory.GenericName("Task")
                        .AddTypeArgumentListArguments(returnType);
                }

                statements.AddRange(CreateHttpClientMethodBody(
                    path,
                    httpMethod,
                    httpParameters,
                    bodyParamName,
                    bodySchema,
                    consumes,
                    returnType,
                    returnSchema is null,
                    option,
                    ref usesCollections,
                    ref usesText,
                    ref usesJson));
            }

            var method = SyntaxFactory.MethodDeclaration(methodReturnType, SyntaxFactory.Identifier(methodName))
                .AddModifiers(modifiers.ToArray())
                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)))
                .WithBody(SyntaxFactory.Block(statements));

            return method.WithLeadingTrivia(CreateDocumentationTrivia(operation, paramDocs, returnSchema is not null));
        }

        /// <summary>
        /// HttpClient 用のメソッド本体を構築する。
        /// Builds HttpClient method body.
        /// </summary>
        private static IEnumerable<StatementSyntax> CreateHttpClientMethodBody(
            string path,
            string httpMethod,
            IReadOnlyList<(string Name, string? Location, bool Required, bool IsValueType, bool IsNullable)> parameters,
            string? bodyParamName,
            SwaggerSchema? bodySchema,
            IReadOnlyList<string> consumes,
            TypeSyntax returnType,
            bool isVoidReturn,
            ApiClientGenerateOption option,
            ref bool usesCollections,
            ref bool usesText,
            ref bool usesJson)
        {
            var statements = new List<StatementSyntax>();
            var escapedPath = EscapeStringLiteral(path);
            statements.Add(SyntaxFactory.ParseStatement($"var url = \"{escapedPath}\";"));

            foreach (var parameter in parameters)
            {
                if (!string.Equals(parameter.Location, "path", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var replaceValue = CreateParameterValueExpression(parameter);
                var escapedName = EscapeStringLiteral(parameter.Name);
                if (replaceValue is null)
                {
                    continue;
                }

                if (replaceValue.Value.Condition is null)
                {
                    statements.Add(SyntaxFactory.ParseStatement(
                        $"url = url.Replace(\"{{{escapedName}}}\", System.Uri.EscapeDataString({replaceValue.Value.ValueExpression}));"));
                }
                else
                {
                    statements.Add(SyntaxFactory.ParseStatement(
                        $"if ({replaceValue.Value.Condition}) {{ url = url.Replace(\"{{{escapedName}}}\", System.Uri.EscapeDataString({replaceValue.Value.ValueExpression})); }}"));
                }
            }

            var queryParameters = parameters.Where(p =>
                string.Equals(p.Location, "query", StringComparison.OrdinalIgnoreCase)).ToList();
            if (queryParameters.Count > 0)
            {
                usesCollections = true;
                statements.Add(SyntaxFactory.ParseStatement("var queryParts = new List<string>();"));

                foreach (var parameter in queryParameters)
                {
                    var valueExpression = CreateParameterValueExpression(parameter);
                    var escapedName = EscapeStringLiteral(parameter.Name);
                    if (valueExpression is null)
                    {
                        continue;
                    }

                    var addStatement =
                        $"queryParts.Add(\"{escapedName}=\" + System.Uri.EscapeDataString({valueExpression.Value.ValueExpression}));";

                    if (valueExpression.Value.Condition is null)
                    {
                        statements.Add(SyntaxFactory.ParseStatement(addStatement));
                    }
                    else
                    {
                        statements.Add(SyntaxFactory.ParseStatement(
                            $"if ({valueExpression.Value.Condition}) {{ {addStatement} }}"));
                    }
                }

                statements.Add(SyntaxFactory.ParseStatement(
                    "if (queryParts.Count > 0) { url = url + (url.Contains(\"?\") ? \"&\" : \"?\") + string.Join(\"&\", queryParts); }"));
            }

            var methodExpression = GetHttpMethodExpression(httpMethod);
            statements.Add(SyntaxFactory.ParseStatement($"var request = new HttpRequestMessage({methodExpression}, url);"));

            foreach (var parameter in parameters)
            {
                if (!string.Equals(parameter.Location, "header", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var valueExpression = CreateParameterValueExpression(parameter);
                var escapedName = EscapeStringLiteral(parameter.Name);
                if (valueExpression is null)
                {
                    continue;
                }

                var addHeader =
                    $"request.Headers.TryAddWithoutValidation(\"{escapedName}\", {valueExpression.Value.ValueExpression});";
                if (valueExpression.Value.Condition is null)
                {
                    statements.Add(SyntaxFactory.ParseStatement(addHeader));
                }
                else
                {
                    statements.Add(SyntaxFactory.ParseStatement($"if ({valueExpression.Value.Condition}) {{ {addHeader} }}"));
                }
            }

            if (bodyParamName is not null && bodySchema is not null)
            {
                usesText = true;
                usesJson = true;
                var mediaType = SelectMediaType(consumes);

                var serializer = option.JsonLibraryType == JsonLibrary.NewtonsoftJson
                    ? "JsonConvert.SerializeObject"
                    : "JsonSerializer.Serialize";

                statements.Add(SyntaxFactory.ParseStatement($"var json = {serializer}({bodyParamName});"));
                statements.Add(SyntaxFactory.ParseStatement(
                    $"request.Content = new StringContent(json, Encoding.UTF8, \"{EscapeStringLiteral(mediaType)}\");"));
            }

            statements.Add(SyntaxFactory.ParseStatement("var response = await _httpClient.SendAsync(request);"));
            statements.Add(SyntaxFactory.ParseStatement("response.EnsureSuccessStatusCode();"));

            if (isVoidReturn)
            {
                statements.Add(SyntaxFactory.ParseStatement("return;"));
                return statements;
            }

            var returnTypeName = returnType.ToString();
            if (returnTypeName == "string" || returnTypeName == "System.String")
            {
                statements.Add(SyntaxFactory.ParseStatement("return await response.Content.ReadAsStringAsync();"));
                return statements;
            }

            if (returnTypeName == "byte[]" || returnTypeName == "System.Byte[]")
            {
                statements.Add(SyntaxFactory.ParseStatement("return await response.Content.ReadAsByteArrayAsync();"));
                return statements;
            }

            usesJson = true;
            var deserializer = option.JsonLibraryType == JsonLibrary.NewtonsoftJson
                ? $"JsonConvert.DeserializeObject<{returnTypeName}>"
                : $"JsonSerializer.Deserialize<{returnTypeName}>";

            statements.Add(SyntaxFactory.ParseStatement("var content = await response.Content.ReadAsStringAsync();"));
            statements.Add(SyntaxFactory.ParseStatement($"var deserialized = {deserializer}(content);"));
            statements.Add(SyntaxFactory.ParseStatement(
                "if (deserialized == null) { throw new InvalidOperationException(\"Deserialization returned null.\"); }"));
            statements.Add(SyntaxFactory.ParseStatement("return deserialized;"));
            return statements;
        }

        private static string SelectMediaType(IReadOnlyList<string> consumes)
        {
            if (consumes is null)
            {
                return "application/json";
            }

            foreach (var mediaType in consumes)
            {
                if (!string.IsNullOrWhiteSpace(mediaType))
                {
                    return mediaType;
                }
            }

            return "application/json";
        }

        private static (string ValueExpression, string? Condition)? CreateParameterValueExpression(
            (string Name, string? Location, bool Required, bool IsValueType, bool IsNullable) parameter)
        {
            var name = parameter.Name;
            if (parameter.Required || (parameter.IsValueType && !parameter.IsNullable))
            {
                return ($"{name}.ToString()", null);
            }

            if (parameter.IsValueType)
            {
                return ($"{name}.Value.ToString()", $"{name}.HasValue");
            }

            return ($"{name}.ToString()", $"{name} != null");
        }

        private static string GetHttpMethodExpression(string httpMethod)
        {
            switch (httpMethod.ToUpperInvariant())
            {
                case "GET":
                    return "HttpMethod.Get";
                case "POST":
                    return "HttpMethod.Post";
                case "PUT":
                    return "HttpMethod.Put";
                case "DELETE":
                    return "HttpMethod.Delete";
                case "HEAD":
                    return "HttpMethod.Head";
                case "OPTIONS":
                    return "HttpMethod.Options";
                case "PATCH":
                    return "HttpMethod.Patch";
                case "TRACE":
                    return "HttpMethod.Trace";
                default:
                    return $"new HttpMethod(\"{EscapeStringLiteral(httpMethod.ToUpperInvariant())}\")";
            }
        }

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static SyntaxTriviaList CreateDocumentationTrivia(
            SwaggerOperation operation,
            IReadOnlyList<(string Name, string? Summary)> parameters,
            bool hasReturn)
        {
            var summary = operation.Summary;
            var operationId = operation.OperationId ?? string.Empty;

            var summaryText = summary ?? operationId;
            if (string.IsNullOrWhiteSpace(summaryText))
            {
                summaryText = "Operation";
            }

            var xml = "/// <summary>" + EscapeXml(summaryText) + "</summary>\n";
            if (!string.IsNullOrWhiteSpace(summary) && !string.IsNullOrWhiteSpace(operationId))
            {
                xml += "/// <remarks>operationId: " + EscapeXml(operationId) + "</remarks>\n";
            }

            foreach (var parameter in parameters)
            {
                xml += "/// <param name=\"" + EscapeXml(parameter.Name) + "\">";
                xml += EscapeXml(parameter.Summary ?? parameter.Name);
                xml += "</param>\n";
            }

            if (hasReturn)
            {
                xml += "/// <returns>Response</returns>\n";
            }

            return SyntaxFactory.ParseLeadingTrivia(xml);
        }

        private static SyntaxTriviaList CreateDocumentationTrivia(
            string summary,
            IReadOnlyList<(string Name, string? Summary)> parameters,
            bool hasReturn)
        {
            var xml = "/// <summary>" + EscapeXml(summary) + "</summary>\n";

            foreach (var parameter in parameters)
            {
                xml += "/// <param name=\"" + EscapeXml(parameter.Name) + "\">";
                xml += EscapeXml(parameter.Summary ?? parameter.Name);
                xml += "</param>\n";
            }

            if (hasReturn)
            {
                xml += "/// <returns>Response</returns>\n";
            }

            return SyntaxFactory.ParseLeadingTrivia(xml);
        }

        private static string EscapeXml(string value)
        {
            return value.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private static SwaggerSchema? GetResponseSchema(IReadOnlyDictionary<string, SwaggerResponse> responses)
        {
            foreach (var response in responses)
            {
                if (response.Key.StartsWith("2", StringComparison.Ordinal))
                {
                    var schema = response.Value.Schema;
                    if (schema is not null)
                    {
                        return schema;
                    }
                }
            }

            if (responses.TryGetValue("default", out var defaultResponse))
            {
                if (defaultResponse.Schema is not null)
                {
                    return defaultResponse.Schema;
                }
            }

            foreach (var response in responses.Values)
            {
                if (response.Schema is not null)
                {
                    return response.Schema;
                }
            }

            return null;
        }

        private static SwaggerSchema? ConvertParameterSchema(SwaggerParameter parameter)
        {
            if (parameter.Schema is not null)
            {
                return parameter.Schema;
            }

            if (string.IsNullOrWhiteSpace(parameter.Type))
            {
                return null;
            }

            if (string.Equals(parameter.Type, "array", StringComparison.OrdinalIgnoreCase))
            {
                var itemSchema = parameter.Items;
                return CreateSchema(type: "array", items: itemSchema);
            }

            return CreateSchema(type: parameter.Type, format: parameter.Format);
        }

        private static SwaggerSchema CreateSchema(
            string? type = null,
            string? format = null,
            SwaggerSchema? items = null)
        {
            return new SwaggerSchema(
                schema: null,
                id: null,
                @ref: null,
                title: null,
                description: null,
                type: type,
                types: Array.Empty<string>(),
                format: format,
                items: items,
                itemSchemas: Array.Empty<SwaggerSchema>(),
                additionalItemsSchema: null,
                additionalItemsAllowed: null,
                properties: new Dictionary<string, SwaggerSchema>(StringComparer.Ordinal),
                required: Array.Empty<string>(),
                patternProperties: new Dictionary<string, SwaggerSchema>(StringComparer.Ordinal),
                enumValues: Array.Empty<OpenApiAny>(),
                allOf: Array.Empty<SwaggerSchema>(),
                anyOf: Array.Empty<SwaggerSchema>(),
                oneOf: Array.Empty<SwaggerSchema>(),
                not: null,
                definitions: new Dictionary<string, SwaggerSchema>(StringComparer.Ordinal),
                additionalPropertiesSchema: null,
                additionalPropertiesAllowed: null,
                dependenciesSchemas: new Dictionary<string, SwaggerSchema>(StringComparer.Ordinal),
                dependenciesRequired: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
                multipleOf: null,
                maximum: null,
                minimum: null,
                exclusiveMaximum: null,
                exclusiveMinimum: null,
                maxLength: null,
                minLength: null,
                pattern: null,
                maxItems: null,
                minItems: null,
                uniqueItems: null,
                maxProperties: null,
                minProperties: null,
                readOnly: null,
                @default: null,
                example: null);
        }

        private static (TypeSyntax Type, bool IsValueType) ResolveTypeInfo(
            SwaggerSchema schema,
            ref bool usesCollections)
        {
            if (schema.Ref is not null)
            {
                var name = schema.Ref.Split('/').LastOrDefault() ?? "RefType";
                return (SyntaxFactory.IdentifierName(name), false);
            }

            var composite = ResolveCompositeType(schema);
            if (composite is not null)
            {
                return composite.Value;
            }

            var type = schema.Type ?? schema.Types.FirstOrDefault() ?? "object";
            switch (type)
            {
                case "integer":
                    if (string.Equals(schema.Format, "int64", StringComparison.OrdinalIgnoreCase))
                    {
                        return (SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.LongKeyword)), true);
                    }

                    return (SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)), true);
                case "number":
                    if (string.Equals(schema.Format, "decimal", StringComparison.OrdinalIgnoreCase))
                    {
                        return (SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DecimalKeyword)), true);
                    }

                    if (string.Equals(schema.Format, "float", StringComparison.OrdinalIgnoreCase))
                    {
                        return (SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.FloatKeyword)), true);
                    }

                    return (SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DoubleKeyword)), true);
                case "boolean":
                    return (SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)), true);
                case "string":
                    return MapStringFormat(schema.Format);
                case "array":
                    usesCollections = true;
                    var itemSchema = schema.Items ?? schema.ItemSchemas.FirstOrDefault();
                    var itemType = itemSchema is null
                        ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword))
                        : ResolveTypeInfo(itemSchema, ref usesCollections).Type;
                    var listType = SyntaxFactory.QualifiedName(
                        SyntaxFactory.ParseName("System.Collections.Generic"),
                        SyntaxFactory.GenericName("IReadOnlyList")
                            .AddTypeArgumentListArguments(itemType));
                    return (listType, false);
                case "object":
                default:
                    return (SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)), false);
            }
        }

        private static (TypeSyntax Type, bool IsValueType)? ResolveCompositeType(SwaggerSchema schema)
        {
            if (schema.AllOf.Count == 0 && schema.AnyOf.Count == 0 && schema.OneOf.Count == 0)
            {
                return null;
            }

            var candidates = new List<TypeSyntax>();
            foreach (var part in schema.AllOf.Concat(schema.AnyOf).Concat(schema.OneOf))
            {
                if (part.Ref is not null)
                {
                    var name = part.Ref.Split('/').LastOrDefault() ?? "RefType";
                    candidates.Add(SyntaxFactory.IdentifierName(name));
                    continue;
                }

                var primitive = part.Type ?? part.Types.FirstOrDefault();
                if (primitive is not null)
                {
                    candidates.Add(MapPrimitiveType(primitive, part.Format));
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            var first = candidates[0].ToString();
            if (candidates.All(candidate => candidate.ToString() == first))
            {
                var isValueType = IsValueTypeName(first);
                return (candidates[0], isValueType);
            }

            return (SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)), false);
        }

        private static TypeSyntax MapPrimitiveType(string type, string? format)
        {
            switch (type)
            {
                case "integer":
                    if (string.Equals(format, "int64", StringComparison.OrdinalIgnoreCase))
                    {
                        return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.LongKeyword));
                    }

                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword));
                case "number":
                    if (string.Equals(format, "float", StringComparison.OrdinalIgnoreCase))
                    {
                        return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.FloatKeyword));
                    }

                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DoubleKeyword));
                case "boolean":
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword));
                case "string":
                    return MapStringFormat(format).Type;
                default:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword));
            }
        }

        private static bool IsValueTypeName(string typeName)
        {
            return typeName == "int" ||
                   typeName == "long" ||
                   typeName == "short" ||
                   typeName == "float" ||
                   typeName == "double" ||
                   typeName == "bool" ||
                   typeName == "decimal" ||
                   typeName == "DateTime" ||
                   typeName == "DateTimeOffset" ||
                   typeName == "Guid" ||
                   typeName == "TimeSpan" ||
                   typeName == "System.DateTime" ||
                   typeName == "System.DateTimeOffset" ||
                   typeName == "System.Guid" ||
                   typeName == "System.TimeSpan";
        }

        private static TypeSyntax ApplyNullability(TypeSyntax type, bool required, bool isValueType, ApiClientGenerateOption option)
        {
            if (required || !option.UseNullableReferenceTypes)
            {
                return type;
            }

            return SyntaxFactory.NullableType(type);
        }

        private static string EnsureUniqueName(string methodName, HashSet<string> usedNames)
        {
            if (usedNames.Add(methodName))
            {
                return methodName;
            }

            var suffix = 1;
            var candidate = $"{methodName}_{suffix}";
            while (!usedNames.Add(candidate))
            {
                suffix++;
                candidate = $"{methodName}_{suffix}";
            }

            return candidate;
        }

        private static string SanitizeMethodName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Call";
            }

            var builder = new StringBuilder(name.Length);
            var first = true;
            foreach (var ch in name)
            {
                if ((ch >= 'a' && ch <= 'z') ||
                    (ch >= 'A' && ch <= 'Z') ||
                    ch == '_')
                {
                    builder.Append(ch);
                }
                else if (!first && (ch >= '0' && ch <= '9'))
                {
                    builder.Append(ch);
                }
                else
                {
                    builder.Append('_');
                }

                first = false;
            }

            if (builder.Length == 0)
            {
                return "Call";
            }

            if (builder[0] >= '0' && builder[0] <= '9')
            {
                builder.Insert(0, "Op_");
            }

            return builder.ToString();
        }

        private static string SanitizeParameterName(string name)
        {
            var sanitized = SanitizeMethodName(name);
            if (SyntaxFacts.GetKeywordKind(sanitized) != SyntaxKind.None)
            {
                return "@" + sanitized;
            }

            return sanitized;
        }

        private static (TypeSyntax Type, bool IsValueType) MapStringFormat(string? format)
        {
            if (string.Equals(format, "date-time", StringComparison.OrdinalIgnoreCase))
            {
                return (SyntaxFactory.ParseTypeName("System.DateTimeOffset"), true);
            }

            if (string.Equals(format, "date", StringComparison.OrdinalIgnoreCase))
            {
                return (SyntaxFactory.ParseTypeName("System.DateTime"), true);
            }

            if (string.Equals(format, "uuid", StringComparison.OrdinalIgnoreCase))
            {
                return (SyntaxFactory.ParseTypeName("System.Guid"), true);
            }

            if (string.Equals(format, "duration", StringComparison.OrdinalIgnoreCase))
            {
                return (SyntaxFactory.ParseTypeName("System.TimeSpan"), true);
            }

            return (SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)), false);
        }
    }
}
