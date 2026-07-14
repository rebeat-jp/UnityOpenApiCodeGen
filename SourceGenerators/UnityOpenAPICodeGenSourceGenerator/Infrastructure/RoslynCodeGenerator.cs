using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// CSharpFile から Roslyn 構文木を使ってコードを生成する。
    /// Generates C# code from CSharpFile using Roslyn.
    /// </summary>
    internal sealed class RoslynCodeGenerator : ICSharpCodeGenerator
    {
        /// <summary>
        /// CSharpFile から GeneratedFile を生成する。
        /// Generates a source file from CSharpFile.
        /// </summary>
        public GeneratedFile Generate(CSharpFile file)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            var compilationUnit = SyntaxFactory.CompilationUnit()
                .AddUsings(CreateUsings(file.Usings));

            var typeDeclarations = file.Types.Select(CreateTypeDeclaration).ToArray();
            if (!string.IsNullOrWhiteSpace(file.Namespace))
            {
                var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(
                        SyntaxFactory.ParseName(file.Namespace))
                    .AddMembers(typeDeclarations);
                compilationUnit = compilationUnit.AddMembers(namespaceDeclaration);
            }
            else
            {
                compilationUnit = compilationUnit.AddMembers(typeDeclarations);
            }

            var content = compilationUnit.NormalizeWhitespace().ToFullString();
            return new GeneratedFile(file.FileName, content);
        }

        /// <summary>
        /// using ディレクティブを生成する。
        /// Creates using directives.
        /// </summary>
        private static UsingDirectiveSyntax[] CreateUsings(IReadOnlyList<string> usings)
        {
            if (usings is null || usings.Count == 0)
            {
                return Array.Empty<UsingDirectiveSyntax>();
            }

            return usings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Select(value => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(value)))
                .ToArray();
        }

        /// <summary>
        /// 型宣言の種類ごとに Roslyn 構文を生成する。
        /// Builds Roslyn syntax for type declarations.
        /// </summary>
        private static MemberDeclarationSyntax CreateTypeDeclaration(CSharpTypeDeclaration declaration)
        {
            switch (declaration)
            {
                case CSharpClassDeclaration classDeclaration:
                    return CreateClassDeclaration(classDeclaration);
                case CSharpStructDeclaration structDeclaration:
                    return CreateStructDeclaration(structDeclaration);
                case CSharpInterfaceDeclaration interfaceDeclaration:
                    return CreateInterfaceDeclaration(interfaceDeclaration);
                case CSharpEnumDeclaration enumDeclaration:
                    return CreateEnumDeclaration(enumDeclaration);
                default:
                    throw new NotSupportedException($"Unsupported type declaration: {declaration?.GetType().Name}");
            }
        }

        /// <summary>
        /// クラス宣言を生成する。
        /// Creates class declaration.
        /// </summary>
        private static ClassDeclarationSyntax CreateClassDeclaration(CSharpClassDeclaration declaration)
        {
            var syntax = SyntaxFactory.ClassDeclaration(declaration.Name);
            syntax = ApplyTypeDeclarationCommon(syntax, declaration);
            var members = new List<MemberDeclarationSyntax>();
            members.AddRange(ParseMembers(declaration.Members));
            members.AddRange(CreateMethodMembers(declaration.Methods));
            syntax = syntax.AddMembers(members.ToArray());
            return syntax;
        }

        /// <summary>
        /// 構造体宣言を生成する。
        /// Creates struct declaration.
        /// </summary>
        private static StructDeclarationSyntax CreateStructDeclaration(CSharpStructDeclaration declaration)
        {
            var syntax = SyntaxFactory.StructDeclaration(declaration.Name);
            syntax = ApplyTypeDeclarationCommon(syntax, declaration);
            var members = new List<MemberDeclarationSyntax>();
            members.AddRange(ParseMembers(declaration.Members));
            members.AddRange(CreateMethodMembers(declaration.Methods));
            syntax = syntax.AddMembers(members.ToArray());
            return syntax;
        }

        /// <summary>
        /// インターフェース宣言を生成する。
        /// Creates interface declaration.
        /// </summary>
        private static InterfaceDeclarationSyntax CreateInterfaceDeclaration(CSharpInterfaceDeclaration declaration)
        {
            var syntax = SyntaxFactory.InterfaceDeclaration(declaration.Name);
            syntax = ApplyTypeDeclarationCommon(syntax, declaration);
            syntax = syntax.AddMembers(ParseMembers(declaration.Members));
            return syntax;
        }

        /// <summary>
        /// enum 宣言を生成する。
        /// Creates enum declaration.
        /// </summary>
        private static EnumDeclarationSyntax CreateEnumDeclaration(CSharpEnumDeclaration declaration)
        {
            var syntax = SyntaxFactory.EnumDeclaration(declaration.Name);
            syntax = ApplyTypeDeclarationCommon(syntax, declaration);

            var members = declaration.Members.Select(CreateEnumMember).ToArray();
            syntax = syntax.AddMembers(members);
            return syntax;
        }

        /// <summary>
        /// enum メンバー宣言を生成する。
        /// Creates enum member declaration.
        /// </summary>
        private static EnumMemberDeclarationSyntax CreateEnumMember(CSharpEnumMember member)
        {
            var enumMember = SyntaxFactory.EnumMemberDeclaration(member.Name);

            if (member.Value is not null && !string.IsNullOrWhiteSpace(member.Value))
            {
                enumMember = enumMember.WithEqualsValue(
                    SyntaxFactory.EqualsValueClause(
                        SyntaxFactory.ParseExpression(member.Value)));
            }

            return enumMember;
        }

        /// <summary>
        /// 型宣言共通の修飾子/属性/ジェネリクス/継承/コメントを付与する。
        /// Applies common type declaration parts.
        /// </summary>
        private static T ApplyTypeDeclarationCommon<T>(T syntax, CSharpTypeDeclaration declaration)
            where T : TypeDeclarationSyntax
        {
            var comments = CreateLeadingTrivia(declaration.Comments);
            if (comments.Count > 0)
            {
                syntax = (T)syntax.WithLeadingTrivia(comments);
            }

            var modifiers = CreateModifiers(declaration.Modifiers);
            if (modifiers.Length > 0)
            {
                syntax = (T)syntax.AddModifiers(modifiers);
            }

            var attributes = CreateAttributes(declaration.Attributes);
            if (attributes.Length > 0)
            {
                syntax = (T)syntax.AddAttributeLists(attributes);
            }

            if (declaration.GenericParameters.Count > 0)
            {
                var parameters = declaration.GenericParameters
                    .Select(name => SyntaxFactory.TypeParameter(name))
                    .ToArray();
                syntax = (T)syntax.WithTypeParameterList(
                    SyntaxFactory.TypeParameterList(
                        SyntaxFactory.SeparatedList(parameters)));
            }

            if (declaration.BaseTypes.Count > 0)
            {
                var baseTypes = declaration.BaseTypes
                    .Select(name => SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(name)))
                    .ToArray();
                syntax = (T)syntax.WithBaseList(
                    SyntaxFactory.BaseList(
                        SyntaxFactory.SeparatedList<BaseTypeSyntax>(baseTypes)));
            }

            return syntax;
        }

        /// <summary>
        /// enum 向けの共通要素を付与する。
        /// Applies common enum parts.
        /// </summary>
        private static EnumDeclarationSyntax ApplyTypeDeclarationCommon(EnumDeclarationSyntax syntax, CSharpTypeDeclaration declaration)
        {
            var comments = CreateLeadingTrivia(declaration.Comments);
            if (comments.Count > 0)
            {
                syntax = syntax.WithLeadingTrivia(comments);
            }

            var modifiers = CreateModifiers(declaration.Modifiers);
            if (modifiers.Length > 0)
            {
                syntax = syntax.AddModifiers(modifiers);
            }

            var attributes = CreateAttributes(declaration.Attributes);
            if (attributes.Length > 0)
            {
                syntax = syntax.AddAttributeLists(attributes);
            }

            if (declaration.BaseTypes.Count > 0)
            {
                var baseTypes = declaration.BaseTypes
                    .Select(name => SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(name)))
                    .ToArray();
                syntax = syntax.WithBaseList(
                    SyntaxFactory.BaseList(
                        SyntaxFactory.SeparatedList<BaseTypeSyntax>(baseTypes)));
            }

            return syntax;
        }

        /// <summary>
        /// 修飾子文字列からトークン配列を生成する。
        /// Converts modifier strings to tokens.
        /// </summary>
        private static SyntaxToken[] CreateModifiers(IReadOnlyList<string> modifiers)
        {
            if (modifiers is null || modifiers.Count == 0)
            {
                return Array.Empty<SyntaxToken>();
            }

            var tokens = new List<SyntaxToken>();
            foreach (var modifier in modifiers)
            {
                if (string.IsNullOrWhiteSpace(modifier))
                {
                    continue;
                }

                var kind = SyntaxFacts.GetKeywordKind(modifier);
                if (kind != SyntaxKind.None)
                {
                    tokens.Add(SyntaxFactory.Token(kind));
                }
            }

            return tokens.ToArray();
        }

        /// <summary>
        /// 属性リストを生成する。
        /// Creates attribute lists.
        /// </summary>
        private static AttributeListSyntax[] CreateAttributes(IReadOnlyList<CSharpAttribute> attributes)
        {
            if (attributes is null || attributes.Count == 0)
            {
                return Array.Empty<AttributeListSyntax>();
            }

            var lists = new List<AttributeListSyntax>();
            foreach (var attribute in attributes)
            {
                if (attribute is null || string.IsNullOrWhiteSpace(attribute.Name))
                {
                    continue;
                }

                var arguments = attribute.Arguments ?? Array.Empty<string>();
                var attributeSyntax = SyntaxFactory.Attribute(SyntaxFactory.ParseName(attribute.Name));
                if (arguments.Count > 0)
                {
                    var argumentList = arguments
                        .Where(arg => !string.IsNullOrWhiteSpace(arg))
                        .Select(arg => SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression(arg)))
                        .ToArray();
                    attributeSyntax = attributeSyntax.WithArgumentList(
                        SyntaxFactory.AttributeArgumentList(
                            SyntaxFactory.SeparatedList(argumentList)));
                }

                lists.Add(SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(attributeSyntax)));
            }

            return lists.ToArray();
        }

        /// <summary>
        /// 生のメンバー文字列を Roslyn メンバーに変換する。
        /// Parses raw member strings into Roslyn members.
        /// </summary>
        private static MemberDeclarationSyntax[] ParseMembers(IReadOnlyList<string> members)
        {
            if (members is null || members.Count == 0)
            {
                return Array.Empty<MemberDeclarationSyntax>();
            }

            var list = new List<MemberDeclarationSyntax>();
            foreach (var member in members)
            {
                if (string.IsNullOrWhiteSpace(member))
                {
                    continue;
                }

                var parsed = SyntaxFactory.ParseMemberDeclaration(member);
                if (parsed is not null)
                {
                    list.Add(parsed);
                }
            }

            return list.ToArray();
        }

        /// <summary>
        /// 型コメントを XML ドキュメントとして付与する。
        /// Creates XML doc trivia from comments.
        /// </summary>
        private static SyntaxTriviaList CreateLeadingTrivia(IReadOnlyList<string> comments)
        {
            if (comments is null || comments.Count == 0)
            {
                return SyntaxFactory.TriviaList();
            }

            var lines = comments
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => EscapeXml(line))
                .ToArray();

            if (lines.Length == 0)
            {
                return SyntaxFactory.TriviaList();
            }

            var xml = "/// <summary>\n";
            foreach (var line in lines)
            {
                xml += "/// " + line + "\n";
            }

            xml += "/// </summary>\n";
            return SyntaxFactory.ParseLeadingTrivia(xml);
        }

        /// <summary>
        /// XML 文字列をエスケープする。
        /// Escapes XML characters.
        /// </summary>
        private static string EscapeXml(string value)
        {
            return value.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        /// <summary>
        /// 文字列形式の型制約を Roslyn 形式に変換する。
        /// Converts textual constraints into Roslyn constraint clauses.
        /// </summary>
        private static SyntaxList<TypeParameterConstraintClauseSyntax> CreateConstraintClauses(
            IReadOnlyList<string> constraints)
        {
            if (constraints is null || constraints.Count == 0)
            {
                return SyntaxFactory.List<TypeParameterConstraintClauseSyntax>();
            }

            var clauses = new List<TypeParameterConstraintClauseSyntax>();
            foreach (var constraint in constraints)
            {
                if (string.IsNullOrWhiteSpace(constraint))
                {
                    continue;
                }

                var clause = ParseConstraintClause(constraint);
                if (clause is not null)
                {
                    clauses.Add(clause);
                }
            }

            return SyntaxFactory.List(clauses);
        }

        /// <summary>
        /// 制約句文字列を構文へ変換する。
        /// Parses a constraint clause string.
        /// </summary>
        private static TypeParameterConstraintClauseSyntax? ParseConstraintClause(string clause)
        {
            // 公開 API に制約句単体のパーサーがないため、ダミーのメソッドをパースして抽出する。
            var source = $"public void __M__(){clause} {{ }}";
            var member = SyntaxFactory.ParseMemberDeclaration(source) as MethodDeclarationSyntax;
            if (member is null || member.ConstraintClauses.Count == 0)
            {
                return null;
            }

            return member.ConstraintClauses[0];
        }

        /// <summary>
        /// メソッド宣言をメンバー一覧へ変換する。
        /// Converts method declarations to member list.
        /// </summary>
        private static IEnumerable<MemberDeclarationSyntax> CreateMethodMembers(
            IReadOnlyList<CSharpMethodDeclaration> methods)
        {
            if (methods is null || methods.Count == 0)
            {
                return Array.Empty<MemberDeclarationSyntax>();
            }

            var list = new List<MemberDeclarationSyntax>();
            foreach (var method in methods)
            {
                list.Add(CreateMethodDeclaration(method));
            }

            return list;
        }

        /// <summary>
        /// メソッド宣言を生成する。
        /// Creates method declaration.
        /// </summary>
        private static MethodDeclarationSyntax CreateMethodDeclaration(CSharpMethodDeclaration method)
        {
            var syntax = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.ParseTypeName(method.ReturnType),
                method.Name);

            var modifiers = CreateModifiers(method.Modifiers);
            if (modifiers.Length > 0)
            {
                syntax = syntax.AddModifiers(modifiers);
            }

            var attributes = CreateAttributes(method.Attributes);
            if (attributes.Length > 0)
            {
                syntax = syntax.AddAttributeLists(attributes);
            }

            if (method.GenericParameters.Count > 0)
            {
                var parameters = method.GenericParameters
                    .Select(name => SyntaxFactory.TypeParameter(name))
                    .ToArray();
                syntax = syntax.WithTypeParameterList(
                    SyntaxFactory.TypeParameterList(
                        SyntaxFactory.SeparatedList(parameters)));
            }

            if (method.Constraints.Count > 0)
            {
                var clauses = CreateConstraintClauses(method.Constraints);
                if (clauses.Count > 0)
                {
                    syntax = syntax.WithConstraintClauses(clauses);
                }
            }

            var parameterSyntax = method.Parameters
                .Select(parameter =>
                    SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameter.Name))
                        .WithType(SyntaxFactory.ParseTypeName(parameter.Type)))
                .ToArray();
            syntax = syntax.WithParameterList(
                SyntaxFactory.ParameterList(
                    SyntaxFactory.SeparatedList(parameterSyntax)));

            if (method.Body is null)
            {
                syntax = syntax.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
                return syntax;
            }

            // 本文文字列をブロックとしてパースする。
            var bodySource = "{" + method.Body + "}";
            if (SyntaxFactory.ParseStatement(bodySource) is BlockSyntax block)
            {
                syntax = syntax.WithBody(block);
            }
            else
            {
                syntax = syntax.WithBody(SyntaxFactory.Block());
            }

            return syntax;
        }
    }
}
