using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// OpenAPI ドキュメントから CSharpFile を生成する。
    /// Generates CSharpFile from OpenAPI document.
    /// </summary>
    internal sealed class OpenApiCSharpClientFileGenerator : ICSharpFileGenerator<OpenApiDocument>
    {
        /// <summary>
        /// OpenAPI と生成オプションから CSharpFile を構築する。
        /// Builds CSharpFile from OpenAPI document and options.
        /// </summary>
        public CSharpFile Generate(OpenApiDocument document, ApiClientGenerateOption option)
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
                        option,
                        ref usesCollections,
                        ref usesTasks,
                        ref usesText,
                        ref usesJson);

                    // Roslyn syntax を文字列化して CSharpFile 側のメンバーとして保持する。
                    methodMembers.Add(method.NormalizeWhitespace().ToFullString());
                }
            }

            var classBuilder = new CSharpSyntaxGenerator(
                $"{option.ApiName}.g.cs",
                string.IsNullOrWhiteSpace(option.Namespace)
                    ? "ReBeat.OpenApiCodeGen.Generated"
                    : option.Namespace);

            classBuilder.AddUsing("System");
            if (option.HttpLibraryType == HttpLibrary.HttpClient)
            {
                classBuilder.AddUsing("System.Net.Http");
            }

            if (usesCollections)
            {
                classBuilder.AddUsing("System.Collections.Generic");
            }

            if (usesTasks)
            {
                classBuilder.AddUsing("System.Threading.Tasks");
            }

            if (usesText)
            {
                classBuilder.AddUsing("System.Text");
            }

            if (usesJson)
            {
                classBuilder.AddUsing(option.JsonLibraryType == JsonLibrary.NewtonsoftJson
                    ? "Newtonsoft.Json"
                    : "System.Text.Json");
            }

            classBuilder.AddClass(option.ApiName, builder =>
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

            return classBuilder.Build();
        }

        /// <summary>
        /// OpenAPI operation からメソッド構文を生成する。
        /// Builds a method syntax from an OpenAPI operation.
        /// </summary>
        private static MethodDeclarationSyntax CreateMethod(
            string methodName,
            string path,
            string httpMethod,
            OpenApiOperation operation,
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

            foreach (var parameter in operation.Parameters)
            {
                if (parameter.Name == null || string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                var paramName = SanitizeParameterName(parameter.Name);
                paramName = EnsureUniqueName(paramName, usedParamNames);

                var typeInfo = ResolveTypeInfo(parameter.Schema, ref usesCollections);
                var paramType = ApplyNullability(typeInfo.Type, parameter.Required, typeInfo.IsValueType, option);
                parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName)).WithType(paramType));
                paramDocs.Add((paramName, null));
                var isNullable = !parameter.Required && option.UseNullableReferenceTypes;
                httpParameters.Add((paramName, parameter.In, parameter.Required, typeInfo.IsValueType, isNullable));
            }

            string? bodyParamName = null;
            OpenApiRequestBody? requestBody = null;
            if (operation.RequestBody is not null)
            {
                requestBody = operation.RequestBody;
                var bodySchema = GetRequestBodySchema(requestBody, option);
                if (bodySchema is not null)
                {
                    var bodyName = EnsureUniqueName("body", usedParamNames);
                    var typeInfo = ResolveTypeInfo(bodySchema, ref usesCollections);
                    var paramType = ApplyNullability(typeInfo.Type, operation.RequestBody.Required, typeInfo.IsValueType, option);
                    parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(bodyName)).WithType(paramType));
                    paramDocs.Add((bodyName, null));
                    bodyParamName = bodyName;
                }
            }

            var returnSchema = GetResponseSchema(operation.Responses, option);
            var returnType = returnSchema is null
                ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword))
                : ResolveTypeInfo(returnSchema, ref usesCollections).Type;

            var isHttpClient = option.HttpLibraryType == HttpLibrary.HttpClient;
            var methodReturnType = returnType;
            var modifiers = new List<SyntaxToken> { SyntaxFactory.Token(SyntaxKind.PublicKeyword) };
            var statements = new List<StatementSyntax>();

            if (isHttpClient)
            {
                // HttpClient モードでは async Task を返す。
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

                statements.AddRange(HttpClientApiSyntaxGenerator.CreateMethodBody(
                    path,
                    httpMethod,
                    httpParameters,
                    bodyParamName,
                    requestBody,
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
        /// OpenAPI operation の XML ドキュメントコメントを生成する。
        /// Creates XML doc comments for an operation.
        /// </summary>
        private static SyntaxTriviaList CreateDocumentationTrivia(
            OpenApiOperation operation,
            IReadOnlyList<(string Name, string? Summary)> parameters,
            bool hasReturn)
        {
            var summary = operation.Summary;
            string operationId = operation.OperationId ?? string.Empty;

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

        /// <summary>
        /// 汎用の XML ドキュメントコメントを生成する。
        /// Creates XML doc comments for a generic summary.
        /// </summary>
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

        /// <summary>
        /// requestBody から優先 schema を取得する。
        /// Picks preferred schema from requestBody.
        /// </summary>
        private static OpenApiSchema? GetRequestBodySchema(OpenApiRequestBody requestBody, ApiClientGenerateOption option)
        {
            if (requestBody.Content.Count == 0)
            {
                return null;
            }

            var preferred = FindPreferredMediaType(requestBody.Content.Keys, option);
            if (preferred is not null && requestBody.Content.TryGetValue(preferred, out var preferredMedia))
            {
                return preferredMedia.Schema;
            }

            return requestBody.Content.Values.FirstOrDefault()?.Schema;
        }

        /// <summary>
        /// レスポンス一覧から優先 schema を取得する。
        /// Picks preferred schema from responses.
        /// </summary>
        private static OpenApiSchema? GetResponseSchema(IReadOnlyDictionary<string, OpenApiResponse> responses, ApiClientGenerateOption option)
        {
            foreach (var response in responses)
            {
                if (response.Key.StartsWith("2", StringComparison.Ordinal))
                {
                    var schema = GetResponseSchema(response.Value, option);
                    if (schema is not null)
                    {
                        return schema;
                    }
                }
            }

            if (responses.TryGetValue("default", out var defaultResponse))
            {
                return GetResponseSchema(defaultResponse, option);
            }

            foreach (var response in responses.Values)
            {
                var schema = GetResponseSchema(response, option);
                if (schema is not null)
                {
                    return schema;
                }
            }

            return null;
        }

        /// <summary>
        /// 単一レスポンスから schema を取得する。
        /// Gets schema from a response.
        /// </summary>
        private static OpenApiSchema? GetResponseSchema(OpenApiResponse response, ApiClientGenerateOption option)
        {
            if (response.Content.Count == 0)
            {
                return null;
            }

            var preferred = FindPreferredMediaType(response.Content.Keys, option);
            if (preferred is not null && response.Content.TryGetValue(preferred, out var preferredMedia))
            {
                return preferredMedia.Schema;
            }

            return response.Content.Values.FirstOrDefault()?.Schema;
        }

        /// <summary>
        /// OpenAPI schema から CLR 型情報を解決する。
        /// Resolves CLR type info from schema.
        /// </summary>
        private static (TypeSyntax Type, bool IsValueType) ResolveTypeInfo(
            OpenApiSchema schema,
            ref bool usesCollections)
        {
            if (schema.ContentSchema is not null)
            {
                return ResolveTypeInfo(schema.ContentSchema, ref usesCollections);
            }

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
                    return MapStringType(schema);
                case "array":
                    usesCollections = true;
                    var itemSchema = schema.Items ?? schema.PrefixItems.FirstOrDefault();
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

        /// <summary>
        /// 合成スキーマを単一 CLR 型へ解決する。
        /// Resolves composite schema to a single CLR type.
        /// </summary>
        private static (TypeSyntax Type, bool IsValueType)? ResolveCompositeType(OpenApiSchema schema)
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

        /// <summary>
        /// プリミティブ型を CLR 型へマッピングする。
        /// Maps primitive type to CLR type.
        /// </summary>
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

        /// <summary>
        /// CLR 型名が値型かどうか判定する。
        /// Checks whether type name is a value type.
        /// </summary>
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

        /// <summary>
        /// 必須/Nullable 設定に応じて null 許容を付与する。
        /// Applies nullability based on requirements.
        /// </summary>
        private static TypeSyntax ApplyNullability(TypeSyntax type, bool required, bool isValueType, ApiClientGenerateOption option)
        {
            if (required || !option.UseNullableReferenceTypes)
            {
                return type;
            }

            return SyntaxFactory.NullableType(type);
        }

        /// <summary>
        /// 名前の重複を避けるためにユニーク化する。
        /// Ensures a unique name.
        /// </summary>
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

        /// <summary>
        /// メソッド名を識別子として有効な形式に整形する。
        /// Sanitizes a method name for identifier.
        /// </summary>
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

        /// <summary>
        /// パラメータ名を識別子として有効な形式に整形する。
        /// Sanitizes a parameter name for identifier.
        /// </summary>
        private static string SanitizeParameterName(string name)
        {
            var sanitized = SanitizeMethodName(name);
            if (SyntaxFacts.GetKeywordKind(sanitized) != SyntaxKind.None)
            {
                return "@" + sanitized;
            }

            return sanitized;
        }

        /// <summary>
        /// 優先順位に従って media type を選択する。
        /// Chooses preferred media type by priority.
        /// </summary>
        private static string? FindPreferredMediaType(IEnumerable<string> available, ApiClientGenerateOption option)
        {
            foreach (var preferred in option.MediaTypePriority)
            {
                if (string.Equals(preferred, "*/*", StringComparison.Ordinal))
                {
                    return available.FirstOrDefault();
                }

                if (preferred.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = preferred.Substring(preferred.IndexOf('+'));
                    var match = available.FirstOrDefault(value =>
                        value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        return match;
                    }
                }

                foreach (var value in available)
                {
                    if (string.Equals(value, preferred, StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// string 型スキーマの format/content を CLR 型へマッピングする。
        /// Maps string schema with format/content to CLR type.
        /// </summary>
        private static (TypeSyntax Type, bool IsValueType) MapStringType(OpenApiSchema schema)
        {
            if (string.Equals(schema.Format, "binary", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(schema.Format, "byte", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(schema.ContentEncoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                var byteArray = SyntaxFactory.ArrayType(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
                    .AddRankSpecifiers(SyntaxFactory.ArrayRankSpecifier());
                return (byteArray, false);
            }

            if (!string.IsNullOrWhiteSpace(schema.ContentMediaType) &&
                string.Equals(schema.ContentMediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                var byteArray = SyntaxFactory.ArrayType(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
                    .AddRankSpecifiers(SyntaxFactory.ArrayRankSpecifier());
                return (byteArray, false);
            }

            return MapStringFormat(schema.Format);
        }

        /// <summary>
        /// string format を CLR 型へマッピングする。
        /// Maps string format to CLR type.
        /// </summary>
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
