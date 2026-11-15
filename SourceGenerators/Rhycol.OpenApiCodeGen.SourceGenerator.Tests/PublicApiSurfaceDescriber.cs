using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Describes the externally visible surface (public / protected) of the source-defined types
    /// in a namespace from Roslyn symbols. One line per type and per member. Attributes (explicit
    /// ones only), constant values, nullable annotations, virtual/abstract/override/sealed,
    /// generic constraints and default parameter values are part of each line.
    /// </summary>
    internal static class PublicApiSurfaceDescriber
    {
        private static readonly SymbolDisplayFormat TypeFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters |
                             SymbolDisplayGenericsOptions.IncludeVariance,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                                  SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
                                  SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

        internal static IReadOnlyList<string> Describe(Compilation compilation, string ns)
        {
            INamespaceSymbol? root = compilation.GlobalNamespace;
            foreach (string part in ns.Split('.'))
            {
                root = root?.GetNamespaceMembers().FirstOrDefault(member => member.Name == part);
            }

            var lines = new SortedSet<string>(StringComparer.Ordinal);
            if (root is not null)
            {
                foreach (INamedTypeSymbol type in EnumerateTypes(root))
                {
                    DescribeType(type, lines);
                }
            }

            return lines.ToArray();
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
        {
            foreach (INamedTypeSymbol type in ns.GetTypeMembers())
            {
                yield return type;
            }

            foreach (INamespaceSymbol child in ns.GetNamespaceMembers())
            {
                foreach (INamedTypeSymbol type in EnumerateTypes(child))
                {
                    yield return type;
                }
            }
        }

        private static bool IsExposed(ISymbol symbol)
        {
            for (ISymbol? current = symbol; current is not null && current is not INamespaceSymbol;
                 current = current.ContainingType)
            {
                if (current.DeclaredAccessibility != Accessibility.Public &&
                    current.DeclaredAccessibility != Accessibility.Protected &&
                    current.DeclaredAccessibility != Accessibility.ProtectedOrInternal)
                {
                    return false;
                }
            }

            return true;
        }

        private static void DescribeType(INamedTypeSymbol type, ISet<string> lines)
        {
            if (!IsExposed(type))
            {
                return;
            }

            string name = type.ToDisplayString(TypeFormat);
            var bases = new List<string>();
            if (type.TypeKind == TypeKind.Enum)
            {
                bases.Add(type.EnumUnderlyingType!.ToDisplayString(TypeFormat));
            }
            else
            {
                if (type.BaseType is not null && type.TypeKind == TypeKind.Class &&
                    type.BaseType.SpecialType != SpecialType.System_Object)
                {
                    bases.Add(type.BaseType.ToDisplayString(TypeFormat));
                }

                bases.AddRange(type.Interfaces
                    .Select(static i => i.ToDisplayString(TypeFormat))
                    .OrderBy(static value => value, StringComparer.Ordinal));
            }

            string kind = type.TypeKind switch
            {
                TypeKind.Enum => "enum",
                TypeKind.Interface => "interface",
                TypeKind.Struct => "struct",
                TypeKind.Delegate => "delegate",
                _ => "class"
            };
            string modifiers = Modifiers(type);
            lines.Add(Attributes(type) + "type " + AccessText(type) + modifiers + kind + " " + name +
                      (bases.Count == 0 ? string.Empty : " : " + string.Join(", ", bases)) +
                      Constraints(type.TypeParameters));

            foreach (ISymbol member in type.GetMembers())
            {
                if (member is INamedTypeSymbol nested)
                {
                    DescribeType(nested, lines);
                    continue;
                }

                if (!IsExposed(member))
                {
                    continue;
                }

                if (member.IsImplicitlyDeclared && !(member is IMethodSymbol { MethodKind: MethodKind.Constructor }))
                {
                    continue;
                }

                string prefix = name + " ";
                switch (member)
                {
                    case IFieldSymbol field:
                        lines.Add(Attributes(field) + prefix + "field " + AccessText(field) +
                                  (field.IsConst ? "const " : (field.IsStatic ? "static " : string.Empty)) +
                                  (field.IsReadOnly ? "readonly " : string.Empty) +
                                  field.Type.ToDisplayString(TypeFormat) + " " + field.Name +
                                  (field.HasConstantValue
                                      ? " = " + (field.ContainingType.TypeKind == TypeKind.Enum ? Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture) : FormatConstant(field.Type, field.ConstantValue))
                                      : string.Empty));
                        break;
                    case IPropertySymbol property:
                        lines.Add(Attributes(property) + prefix + "property " + AccessText(property) +
                                  Modifiers(property) +
                                  property.Type.ToDisplayString(TypeFormat) + " " +
                                  (property.IsIndexer
                                      ? "this[" + FormatParameters(property.Parameters) + "]"
                                      : property.Name) +
                                  " {" + Accessor(property, property.GetMethod, "get") +
                                  Accessor(property, property.SetMethod, "set") + " }");
                        break;
                    case IMethodSymbol method when method.MethodKind == MethodKind.Constructor:
                        lines.Add(Attributes(method) + prefix + "ctor " + AccessText(method) +
                                  "(" + FormatParameters(method.Parameters) + ")");
                        break;
                    case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary ||
                                                   method.MethodKind == MethodKind.UserDefinedOperator ||
                                                   method.MethodKind == MethodKind.Conversion:
                        lines.Add(Attributes(method) + prefix + "method " + AccessText(method) +
                                  Modifiers(method) +
                                  method.ReturnType.ToDisplayString(TypeFormat) + " " + method.Name +
                                  (method.TypeParameters.Length == 0
                                      ? string.Empty
                                      : "<" + string.Join(",", method.TypeParameters.Select(static p => p.Name)) + ">") +
                                  "(" + FormatParameters(method.Parameters) + ")" +
                                  Constraints(method.TypeParameters));
                        break;
                    case IEventSymbol evt:
                        lines.Add(Attributes(evt) + prefix + "event " + AccessText(evt) + Modifiers(evt) +
                                  evt.Type.ToDisplayString(TypeFormat) + " " + evt.Name);
                        break;
                }
            }
        }

        private static string Accessor(IPropertySymbol property, IMethodSymbol? accessor, string keyword)
        {
            if (accessor is null)
            {
                return string.Empty;
            }

            if (accessor.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.NotApplicable ||
                accessor.DeclaredAccessibility == property.DeclaredAccessibility)
            {
                return " " + AccessorKeyword(accessor, keyword) + ";";
            }

            return accessor.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Private ||
                   accessor.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Internal ||
                   accessor.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal
                ? string.Empty
                : " " + AccessibilityKeyword(accessor.DeclaredAccessibility) + " " +
                  AccessorKeyword(accessor, keyword) + ";";
        }

        private static string AccessorKeyword(IMethodSymbol accessor, string keyword)
        {
            return keyword == "set" && accessor.IsInitOnly ? "init" : keyword;
        }

        private static string AccessText(ISymbol symbol)
        {
            return AccessibilityKeyword(symbol.DeclaredAccessibility) + " ";
        }

        private static string AccessibilityKeyword(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Microsoft.CodeAnalysis.Accessibility.Public:
                    return "public";
                case Microsoft.CodeAnalysis.Accessibility.Protected:
                    return "protected";
                case Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal:
                    return "protected internal";
                default:
                    return accessibility.ToString().ToLowerInvariant();
            }
        }

        private static string Modifiers(ISymbol symbol)
        {
            string result = string.Empty;
            if (symbol is INamedTypeSymbol type)
            {
                if (type.TypeKind == TypeKind.Class)
                {
                    result += type.IsStatic ? "static " : type.IsAbstract ? "abstract " : type.IsSealed ? "sealed " : string.Empty;
                }
                else if (type.TypeKind == TypeKind.Struct && type.IsReadOnly)
                {
                    result += "readonly ";
                }

                return result;
            }

            if (symbol.IsStatic)
            {
                result += "static ";
            }

            if (symbol.IsAbstract)
            {
                result += "abstract ";
            }
            else if (symbol.IsVirtual)
            {
                result += "virtual ";
            }

            if (symbol.IsOverride)
            {
                result += "override ";
            }

            if (symbol.IsSealed && symbol.IsOverride)
            {
                result += "sealed ";
            }

            return result;
        }

        private static string Constraints(IEnumerable<ITypeParameterSymbol> typeParameters)
        {
            var parts = new List<string>();
            foreach (ITypeParameterSymbol parameter in typeParameters)
            {
                var constraints = new List<string>();
                if (parameter.HasReferenceTypeConstraint)
                {
                    constraints.Add("class");
                }

                if (parameter.HasValueTypeConstraint)
                {
                    constraints.Add("struct");
                }

                if (parameter.HasUnmanagedTypeConstraint)
                {
                    constraints.Add("unmanaged");
                }

                if (parameter.HasNotNullConstraint)
                {
                    constraints.Add("notnull");
                }

                constraints.AddRange(parameter.ConstraintTypes.Select(static t => t.ToDisplayString(TypeFormat)));
                if (parameter.HasConstructorConstraint)
                {
                    constraints.Add("new()");
                }

                if (constraints.Count > 0)
                {
                    parts.Add(" where " + parameter.Name + " : " + string.Join(", ", constraints));
                }
            }

            return string.Concat(parts);
        }

        private static string FormatParameters(IEnumerable<IParameterSymbol> parameters)
        {
            return string.Join(", ", parameters.Select(parameter =>
                (parameter.IsParams ? "params " : string.Empty) +
                (parameter.RefKind == RefKind.None ? string.Empty : parameter.RefKind.ToString().ToLowerInvariant() + " ") +
                parameter.Type.ToDisplayString(TypeFormat) + " " + parameter.Name +
                (parameter.HasExplicitDefaultValue
                    ? " = " + FormatDefault(parameter.Type, parameter.ExplicitDefaultValue)
                    : string.Empty)));
        }

        private static string FormatDefault(ITypeSymbol type, object? value)
        {
            if (value is null)
            {
                return type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
                    ? "default(" + type.ToDisplayString(TypeFormat) + ")"
                    : "null";
            }

            return FormatConstant(type, value);
        }

        private static string FormatConstant(ITypeSymbol type, object? value)
        {
            if (value is null)
            {
                return "null";
            }

            ITypeSymbol actualType = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                ? nullable.TypeArguments[0]
                : type;
            if (actualType.TypeKind == TypeKind.Enum)
            {
                IFieldSymbol? field = actualType.GetMembers().OfType<IFieldSymbol>()
                    .FirstOrDefault(candidate => candidate.HasConstantValue &&
                                                 Equals(candidate.ConstantValue, value));
                return field is null
                    ? "(" + actualType.ToDisplayString(TypeFormat) + ")" +
                      Convert.ToString(value, CultureInfo.InvariantCulture)
                    : actualType.ToDisplayString(TypeFormat) + "." + field.Name;
            }

            switch (value)
            {
                case string text:
                    return SymbolDisplay.FormatLiteral(text, quote: true);
                case char character:
                    return SymbolDisplay.FormatLiteral(character, quote: true);
                case bool flag:
                    return flag ? "true" : "false";
                case float single:
                    return single.ToString("R", CultureInfo.InvariantCulture) + "f";
                case double number:
                    return number.ToString("R", CultureInfo.InvariantCulture) + "d";
                case decimal money:
                    return money.ToString(CultureInfo.InvariantCulture) + "m";
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        private static string Attributes(ISymbol symbol)
        {
            string[] attributes = symbol.GetAttributes()
                .Select(FormatAttribute)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
            return attributes.Length == 0
                ? string.Empty
                : string.Concat(attributes.Select(static value => "[" + value + "] "));
        }

        private static string FormatAttribute(AttributeData attribute)
        {
            var arguments = new List<string>();
            arguments.AddRange(attribute.ConstructorArguments.Select(FormatTyped));
            arguments.AddRange(attribute.NamedArguments
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => pair.Key + " = " + FormatTyped(pair.Value)));
            return attribute.AttributeClass!.ToDisplayString(TypeFormat) +
                   (arguments.Count == 0 ? string.Empty : "(" + string.Join(", ", arguments) + ")");
        }

        private static string FormatTyped(TypedConstant constant)
        {
            if (constant.IsNull)
            {
                return "null";
            }

            switch (constant.Kind)
            {
                case TypedConstantKind.Array:
                    return "{" + string.Join(", ", constant.Values.Select(FormatTyped)) + "}";
                case TypedConstantKind.Type:
                    return "typeof(" + ((ITypeSymbol)constant.Value!).ToDisplayString(TypeFormat) + ")";
                default:
                    return FormatConstant(constant.Type!, constant.Value);
            }
        }
    }
}
