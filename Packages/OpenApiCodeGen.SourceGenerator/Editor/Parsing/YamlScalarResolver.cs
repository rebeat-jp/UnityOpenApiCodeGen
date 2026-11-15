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

            ValidateLiteralUnicode(token, sourcePath, line, column, offset);

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

        // YAML 1.2 7.3.2: a line break in a single-quoted scalar is folded like in a double-quoted one.
        private static string DecodeSingleQuoted(string token)
        {
            int end = token.Length - 1;
            var builder = new StringBuilder(token.Length - 2);
            int keep = 0;
            for (int index = 1; index < end; index++)
            {
                char character = token[index];
                if (character == '\r' || character == '\n')
                {
                    index = FoldLineBreak(token, index, builder, ref keep, true, true);
                    continue;
                }

                if (character == '\'' && index + 1 < end && token[index + 1] == '\'')
                {
                    index++;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }

        // Folds the line break at token[index] (YAML 1.2 7.3.1): the whitespace that ends the line and the whitespace that
        // starts the next line are dropped, one break becomes a space and k empty lines become k line feeds. Whitespace
        // before 'keep' came from an escape and is never trimmed. An escaped break (insertSpace false) keeps the
        // whitespace before it and adds no space. Returns the index of the last consumed character.
        private static int FoldLineBreak(
            string token,
            int index,
            StringBuilder builder,
            ref int keep,
            bool insertSpace,
            bool trimTrailing)
        {
            if (trimTrailing)
            {
                while (builder.Length > keep && (builder[builder.Length - 1] == ' ' || builder[builder.Length - 1] == '\t'))
                {
                    builder.Length--;
                }
            }

            int end = token.Length - 1;
            int cursor = index;
            int emptyLines = 0;
            bool first = true;
            while (true)
            {
                // cursor is at a line break; step over it.
                if (token[cursor] == '\r' && cursor + 1 < end && token[cursor + 1] == '\n')
                {
                    cursor++;
                }

                cursor++;
                if (!first)
                {
                    emptyLines++;
                }

                first = false;
                while (cursor < end && (token[cursor] == ' ' || token[cursor] == '\t'))
                {
                    cursor++;
                }

                if (cursor < end && (token[cursor] == '\r' || token[cursor] == '\n'))
                {
                    continue;
                }

                break;
            }

            if (emptyLines > 0)
            {
                builder.Append('\n', emptyLines);
            }
            else if (insertSpace)
            {
                builder.Append(' ');
            }

            keep = builder.Length;
            return cursor - 1;
        }

        // Converts an index inside a (possibly multi-line) token to the line, column and offset in the source.
        private static void Locate(
            string token,
            int tokenIndex,
            int line,
            int column,
            int offset,
            out int locatedLine,
            out int locatedColumn,
            out int locatedOffset)
        {
            locatedLine = line;
            locatedOffset = offset + tokenIndex;
            int lineBegin = 0;
            int limit = Math.Min(tokenIndex, token.Length);
            for (int index = 0; index < limit; index++)
            {
                char character = token[index];
                if (character == '\n' || (character == '\r' && !(index + 1 < token.Length && token[index + 1] == '\n')))
                {
                    locatedLine++;
                    lineBegin = index + 1;
                }
            }

            locatedColumn = lineBegin == 0 ? column + tokenIndex : tokenIndex - lineBegin + 1;
        }

        private static YamlParseException CreateExceptionInToken(
            YamlDiagnosticCode code,
            string message,
            string sourcePath,
            string token,
            int tokenIndex,
            int line,
            int column,
            int offset)
        {
            int locatedLine;
            int locatedColumn;
            int locatedOffset;
            Locate(token, tokenIndex, line, column, offset, out locatedLine, out locatedColumn, out locatedOffset);
            return CreateException(code, message, sourcePath, locatedLine, locatedColumn, locatedOffset);
        }

        private static void ValidateLiteralUnicode(
            string token,
            string sourcePath,
            int line,
            int column,
            int offset)
        {
            for (int index = 0; index < token.Length; index++)
            {
                if (char.IsHighSurrogate(token[index]) &&
                    index + 1 < token.Length &&
                    char.IsLowSurrogate(token[index + 1]))
                {
                    index++;
                    continue;
                }

                if (char.IsSurrogate(token[index]))
                {
                    throw CreateExceptionInToken(
                        YamlDiagnosticCode.InvalidScalar,
                        "A YAML scalar contains an unpaired Unicode surrogate.",
                        sourcePath,
                        token,
                        index,
                        line,
                        column,
                        offset);
                }
            }
        }

        private static string DecodeDoubleQuoted(
            string token,
            string sourcePath,
            int line,
            int column,
            int offset)
        {
            var builder = new StringBuilder(token.Length - 2);
            int keep = 0;
            for (int index = 1; index < token.Length - 1; index++)
            {
                char character = token[index];
                if (character == '\r' || character == '\n')
                {
                    index = FoldLineBreak(token, index, builder, ref keep, true, true);
                    continue;
                }

                if (character != '\\')
                {
                    builder.Append(character);
                    continue;
                }

                int escapeStart = index;
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
                if (escape == '\r' || escape == '\n')
                {
                    // An escaped line break joins the lines without a space.
                    index = FoldLineBreak(token, index, builder, ref keep, false, false);
                    continue;
                }

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
                        builder.Append(ParseHexEscape(token, ref index, 2, sourcePath, line, column, offset, escapeStart));
                        break;
                    case 'u':
                        int escapedCodeUnit = ParseHexCodePoint(
                            token, ref index, 4, sourcePath, line, column, offset, escapeStart);
                        if (char.IsHighSurrogate((char)escapedCodeUnit))
                        {
                            int lowEscapeStart = index + 1;
                            if (lowEscapeStart + 1 >= token.Length - 1 ||
                                token[lowEscapeStart] != '\\' || token[lowEscapeStart + 1] != 'u')
                            {
                                throw CreateExceptionInToken(
                                    YamlDiagnosticCode.InvalidScalar,
                                    "A high Unicode surrogate escape must be followed by a low surrogate escape.",
                                    sourcePath,
                                    token,
                                    escapeStart,
                                    line,
                                    column,
                                    offset);
                            }

                            index = lowEscapeStart + 1;
                            int lowCodeUnit = ParseHexCodePoint(
                                token, ref index, 4, sourcePath, line, column, offset, lowEscapeStart);
                            if (!char.IsLowSurrogate((char)lowCodeUnit))
                            {
                                throw CreateExceptionInToken(
                                    YamlDiagnosticCode.InvalidScalar,
                                    "A high Unicode surrogate escape must be followed by a low surrogate escape.",
                                    sourcePath,
                                    token,
                                    lowEscapeStart,
                                    line,
                                    column,
                                    offset);
                            }

                            builder.Append((char)escapedCodeUnit);
                            builder.Append((char)lowCodeUnit);
                        }
                        else if (char.IsLowSurrogate((char)escapedCodeUnit))
                        {
                            throw CreateExceptionInToken(
                                YamlDiagnosticCode.InvalidScalar,
                                "A low Unicode surrogate escape must follow a high surrogate escape.",
                                sourcePath,
                                token,
                                escapeStart,
                                line,
                                column,
                                offset);
                        }
                        else
                        {
                            builder.Append((char)escapedCodeUnit);
                        }

                        break;
                    case 'U':
                        int codePoint = ParseHexCodePoint(
                            token, ref index, 8, sourcePath, line, column, offset, escapeStart);
                        try
                        {
                            builder.Append(char.ConvertFromUtf32(codePoint));
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            throw CreateExceptionInToken(
                                YamlDiagnosticCode.InvalidScalar,
                                "A YAML Unicode escape is outside the Unicode scalar range.",
                                sourcePath,
                                token,
                                escapeStart,
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

                // Whitespace produced by an escape (\t, \ ) is content and is never trimmed at a line break.
                keep = builder.Length;
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
            int offset,
            int errorIndex)
        {
            int codePoint = ParseHexCodePoint(token, ref index, digits, sourcePath, line, column, offset, errorIndex);
            return (char)codePoint;
        }

        private static int ParseHexCodePoint(
            string token,
            ref int index,
            int digits,
            string sourcePath,
            int line,
            int column,
            int offset,
            int errorIndex)
        {
            if (index + digits >= token.Length)
            {
                throw CreateExceptionInToken(
                    YamlDiagnosticCode.InvalidScalar,
                    "A YAML Unicode escape is incomplete.",
                    sourcePath,
                    token,
                    errorIndex,
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
                    throw CreateExceptionInToken(
                        YamlDiagnosticCode.InvalidScalar,
                        "A YAML Unicode escape contains a non-hexadecimal digit.",
                        sourcePath,
                        token,
                        errorIndex,
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
