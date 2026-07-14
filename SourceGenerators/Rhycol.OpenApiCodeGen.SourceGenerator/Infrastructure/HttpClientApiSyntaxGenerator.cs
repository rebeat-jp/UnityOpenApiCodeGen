using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// HttpClient 向け API クライアント構築ロジック。
    /// HttpClient-specific API client builders.
    /// </summary>
    internal static class HttpClientApiSyntaxGenerator
    {
        /// <summary>
        /// HttpClient 用のフィールド/コンストラクタ群を生成する。
        /// Creates HttpClient field and constructors.
        /// </summary>
        public static MemberDeclarationSyntax[] CreateClientMembers(
            string apiName,
            Func<string, IReadOnlyList<(string Name, string? Summary)>, bool, SyntaxTriviaList> docFactory)
        {
            var httpClientType = SyntaxFactory.ParseTypeName("HttpClient");
            var handlerType = SyntaxFactory.ParseTypeName("HttpMessageHandler");

            var field = SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(httpClientType)
                        .AddVariables(SyntaxFactory.VariableDeclarator("_httpClient")))
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword));

            var httpClientParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier("httpClient"))
                .WithType(httpClientType);

            var httpClientCtor = SyntaxFactory.ConstructorDeclaration(apiName)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(new[] { httpClientParam })))
                .WithBody(SyntaxFactory.Block(
                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName("_httpClient"),
                            SyntaxFactory.BinaryExpression(
                                SyntaxKind.CoalesceExpression,
                                SyntaxFactory.IdentifierName("httpClient"),
                                SyntaxFactory.ThrowExpression(
                                    SyntaxFactory.ObjectCreationExpression(
                                            SyntaxFactory.ParseTypeName("ArgumentNullException"))
                                        .WithArgumentList(
                                            SyntaxFactory.ArgumentList(
                                                SyntaxFactory.SeparatedList(new[]
                                                {
                                                    SyntaxFactory.Argument(
                                                        SyntaxFactory.InvocationExpression(
                                                                SyntaxFactory.IdentifierName("nameof"))
                                                            .WithArgumentList(
                                                                SyntaxFactory.ArgumentList(
                                                                    SyntaxFactory.SeparatedList(new[]
                                                                    {
                                                                        SyntaxFactory.Argument(
                                                                            SyntaxFactory.IdentifierName("httpClient"))
                                                                    }))))
                                                })))))))));

            var handlerParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier("handler"))
                .WithType(handlerType);

            var handlerCtor = SyntaxFactory.ConstructorDeclaration(apiName)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(new[] { handlerParam })))
                .WithInitializer(
                    SyntaxFactory.ConstructorInitializer(
                        SyntaxKind.ThisConstructorInitializer,
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SeparatedList(new[]
                            {
                                SyntaxFactory.Argument(
                                    SyntaxFactory.ObjectCreationExpression(httpClientType)
                                        .WithArgumentList(
                                            SyntaxFactory.ArgumentList(
                                                SyntaxFactory.SeparatedList(new[]
                                                {
                                                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName("handler"))
                                                }))))
                            }))))
                .WithBody(SyntaxFactory.Block());

            var defaultCtor = SyntaxFactory.ConstructorDeclaration(apiName)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .WithInitializer(
                    SyntaxFactory.ConstructorInitializer(
                        SyntaxKind.ThisConstructorInitializer,
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SeparatedList(new[]
                            {
                                // 既定の HttpClient を生成して既存コンストラクタへ委譲する。
                                SyntaxFactory.Argument(
                                    SyntaxFactory.ObjectCreationExpression(httpClientType)
                                        .WithArgumentList(SyntaxFactory.ArgumentList()))
                            }))))
                .WithBody(SyntaxFactory.Block());

            return new MemberDeclarationSyntax[]
            {
                field,
                defaultCtor.WithLeadingTrivia(docFactory(
                    "Creates a new client instance.",
                    Array.Empty<(string Name, string? Summary)>(),
                    false)),
                httpClientCtor.WithLeadingTrivia(docFactory(
                    "Creates a new client instance.",
                    new(string, string?)[] { ("httpClient", "HttpClient instance to use.") },
                    false)),
                handlerCtor.WithLeadingTrivia(docFactory(
                    "Creates a new client instance.",
                    new(string, string?)[] { ("handler", "HttpMessageHandler instance to use.") },
                    false))
            };
        }

        /// <summary>
        /// HttpClient 呼び出し用のメソッド本体を生成する。
        /// Creates HttpClient method body.
        /// </summary>
        public static IEnumerable<StatementSyntax> CreateMethodBody(
            string path,
            string httpMethod,
            IReadOnlyList<(string Name, string? Location, bool Required, bool IsValueType, bool IsNullable)> parameters,
            string? bodyParamName,
            OpenApiRequestBody? requestBody,
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
                    // 必須パラメータは無条件で置換する。
                    statements.Add(SyntaxFactory.ParseStatement(
                        $"url = url.Replace(\"{{{escapedName}}}\", System.Uri.EscapeDataString({replaceValue.Value.ValueExpression}));"));
                }
                else
                {
                    // 任意パラメータは値がある時だけ置換する。
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
                        // 必須パラメータは常にクエリに付与する。
                        statements.Add(SyntaxFactory.ParseStatement(addStatement));
                    }
                    else
                    {
                        // 任意パラメータは値がある場合のみ付与する。
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
                    // 必須ヘッダーは常に追加する。
                    statements.Add(SyntaxFactory.ParseStatement(addHeader));
                }
                else
                {
                    // 任意ヘッダーは値がある場合のみ追加する。
                    statements.Add(SyntaxFactory.ParseStatement($"if ({valueExpression.Value.Condition}) {{ {addHeader} }}"));
                }
            }

            if (bodyParamName is not null)
            {
                usesText = true;
                usesJson = true;
                var mediaType = "application/json";
                if (requestBody is not null && requestBody.Content.Count > 0)
                {
                    mediaType = FindPreferredMediaType(requestBody.Content.Keys, option) ?? mediaType;
                }

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
                // 文字列はそのまま返す。
                statements.Add(SyntaxFactory.ParseStatement("return await response.Content.ReadAsStringAsync();"));
                return statements;
            }

            if (returnTypeName == "byte[]" || returnTypeName == "System.Byte[]")
            {
                // バイナリは byte[] として返す。
                statements.Add(SyntaxFactory.ParseStatement("return await response.Content.ReadAsByteArrayAsync();"));
                return statements;
            }

            usesJson = true;
            var deserializer = option.JsonLibraryType == JsonLibrary.NewtonsoftJson
                ? $"JsonConvert.DeserializeObject<{returnTypeName}>"
                : $"JsonSerializer.Deserialize<{returnTypeName}>";

            statements.Add(SyntaxFactory.ParseStatement("var content = await response.Content.ReadAsStringAsync();"));
            statements.Add(SyntaxFactory.ParseStatement($"return {deserializer}(content)!;"));
            return statements;
        }

        private static (string ValueExpression, string? Condition)? CreateParameterValueExpression(
            (string Name, string? Location, bool Required, bool IsValueType, bool IsNullable) parameter)
        {
            var name = parameter.Name;
            if (parameter.Required || (parameter.IsValueType && !parameter.IsNullable))
            {
                // 必須の値型/参照型は常に ToString 可能とみなす。
                return ($"{name}.ToString()", null);
            }

            if (parameter.IsValueType)
            {
                // Nullable 値型は HasValue を条件にする。
                return ($"{name}.Value.ToString()", $"{name}.HasValue");
            }

            // 参照型は null チェックを条件にする。
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
            // 生成コード内の文字列リテラル用にエスケープする。
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
    }
}
