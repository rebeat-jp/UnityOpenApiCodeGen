using System;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal static class GeneratedSourceEmitter
    {
        internal static GeneratedFile Create(string fileName, string source)
        {
            var tree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp9));
            Diagnostic? error = tree.GetDiagnostics()
                .FirstOrDefault(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            if (error is not null)
            {
                throw new InvalidOperationException(
                    "Generated source '" + fileName + "' is invalid: " + error.GetMessage());
            }

            string content = tree.GetCompilationUnitRoot()
                .NormalizeWhitespace(eol: "\n")
                .ToFullString();
            return new GeneratedFile(fileName, content + "\n");
        }

        internal static string TypeName(GeneratedTypeModel type)
        {
            string name;
            switch (type.Kind)
            {
                case GeneratedTypeKind.String:
                    name = "string";
                    break;
                case GeneratedTypeKind.Int32:
                    name = "int";
                    break;
                case GeneratedTypeKind.Int64:
                    name = "long";
                    break;
                case GeneratedTypeKind.Single:
                    name = "float";
                    break;
                case GeneratedTypeKind.Double:
                    name = "double";
                    break;
                case GeneratedTypeKind.Decimal:
                    name = "decimal";
                    break;
                case GeneratedTypeKind.Boolean:
                    name = "bool";
                    break;
                case GeneratedTypeKind.DateTime:
                    name = "global::System.DateTime";
                    break;
                case GeneratedTypeKind.DateTimeOffset:
                    name = "global::System.DateTimeOffset";
                    break;
                case GeneratedTypeKind.Guid:
                    name = "global::System.Guid";
                    break;
                case GeneratedTypeKind.Named:
                case GeneratedTypeKind.NamedEnum:
                    name = type.Name;
                    break;
                case GeneratedTypeKind.Array:
                    name = "global::System.Collections.Generic.List<" + TypeName(type.ItemType!) + ">";
                    break;
                default:
                    throw new InvalidOperationException("Unknown generated type kind.");
            }

            return type.Nullable ? name + "?" : name;
        }

        internal static string StringLiteral(string value)
        {
            return SyntaxFactory.Literal(value ?? string.Empty).ToFullString();
        }
    }
}
