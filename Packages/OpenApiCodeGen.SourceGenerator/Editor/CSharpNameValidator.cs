using System;
using System.Collections.Generic;
using System.Globalization;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal static class CSharpNameValidator
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(
            new[]
            {
                "abstract", "add", "alias", "and", "as", "ascending", "async", "await",
                "base", "bool", "break", "by", "byte", "case", "catch", "char", "checked",
                "class", "const", "continue", "decimal", "default", "delegate", "descending",
                "do", "double", "dynamic", "else", "enum", "equals", "event", "explicit",
                "extern", "false", "file", "finally", "fixed", "float", "for", "foreach",
                "from", "get", "global", "goto", "group", "if", "implicit", "in", "init",
                "int", "interface", "internal", "into", "is", "join", "let", "lock", "long",
                "managed", "nameof", "namespace", "new", "nint", "not", "notnull", "nuint",
                "null", "object", "on", "operator", "or", "orderby", "out", "override",
                "params", "partial", "private", "protected", "public", "readonly", "record",
                "ref", "remove", "required", "return", "sbyte", "scoped", "sealed", "select",
                "set", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
                "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
                "unmanaged", "unsafe", "ushort", "using", "value", "var", "virtual", "void",
                "volatile", "when", "where", "while", "with", "yield",
            },
            StringComparer.Ordinal);

        internal static bool TryValidateIdentifier(string value, out string failureReason)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                failureReason = "A C# identifier is required.";
                return false;
            }

            if (Keywords.Contains(value))
            {
                failureReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0}' is a C# keyword and cannot be used as an identifier.",
                    value);
                return false;
            }

            int index = 0;
            if (!TryReadCategory(value, index, out UnicodeCategory firstCategory, out int firstLength) ||
                !IsIdentifierStart(value[index], firstCategory))
            {
                failureReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0}' is not a valid C# identifier.",
                    value);
                return false;
            }

            index += firstLength;
            while (index < value.Length)
            {
                if (!TryReadCategory(value, index, out UnicodeCategory category, out int characterLength) ||
                    !IsIdentifierPart(value[index], category))
                {
                    failureReason = string.Format(
                        CultureInfo.InvariantCulture,
                        "'{0}' is not a valid C# identifier.",
                        value);
                    return false;
                }

                index += characterLength;
            }

            failureReason = string.Empty;
            return true;
        }

        internal static bool TryValidateNamespace(string value, out string failureReason)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                failureReason = "A generated namespace is required.";
                return false;
            }

            string[] segments = value.Split('.');
            for (int index = 0; index < segments.Length; index++)
            {
                if (!TryValidateIdentifier(segments[index], out string identifierFailure))
                {
                    failureReason = string.Format(
                        CultureInfo.InvariantCulture,
                        "Generated namespace '{0}' is invalid: {1}",
                        value,
                        identifierFailure);
                    return false;
                }
            }

            failureReason = string.Empty;
            return true;
        }

        private static bool TryReadCategory(
            string value,
            int index,
            out UnicodeCategory category,
            out int characterLength)
        {
            char character = value[index];
            if (char.IsLowSurrogate(character))
            {
                category = UnicodeCategory.Surrogate;
                characterLength = 1;
                return false;
            }

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    category = UnicodeCategory.Surrogate;
                    characterLength = 1;
                    return false;
                }

                category = CharUnicodeInfo.GetUnicodeCategory(value, index);
                characterLength = 2;
                return true;
            }

            category = char.GetUnicodeCategory(character);
            characterLength = 1;
            return true;
        }

        private static bool IsIdentifierStart(char character, UnicodeCategory category)
        {
            return character == '_' ||
                   category == UnicodeCategory.UppercaseLetter ||
                   category == UnicodeCategory.LowercaseLetter ||
                   category == UnicodeCategory.TitlecaseLetter ||
                   category == UnicodeCategory.ModifierLetter ||
                   category == UnicodeCategory.OtherLetter ||
                   category == UnicodeCategory.LetterNumber;
        }

        private static bool IsIdentifierPart(char character, UnicodeCategory category)
        {
            return IsIdentifierStart(character, category) ||
                   category == UnicodeCategory.DecimalDigitNumber ||
                   category == UnicodeCategory.ConnectorPunctuation ||
                   category == UnicodeCategory.NonSpacingMark ||
                   category == UnicodeCategory.SpacingCombiningMark ||
                   category == UnicodeCategory.Format;
        }
    }
}
