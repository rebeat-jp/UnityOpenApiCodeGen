using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    /// <summary>
    /// Parses one strict RFC 8259 value while retaining the original number lexemes and token locations.
    /// Json.NET performs a streaming validation pass and decodes each JSON string; the small source cursor
    /// supplies information that JsonTextReader intentionally does not retain.
    /// </summary>
    internal sealed class StrictJsonSpecParser
    {
        private readonly string source;
        private readonly string sourcePath;
        private readonly List<NumberRange> numberRanges = new List<NumberRange>();
        private int index;
        private int line = 1;
        private int column = 1;

        private StrictJsonSpecParser(string source, string sourcePath)
        {
            this.source = source;
            this.sourcePath = sourcePath;
        }

        internal static SpecNode Parse(string source, string sourcePath)
        {
            var parser = new StrictJsonSpecParser(source, sourcePath);
            SpecNode root = parser.ParseValue(0, string.Empty);
            parser.SkipWhitespace();
            if (!parser.IsAtEnd)
            {
                throw parser.CreateException("Only one root JSON value is allowed.", string.Empty);
            }

            ValidateWithJsonNet(parser.CreateJsonNetValidationSource(), sourcePath);
            return root;
        }

        private bool IsAtEnd => index >= source.Length;

        private char Current => source[index];

        private SpecNode ParseValue(int depth, string logicalPath)
        {
            SkipWhitespace();
            if (IsAtEnd)
            {
                throw CreateException("A JSON value was expected.", logicalPath);
            }

            int valueLine = line;
            int valueColumn = column;
            switch (Current)
            {
                case '{':
                    return ParseObject(depth + 1, logicalPath, valueLine, valueColumn);
                case '[':
                    return ParseArray(depth + 1, logicalPath, valueLine, valueColumn);
                case '"':
                    return new SpecStringNode(valueLine, valueColumn, ParseString(logicalPath));
                case 't':
                    ConsumeLiteral("true", logicalPath);
                    return new SpecBooleanNode(valueLine, valueColumn, true);
                case 'f':
                    ConsumeLiteral("false", logicalPath);
                    return new SpecBooleanNode(valueLine, valueColumn, false);
                case 'n':
                    ConsumeLiteral("null", logicalPath);
                    return new SpecNullNode(valueLine, valueColumn);
                default:
                    if (Current == '-' || IsDigit(Current))
                    {
                        return ParseNumber(logicalPath, valueLine, valueColumn);
                    }

                    throw CreateException("Unexpected character while reading a JSON value.", logicalPath);
            }
        }

        private SpecObjectNode ParseObject(
            int depth,
            string logicalPath,
            int valueLine,
            int valueColumn)
        {
            EnsureDepth(depth, logicalPath);
            ConsumeExpected('{', logicalPath);
            SkipWhitespace();

            var properties = new List<SpecProperty>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (TryConsume('}'))
            {
                return new SpecObjectNode(valueLine, valueColumn, properties);
            }

            while (true)
            {
                SkipWhitespace();
                if (!IsAtEnd && Current == '/')
                {
                    throw CreateException("JSON comments are not allowed.", logicalPath);
                }

                if (IsAtEnd || Current != '"')
                {
                    throw CreateException("An object property name was expected.", logicalPath);
                }

                int propertyLine = line;
                int propertyColumn = column;
                string propertyName = ParseString(logicalPath);
                string propertyPath = AppendPointer(logicalPath, propertyName);
                if (!names.Add(propertyName))
                {
                    throw new NormalizedSpecException(
                        "Duplicate object property names are not allowed.",
                        sourcePath,
                        propertyLine,
                        propertyColumn,
                        propertyPath);
                }

                SkipWhitespace();
                ConsumeExpected(':', propertyPath);
                SpecNode value = ParseValue(depth, propertyPath);
                properties.Add(new SpecProperty(propertyName, propertyLine, propertyColumn, value));

                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return new SpecObjectNode(valueLine, valueColumn, properties);
                }

                ConsumeExpected(',', logicalPath);
                SkipWhitespace();
                if (!IsAtEnd && Current == '}')
                {
                    throw CreateException("Trailing commas are not allowed.", logicalPath);
                }
            }
        }

        private SpecArrayNode ParseArray(
            int depth,
            string logicalPath,
            int valueLine,
            int valueColumn)
        {
            EnsureDepth(depth, logicalPath);
            ConsumeExpected('[', logicalPath);
            SkipWhitespace();

            var items = new List<SpecNode>();
            if (TryConsume(']'))
            {
                return new SpecArrayNode(valueLine, valueColumn, items);
            }

            while (true)
            {
                string itemPath = AppendPointer(logicalPath, items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                items.Add(ParseValue(depth, itemPath));

                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return new SpecArrayNode(valueLine, valueColumn, items);
                }

                ConsumeExpected(',', logicalPath);
                SkipWhitespace();
                if (!IsAtEnd && Current == ']')
                {
                    throw CreateException("Trailing commas are not allowed.", logicalPath);
                }
            }
        }

        private string ParseString(string logicalPath)
        {
            int startIndex = index;
            int tokenLine = line;
            int tokenColumn = column;
            ConsumeExpected('"', logicalPath);

            while (!IsAtEnd)
            {
                char character = Current;
                if (character == '"')
                {
                    ConsumeCharacter();
                    string lexeme = source.Substring(startIndex, index - startIndex);
                    return DecodeStringWithJsonNet(lexeme, sourcePath, tokenLine, tokenColumn, logicalPath);
                }

                if (character < 0x20)
                {
                    throw CreateException("Unescaped control characters are not allowed in JSON strings.", logicalPath);
                }

                if (character == '\\')
                {
                    ConsumeCharacter();
                    if (IsAtEnd)
                    {
                        throw CreateException("A JSON escape sequence is incomplete.", logicalPath);
                    }

                    char escape = Current;
                    if (escape == 'u')
                    {
                        ConsumeCharacter();
                        for (int offset = 0; offset < 4; offset++)
                        {
                            if (IsAtEnd || !IsHexDigit(Current))
                            {
                                throw CreateException("A Unicode escape must contain four hexadecimal digits.", logicalPath);
                            }

                            ConsumeCharacter();
                        }

                        continue;
                    }

                    if (escape != '"' && escape != '\\' && escape != '/' &&
                        escape != 'b' && escape != 'f' && escape != 'n' &&
                        escape != 'r' && escape != 't')
                    {
                        throw CreateException("The JSON string contains an invalid escape sequence.", logicalPath);
                    }

                    ConsumeCharacter();
                    continue;
                }

                ConsumeCharacter();
            }

            throw CreateException("The JSON string is not terminated.", logicalPath);
        }

        private SpecNumberNode ParseNumber(string logicalPath, int valueLine, int valueColumn)
        {
            int startIndex = index;
            bool isInteger = true;

            TryConsume('-');
            if (IsAtEnd)
            {
                throw CreateException("A digit must follow the number sign.", logicalPath);
            }

            if (TryConsume('0'))
            {
                if (!IsAtEnd && IsDigit(Current))
                {
                    throw CreateException("Leading zeroes are not allowed in JSON numbers.", logicalPath);
                }
            }
            else
            {
                if (IsAtEnd || Current < '1' || Current > '9')
                {
                    throw CreateException("A JSON number must contain an integer part.", logicalPath);
                }

                ConsumeDigits();
            }

            if (TryConsume('.'))
            {
                isInteger = false;
                if (IsAtEnd || !IsDigit(Current))
                {
                    throw CreateException("A digit must follow the decimal point.", logicalPath);
                }

                ConsumeDigits();
            }

            if (!IsAtEnd && (Current == 'e' || Current == 'E'))
            {
                isInteger = false;
                ConsumeCharacter();
                if (!IsAtEnd && (Current == '+' || Current == '-'))
                {
                    ConsumeCharacter();
                }

                if (IsAtEnd || !IsDigit(Current))
                {
                    throw CreateException("A JSON exponent must contain at least one digit.", logicalPath);
                }

                ConsumeDigits();
            }

            string lexeme = source.Substring(startIndex, index - startIndex);
            numberRanges.Add(new NumberRange(startIndex, index - startIndex));
            return new SpecNumberNode(valueLine, valueColumn, isInteger, lexeme);
        }

        private void ConsumeDigits()
        {
            while (!IsAtEnd && IsDigit(Current))
            {
                ConsumeCharacter();
            }
        }

        private void ConsumeLiteral(string expected, string logicalPath)
        {
            for (int offset = 0; offset < expected.Length; offset++)
            {
                if (IsAtEnd || Current != expected[offset])
                {
                    throw CreateException("The JSON literal is invalid.", logicalPath);
                }

                ConsumeCharacter();
            }
        }

        private void EnsureDepth(int depth, string logicalPath)
        {
            if (depth > NormalizedSpecBundleConstants.MaximumDepth)
            {
                throw CreateException(
                    "The JSON nesting depth exceeds the supported maximum of 256.",
                    logicalPath);
            }
        }

        private void SkipWhitespace()
        {
            while (!IsAtEnd)
            {
                char character = Current;
                if (character != ' ' && character != '\t' && character != '\r' && character != '\n')
                {
                    return;
                }

                ConsumeCharacter();
            }
        }

        private void ConsumeExpected(char expected, string logicalPath)
        {
            if (IsAtEnd || Current != expected)
            {
                throw CreateException("Expected '" + expected + "'.", logicalPath);
            }

            ConsumeCharacter();
        }

        private bool TryConsume(char expected)
        {
            if (IsAtEnd || Current != expected)
            {
                return false;
            }

            ConsumeCharacter();
            return true;
        }

        private void ConsumeCharacter()
        {
            char character = source[index++];
            if (character == '\r')
            {
                if (!IsAtEnd && Current == '\n')
                {
                    index++;
                }

                line++;
                column = 1;
                return;
            }

            if (character == '\n')
            {
                line++;
                column = 1;
                return;
            }

            column++;
        }

        private NormalizedSpecException CreateException(string message, string logicalPath)
        {
            return new NormalizedSpecException(message, sourcePath, line, column, logicalPath);
        }

        private string CreateJsonNetValidationSource()
        {
            if (numberRanges.Count == 0)
            {
                return source;
            }

            char[] masked = source.ToCharArray();
            foreach (NumberRange range in numberRanges)
            {
                masked[range.Start] = '0';
                for (int offset = 1; offset < range.Length; offset++)
                {
                    masked[range.Start + offset] = ' ';
                }
            }

            return new string(masked);
        }

        private static string AppendPointer(string parent, string segment)
        {
            return parent + "/" + segment.Replace("~", "~0").Replace("/", "~1");
        }

        private static bool IsDigit(char character)
        {
            return character >= '0' && character <= '9';
        }

        private static bool IsHexDigit(char character)
        {
            return (character >= '0' && character <= '9') ||
                   (character >= 'a' && character <= 'f') ||
                   (character >= 'A' && character <= 'F');
        }

        private static string DecodeStringWithJsonNet(
            string lexeme,
            string sourcePath,
            int line,
            int column,
            string logicalPath)
        {
            try
            {
                using (var stringReader = new StringReader(lexeme))
                using (var jsonReader = CreateJsonReader(stringReader))
                {
                    if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.String)
                    {
                        throw new JsonReaderException("A JSON string token was expected.");
                    }

                    string value = (string)jsonReader.Value;
                    if (jsonReader.Read())
                    {
                        throw new JsonReaderException("Additional content followed the JSON string.");
                    }

                    return value;
                }
            }
            catch (JsonException exception)
            {
                throw new NormalizedSpecException(
                    "Json.NET could not decode the JSON string.",
                    sourcePath,
                    line,
                    column,
                    logicalPath,
                    exception);
            }
        }

        private static void ValidateWithJsonNet(string source, string sourcePath)
        {
            try
            {
                using (var stringReader = new StringReader(source))
                using (var jsonReader = CreateJsonReader(stringReader))
                {
                    while (jsonReader.Read())
                    {
                        if (jsonReader.TokenType == JsonToken.Comment)
                        {
                            throw new JsonReaderException("JSON comments are not allowed.");
                        }
                    }
                }
            }
            catch (JsonException exception)
            {
                throw new NormalizedSpecException(
                    "Json.NET rejected the raw JSON.",
                    sourcePath,
                    1,
                    1,
                    string.Empty,
                    exception);
            }
        }

        private static JsonTextReader CreateJsonReader(TextReader textReader)
        {
            return new JsonTextReader(textReader)
            {
                DateParseHandling = DateParseHandling.None,
                MaxDepth = NormalizedSpecBundleConstants.MaximumDepth,
                SupportMultipleContent = false,
            };
        }

        private struct NumberRange
        {
            internal NumberRange(int start, int length)
            {
                Start = start;
                Length = length;
            }

            internal int Start { get; }

            internal int Length { get; }
        }
    }
}
