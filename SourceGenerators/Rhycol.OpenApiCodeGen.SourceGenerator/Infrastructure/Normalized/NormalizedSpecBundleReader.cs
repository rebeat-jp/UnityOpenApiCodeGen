using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class NormalizedSpecBundleReader
    {
        private const int MaximumNodeDepth = 256;

        private readonly string _content;
        private int _index;
        private int _line = 1;
        private int _column = 1;

        private NormalizedSpecBundleReader(string content)
        {
            _content = content;
        }

        private bool IsAtEnd => _index >= _content.Length;

        private char Current => _content[_index];

        internal static NormalizedSpecBundle Read(string content)
        {
            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            return new NormalizedSpecBundleReader(content).ReadBundle();
        }

        private NormalizedSpecBundle ReadBundle()
        {
            ReadStartObject();
            ReadPropertyName("formatVersion", true);
            SkipWhitespace();
            int versionLine = _line;
            int versionColumn = _column;
            string formatVersion = ReadNumberLexeme();
            if (!IsIntegerLexeme(formatVersion))
            {
                throw CreateFormatException("'formatVersion' must be a JSON integer.");
            }

            if (!string.Equals(formatVersion, "1", StringComparison.Ordinal))
            {
                throw new UnsupportedNormalizedSpecVersionException(formatVersion, versionLine, versionColumn);
            }

            ReadPropertyName("specId", false);
            string specId = ReadString();
            if (!IsLowerHex(specId, 32))
            {
                throw CreateFormatException("'specId' must be a lower-case Guid N value.");
            }

            ReadPropertyName("rawSha256", false);
            string rawSha256 = ReadString();
            if (!IsLowerHex(rawSha256, 64))
            {
                throw CreateFormatException("'rawSha256' must be a 64-character lower-case hexadecimal value.");
            }

            ReadPropertyName("rootDocumentId", false);
            string rootDocumentId = ReadString();
            if (!string.Equals(rootDocumentId, "root", StringComparison.Ordinal))
            {
                throw CreateFormatException("'rootDocumentId' must be 'root' in format v1.");
            }

            ReadPropertyName("documents", false);
            ReadStartArray();
            if (TryConsume(']'))
            {
                throw CreateFormatException("'documents' must contain exactly one document in format v1.");
            }

            (string SourcePath, SpecNode Root) document = ReadDocument();
            if (!TryConsume(']'))
            {
                throw CreateFormatException("'documents' must contain exactly one document in format v1.");
            }

            ReadEndObject();
            SkipWhitespace();
            if (!IsAtEnd)
            {
                throw CreateFormatException("Unexpected content follows the bundle root object.");
            }

            return new NormalizedSpecBundle(specId, rawSha256, document.SourcePath, document.Root);
        }

        private (string SourcePath, SpecNode Root) ReadDocument()
        {
            ReadStartObject();
            ReadPropertyName("documentId", true);
            string documentId = ReadString();
            if (!string.Equals(documentId, "root", StringComparison.Ordinal))
            {
                throw CreateFormatException("'documentId' must be 'root' in format v1.");
            }

            ReadPropertyName("sourcePath", false);
            string sourcePath = ReadString();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw CreateFormatException("'sourcePath' must not be empty.");
            }

            if (!IsNormalizedSourcePath(sourcePath))
            {
                throw CreateFormatException(
                    "'sourcePath' must be a normalized project-relative or absolute path using '/' separators.");
            }

            ReadPropertyName("root", false);
            SpecNode root = ReadNode(0, string.Empty);
            ReadEndObject();
            return (sourcePath, root);
        }

        private SpecNode ReadNode(int parentContainerDepth, string logicalPath)
        {
            ReadStartObject();
            ReadPropertyName("kind", true);
            string kind = ReadString();
            ReadPropertyName("line", false);
            int line = ReadPositiveInteger("line");
            ReadPropertyName("column", false);
            int column = ReadPositiveInteger("column");

            SpecNode node;
            switch (kind)
            {
                case "object":
                    int objectDepth = GetContainerDepth(parentContainerDepth);
                    ReadPropertyName("properties", false);
                    IReadOnlyList<SpecProperty> properties = ReadProperties(objectDepth, logicalPath);
                    node = SpecNode.CreateObject(line, column, logicalPath, properties);
                    break;
                case "array":
                    int arrayDepth = GetContainerDepth(parentContainerDepth);
                    ReadPropertyName("items", false);
                    IReadOnlyList<SpecNode> items = ReadItems(arrayDepth, logicalPath);
                    node = SpecNode.CreateArray(line, column, logicalPath, items);
                    break;
                case "string":
                    ReadPropertyName("value", false);
                    node = SpecNode.CreateString(line, column, logicalPath, ReadString());
                    break;
                case "number":
                    ReadPropertyName("numberKind", false);
                    string numberKind = ReadString();
                    if (!string.Equals(numberKind, "integer", StringComparison.Ordinal) &&
                        !string.Equals(numberKind, "real", StringComparison.Ordinal))
                    {
                        throw CreateFormatException("'numberKind' must be 'integer' or 'real'.");
                    }

                    ReadPropertyName("value", false);
                    string numberValue = ReadString();
                    if (!TryValidateNumberLexeme(numberValue, out bool isInteger))
                    {
                        throw CreateFormatException("A number node contains an invalid RFC 8259 number lexeme.");
                    }

                    if (isInteger != string.Equals(numberKind, "integer", StringComparison.Ordinal))
                    {
                        throw CreateFormatException("'numberKind' does not match the number lexeme.");
                    }

                    node = SpecNode.CreateNumber(line, column, logicalPath, isInteger, numberValue);
                    break;
                case "boolean":
                    ReadPropertyName("value", false);
                    node = SpecNode.CreateBoolean(line, column, logicalPath, ReadBoolean());
                    break;
                case "null":
                    node = SpecNode.CreateNull(line, column, logicalPath);
                    break;
                default:
                    throw CreateFormatException("Unknown normalized node kind '" + kind + "'.");
            }

            ReadEndObject();
            return node;
        }

        private IReadOnlyList<SpecProperty> ReadProperties(int containerDepth, string logicalPath)
        {
            var properties = new List<SpecProperty>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            ReadStartArray();
            if (TryConsume(']'))
            {
                return properties;
            }

            while (true)
            {
                ReadStartObject();
                ReadPropertyName("name", true);
                string name = ReadString();
                ReadPropertyName("line", false);
                int line = ReadPositiveInteger("line");
                ReadPropertyName("column", false);
                int column = ReadPositiveInteger("column");
                ReadPropertyName("value", false);
                string childPath = AppendPointer(logicalPath, name);
                SpecNode value = ReadNode(containerDepth, childPath);
                ReadEndObject();

                if (!names.Add(name))
                {
                    throw CreateFormatException("Duplicate normalized object property '" + name + "'.");
                }

                properties.Add(new SpecProperty(name, line, column, value));
                if (TryConsume(']'))
                {
                    return properties;
                }

                Expect(',');
                if (Peek(']'))
                {
                    throw CreateFormatException("Trailing commas are not allowed.");
                }
            }
        }

        private IReadOnlyList<SpecNode> ReadItems(int containerDepth, string logicalPath)
        {
            var items = new List<SpecNode>();
            ReadStartArray();
            if (TryConsume(']'))
            {
                return items;
            }

            while (true)
            {
                string childPath = AppendPointer(
                    logicalPath,
                    items.Count.ToString(CultureInfo.InvariantCulture));
                items.Add(ReadNode(containerDepth, childPath));
                if (TryConsume(']'))
                {
                    return items;
                }

                Expect(',');
                if (Peek(']'))
                {
                    throw CreateFormatException("Trailing commas are not allowed.");
                }
            }
        }

        private int ReadPositiveInteger(string fieldName)
        {
            string lexeme = ReadNumberLexeme();
            if (!IsIntegerLexeme(lexeme) ||
                !int.TryParse(lexeme, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
                value < 1)
            {
                throw CreateFormatException("'" + fieldName + "' must be a positive JSON integer.");
            }

            return value;
        }

        private void ReadPropertyName(string expected, bool isFirst)
        {
            if (!isFirst)
            {
                Expect(',');
            }

            string actual = ReadString();
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw CreateFormatException(
                    "Expected field '" + expected + "' but found '" + actual + "'. Field order is fixed in format v1.");
            }

            Expect(':');
        }

        private void ReadStartObject()
        {
            Expect('{');
        }

        private void ReadEndObject()
        {
            Expect('}');
        }

        private void ReadStartArray()
        {
            Expect('[');
        }

        private bool ReadBoolean()
        {
            SkipWhitespace();
            if (TryConsumeLiteral("true"))
            {
                return true;
            }

            if (TryConsumeLiteral("false"))
            {
                return false;
            }

            throw CreateFormatException("A JSON boolean was expected.");
        }

        private string ReadString()
        {
            SkipWhitespace();
            if (IsAtEnd || Current != '"')
            {
                throw CreateFormatException("A JSON string was expected.");
            }

            ConsumeCharacter();
            var builder = new StringBuilder();
            while (!IsAtEnd)
            {
                char character = Current;
                if (character == '"')
                {
                    ConsumeCharacter();
                    return builder.ToString();
                }

                if (character < 0x20)
                {
                    throw CreateFormatException("Unescaped control characters are not allowed in JSON strings.");
                }

                if (character != '\\')
                {
                    builder.Append(character);
                    ConsumeCharacter();
                    continue;
                }

                ConsumeCharacter();
                if (IsAtEnd)
                {
                    throw CreateFormatException("The JSON escape sequence is incomplete.");
                }

                char escape = Current;
                ConsumeCharacter();
                switch (escape)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escape);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        builder.Append(ReadUnicodeCodeUnit());
                        break;
                    default:
                        throw CreateFormatException("The JSON string contains an invalid escape sequence.");
                }
            }

            throw CreateFormatException("The JSON string is not terminated.");
        }

        private char ReadUnicodeCodeUnit()
        {
            int value = 0;
            for (int offset = 0; offset < 4; offset++)
            {
                if (IsAtEnd)
                {
                    throw CreateFormatException("A Unicode escape must contain four hexadecimal digits.");
                }

                int digit = HexValue(Current);
                if (digit < 0)
                {
                    throw CreateFormatException("A Unicode escape must contain four hexadecimal digits.");
                }

                value = (value * 16) + digit;
                ConsumeCharacter();
            }

            return (char)value;
        }

        private string ReadNumberLexeme()
        {
            SkipWhitespace();
            int start = _index;
            if (TryConsumeWithoutWhitespace('-') && IsAtEnd)
            {
                throw CreateFormatException("A digit must follow the number sign.");
            }

            if (TryConsumeWithoutWhitespace('0'))
            {
                if (!IsAtEnd && IsDigit(Current))
                {
                    throw CreateFormatException("Leading zeroes are not allowed in JSON numbers.");
                }
            }
            else
            {
                if (IsAtEnd || Current < '1' || Current > '9')
                {
                    throw CreateFormatException("A JSON number was expected.");
                }

                ConsumeDigits();
            }

            if (TryConsumeWithoutWhitespace('.'))
            {
                if (IsAtEnd || !IsDigit(Current))
                {
                    throw CreateFormatException("A digit must follow the decimal point.");
                }

                ConsumeDigits();
            }

            if (!IsAtEnd && (Current == 'e' || Current == 'E'))
            {
                ConsumeCharacter();
                if (!IsAtEnd && (Current == '+' || Current == '-'))
                {
                    ConsumeCharacter();
                }

                if (IsAtEnd || !IsDigit(Current))
                {
                    throw CreateFormatException("A JSON exponent must contain at least one digit.");
                }

                ConsumeDigits();
            }

            return _content.Substring(start, _index - start);
        }

        private void ConsumeDigits()
        {
            while (!IsAtEnd && IsDigit(Current))
            {
                ConsumeCharacter();
            }
        }

        private bool TryConsumeLiteral(string literal)
        {
            if (_index + literal.Length > _content.Length ||
                !string.Equals(_content.Substring(_index, literal.Length), literal, StringComparison.Ordinal))
            {
                return false;
            }

            for (int index = 0; index < literal.Length; index++)
            {
                ConsumeCharacter();
            }

            return true;
        }

        private void Expect(char expected)
        {
            SkipWhitespace();
            if (IsAtEnd || Current != expected)
            {
                throw CreateFormatException("Expected '" + expected + "'.");
            }

            ConsumeCharacter();
        }

        private bool TryConsume(char expected)
        {
            SkipWhitespace();
            return TryConsumeWithoutWhitespace(expected);
        }

        private bool TryConsumeWithoutWhitespace(char expected)
        {
            if (IsAtEnd || Current != expected)
            {
                return false;
            }

            ConsumeCharacter();
            return true;
        }

        private bool Peek(char expected)
        {
            SkipWhitespace();
            return !IsAtEnd && Current == expected;
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

        private void ConsumeCharacter()
        {
            char character = _content[_index++];
            if (character == '\r')
            {
                _line++;
                _column = 1;
            }
            else if (character == '\n')
            {
                if (_index < 2 || _content[_index - 2] != '\r')
                {
                    _line++;
                }

                _column = 1;
            }
            else
            {
                _column++;
            }
        }

        private NormalizedSpecBundleFormatException CreateFormatException(string message)
        {
            return new NormalizedSpecBundleFormatException(message, _line, _column);
        }

        private static string AppendPointer(string path, string segment)
        {
            return path + "/" + segment.Replace("~", "~0").Replace("/", "~1");
        }

        private int GetContainerDepth(int parentContainerDepth)
        {
            int depth = parentContainerDepth + 1;
            if (depth > MaximumNodeDepth)
            {
                throw CreateFormatException("Normalized node depth exceeds the supported maximum of 256.");
            }

            return depth;
        }

        private static bool IsIntegerLexeme(string value)
        {
            return TryValidateNumberLexeme(value, out bool isInteger) && isInteger;
        }

        private static bool TryValidateNumberLexeme(string value, out bool isInteger)
        {
            isInteger = true;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            int index = 0;
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
                if (value[index] < '1' || value[index] > '9')
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

                if (index == fractionStart)
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

                if (index == exponentStart)
                {
                    return false;
                }
            }

            return index == value.Length;
        }

        private static bool IsLowerHex(string value, int requiredLength)
        {
            if (value.Length != requiredLength)
            {
                return false;
            }

            foreach (char character in value)
            {
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsNormalizedSourcePath(string value)
        {
            if (value.IndexOf('\\') >= 0 || value.EndsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            int segmentStart;
            bool isAbsolute;
            if (value.StartsWith("//", StringComparison.Ordinal))
            {
                isAbsolute = true;
                segmentStart = 2;
            }
            else if (value[0] == '/')
            {
                isAbsolute = true;
                segmentStart = 1;
            }
            else if (value.Length >= 3 &&
                     IsAsciiLetter(value[0]) &&
                     value[1] == ':' &&
                     value[2] == '/')
            {
                isAbsolute = true;
                segmentStart = 3;
            }
            else
            {
                isAbsolute = false;
                segmentStart = 0;
            }

            if (segmentStart >= value.Length ||
                (!isAbsolute && value.IndexOf(':') >= 0))
            {
                return false;
            }

            string[] segments = value.Substring(segmentStart).Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 ||
                    string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDigit(char character)
        {
            return character >= '0' && character <= '9';
        }

        private static bool IsAsciiLetter(char character)
        {
            return (character >= 'a' && character <= 'z') ||
                   (character >= 'A' && character <= 'Z');
        }

        private static int HexValue(char character)
        {
            if (character >= '0' && character <= '9')
            {
                return character - '0';
            }

            if (character >= 'a' && character <= 'f')
            {
                return character - 'a' + 10;
            }

            if (character >= 'A' && character <= 'F')
            {
                return character - 'A' + 10;
            }

            return -1;
        }
    }
}
