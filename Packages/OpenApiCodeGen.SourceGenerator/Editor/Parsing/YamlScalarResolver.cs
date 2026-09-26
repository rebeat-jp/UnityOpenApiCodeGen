using System;
using System.Globalization;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal static class YamlScalarResolver
    {
        internal static SpecNode Resolve(
            string token,
            string sourcePath,
            int line,
            int column,
            int offset)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (token.Length > YamlParserLimits.MaximumScalarCharacters)
            {
                throw CreateException(
                    YamlDiagnosticCode.LimitExceeded,
                    "The YAML scalar exceeds the supported maximum size.",
                    sourcePath,
                    line,
                    column,
                    offset);
            }

            if (token.Length >= 2 && token[0] == '\'' && token[token.Length - 1] == '\'')
            {
                return new SpecStringNode(line, column, DecodeSingleQuoted(token));
            }

            if (token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
            {
                return new SpecStringNode(line, column, DecodeDoubleQuoted(token, sourcePath, line, column, offset));
            }

            if (string.Equals(token, "null", StringComparison.Ordinal))
            {
                return new SpecNullNode(line, column);
            }

            if (string.Equals(token, "true", StringComparison.Ordinal))
            {
                return new SpecBooleanNode(line, column, true);
            }

            if (string.Equals(token, "false", StringComparison.Ordinal))
            {
                return new SpecBooleanNode(line, column, false);
            }

            bool isInteger;
            if (TryValidateJsonNumber(token, out isInteger))
            {
                return new SpecNumberNode(line, column, isInteger, token);
            }

            return new SpecStringNode(line, column, token);
        }

        internal static bool TryGetString(
            string token,
            string sourcePath,
            int line,
            int column,
            int offset,
            out string value)
        {
            SpecNode node = Resolve(token, sourcePath, line, column, offset);
            var stringNode = node as SpecStringNode;
            if (stringNode == null)
            {
                value = string.Empty;
                return false;
            }

            value = stringNode.Value;
            return true;
        }

        private static string DecodeSingleQuoted(string token)
        {
            string value = token.Substring(1, token.Length - 2);
            return value.Replace("''", "'");
        }

        private static string DecodeDoubleQuoted(
            string token,
            string sourcePath,
            int line,
            int column,
            int offset)
        {
            var builder = new StringBuilder(token.Length - 2);
            for (int index = 1; index < token.Length - 1; index++)
            {
                char character = token[index];
                if (character != '\\')
                {
                    builder.Append(character);
                    continue;
                }

                if (++index >= token.Length - 1)
                {
                    throw CreateException(
                        YamlDiagnosticCode.InvalidScalar,
                        "A double-quoted YAML escape sequence is incomplete.",
                        sourcePath,
                        line,
                        column,
                        offset);
                }

                char escape = token[index];
                switch (escape)
                {
                    case '0': builder.Append('\0'); break;
                    case 'a': builder.Append('\a'); break;
                    case 'b': builder.Append('\b'); break;
                    case 't': builder.Append('\t'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'v': builder.Append('\v'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'r': builder.Append('\r'); break;
                    case 'e': builder.Append('\x1b'); break;
                    case ' ': builder.Append(' '); break;
                    case '"': builder.Append('"'); break;
                    case '/': builder.Append('/'); break;
                    case '\\': builder.Append('\\'); break;
                    case 'N': builder.Append('\u0085'); break;
                    case '_': builder.Append('\u00a0'); break;
                    case 'L': builder.Append('\u2028'); break;
                    case 'P': builder.Append('\u2029'); break;
                    case 'x':
                        builder.Append(ParseHexEscape(token, ref index, 2, sourcePath, line, column, offset));
                        break;
                    case 'u':
                        builder.Append(ParseHexEscape(token, ref index, 4, sourcePath, line, column, offset));
                        break;
                    case 'U':
                        int codePoint = ParseHexCodePoint(token, ref index, 8, sourcePath, line, column, offset);
                        try
                        {
                            builder.Append(char.ConvertFromUtf32(codePoint));
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            throw CreateException(
                                YamlDiagnosticCode.InvalidScalar,
                                "A YAML Unicode escape is outside the Unicode scalar range.",
                                sourcePath,
                                line,
                                column,
                                offset);
                        }

                        break;
                    default:
                        throw CreateException(
                            YamlDiagnosticCode.InvalidScalar,
                            "The double-quoted YAML scalar contains an unsupported escape sequence.",
                            sourcePath,
                            line,
                            column,
                            offset);
                }
            }

            return builder.ToString();
        }

        private static char ParseHexEscape(
            string token,
            ref int index,
            int digits,
            string sourcePath,
            int line,
            int column,
            int offset)
        {
            int codePoint = ParseHexCodePoint(token, ref index, digits, sourcePath, line, column, offset);
            return (char)codePoint;
        }

        private static int ParseHexCodePoint(
            string token,
            ref int index,
            int digits,
            string sourcePath,
            int line,
            int column,
            int offset)
        {
            if (index + digits >= token.Length)
            {
                throw CreateException(
                    YamlDiagnosticCode.InvalidScalar,
                    "A YAML Unicode escape is incomplete.",
                    sourcePath,
                    line,
                    column,
                    offset);
            }

            int value = 0;
            for (int digitIndex = 0; digitIndex < digits; digitIndex++)
            {
                char digit = token[++index];
                int nibble = HexValue(digit);
                if (nibble < 0)
                {
                    throw CreateException(
                        YamlDiagnosticCode.InvalidScalar,
                        "A YAML Unicode escape contains a non-hexadecimal digit.",
                        sourcePath,
                        line,
                        column,
                        offset);
                }

                value = (value << 4) | nibble;
            }

            return value;
        }

        private static bool TryValidateJsonNumber(string value, out bool isInteger)
        {
            int index = 0;
            isInteger = true;
            if (value.Length == 0)
            {
                return false;
            }

            if (value[index] == '-')
            {
                index++;
                if (index == value.Length)
                {
                    return false;
                }
            }

            if (value[index] == '0')
            {
                index++;
                if (index < value.Length && IsDigit(value[index]))
                {
                    return false;
                }
            }
            else
            {
                if (index >= value.Length || value[index] < '1' || value[index] > '9')
                {
                    return false;
                }

                while (index < value.Length && IsDigit(value[index]))
                {
                    index++;
                }
            }

            if (index < value.Length && value[index] == '.')
            {
                isInteger = false;
                index++;
                int fractionStart = index;
                while (index < value.Length && IsDigit(value[index]))
                {
                    index++;
                }

                if (fractionStart == index)
                {
                    return false;
                }
            }

            if (index < value.Length && (value[index] == 'e' || value[index] == 'E'))
            {
                isInteger = false;
                index++;
                if (index < value.Length && (value[index] == '+' || value[index] == '-'))
                {
                    index++;
                }

                int exponentStart = index;
                while (index < value.Length && IsDigit(value[index]))
                {
                    index++;
                }

                if (exponentStart == index)
                {
                    return false;
                }
            }

            return index == value.Length;
        }

        private static int HexValue(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }

        private static bool IsDigit(char value)
        {
            return value >= '0' && value <= '9';
        }

        private static YamlParseException CreateException(
            YamlDiagnosticCode code,
            string message,
            string sourcePath,
            int line,
            int column,
            int offset)
        {
            return new YamlParseException(code, message, sourcePath, line, column, offset);
        }
    }
}
