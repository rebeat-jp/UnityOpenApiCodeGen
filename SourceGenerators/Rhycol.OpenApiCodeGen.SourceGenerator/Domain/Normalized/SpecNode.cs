using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class SpecNode
    {
        private readonly IReadOnlyList<SpecProperty> _properties;
        private readonly IReadOnlyList<SpecNode> _items;
        private readonly string? _textValue;
        private readonly bool _booleanValue;

        private SpecNode(
            SpecValueKind valueKind,
            int line,
            int column,
            string logicalPath,
            IReadOnlyList<SpecProperty>? properties = null,
            IReadOnlyList<SpecNode>? items = null,
            string? textValue = null,
            bool booleanValue = false,
            bool isInteger = false)
        {
            ValueKind = valueKind;
            Line = line;
            Column = column;
            LogicalPath = logicalPath;
            _properties = properties ?? Array.Empty<SpecProperty>();
            _items = items ?? Array.Empty<SpecNode>();
            _textValue = textValue;
            _booleanValue = booleanValue;
            IsInteger = isInteger;
        }

        internal SpecValueKind ValueKind { get; }

        internal int Line { get; }

        internal int Column { get; }

        internal string LogicalPath { get; }

        internal bool IsInteger { get; }

        internal SpecNode Value => this;

        internal static SpecNode CreateObject(
            int line,
            int column,
            string logicalPath,
            IReadOnlyList<SpecProperty> properties)
        {
            return new SpecNode(SpecValueKind.Object, line, column, logicalPath, properties: properties);
        }

        internal static SpecNode CreateArray(
            int line,
            int column,
            string logicalPath,
            IReadOnlyList<SpecNode> items)
        {
            return new SpecNode(SpecValueKind.Array, line, column, logicalPath, items: items);
        }

        internal static SpecNode CreateString(int line, int column, string logicalPath, string value)
        {
            return new SpecNode(SpecValueKind.String, line, column, logicalPath, textValue: value);
        }

        internal static SpecNode CreateNumber(
            int line,
            int column,
            string logicalPath,
            bool isInteger,
            string value)
        {
            return new SpecNode(
                SpecValueKind.Number,
                line,
                column,
                logicalPath,
                textValue: value,
                isInteger: isInteger);
        }

        internal static SpecNode CreateBoolean(int line, int column, string logicalPath, bool value)
        {
            return new SpecNode(
                value ? SpecValueKind.True : SpecValueKind.False,
                line,
                column,
                logicalPath,
                booleanValue: value);
        }

        internal static SpecNode CreateNull(int line, int column, string logicalPath)
        {
            return new SpecNode(SpecValueKind.Null, line, column, logicalPath);
        }

        internal IEnumerable<SpecProperty> EnumerateObject()
        {
            return _properties;
        }

        internal IEnumerable<SpecNode> EnumerateArray()
        {
            return _items;
        }

        internal bool TryGetProperty(string name, out SpecNode value)
        {
            foreach (SpecProperty property in _properties)
            {
                if (string.Equals(property.Name, name, StringComparison.Ordinal))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = this;
            return false;
        }

        internal string? GetString()
        {
            return ValueKind == SpecValueKind.String ? _textValue : null;
        }

        internal bool TryGetDouble(out double value)
        {
            value = default;
            if (ValueKind != SpecValueKind.Number ||
                !double.TryParse(_textValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsInfinity(value) ||
                double.IsNaN(value))
            {
                value = default;
                return false;
            }

            return true;
        }

        internal bool TryGetInt64(out long value)
        {
            value = default;
            return ValueKind == SpecValueKind.Number &&
                   IsInteger &&
                   long.TryParse(_textValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        internal string GetRawJson()
        {
            var builder = new StringBuilder();
            AppendRawJson(builder);
            return builder.ToString();
        }

        private void AppendRawJson(StringBuilder builder)
        {
            switch (ValueKind)
            {
                case SpecValueKind.Object:
                    builder.Append('{');
                    for (int index = 0; index < _properties.Count; index++)
                    {
                        if (index > 0)
                        {
                            builder.Append(',');
                        }

                        AppendJsonString(builder, _properties[index].Name);
                        builder.Append(':');
                        _properties[index].Value.AppendRawJson(builder);
                    }

                    builder.Append('}');
                    break;
                case SpecValueKind.Array:
                    builder.Append('[');
                    for (int index = 0; index < _items.Count; index++)
                    {
                        if (index > 0)
                        {
                            builder.Append(',');
                        }

                        _items[index].AppendRawJson(builder);
                    }

                    builder.Append(']');
                    break;
                case SpecValueKind.String:
                    AppendJsonString(builder, _textValue ?? string.Empty);
                    break;
                case SpecValueKind.Number:
                    builder.Append(_textValue);
                    break;
                case SpecValueKind.True:
                    builder.Append("true");
                    break;
                case SpecValueKind.False:
                    builder.Append("false");
                    break;
                case SpecValueKind.Null:
                    builder.Append("null");
                    break;
                default:
                    throw new InvalidOperationException("Unknown normalized node kind.");
            }
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20 ||
                            (char.IsSurrogate(character) &&
                             !(char.IsHighSurrogate(character) &&
                               index + 1 < value.Length &&
                               char.IsLowSurrogate(value[index + 1]))))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                            if (char.IsHighSurrogate(character))
                            {
                                builder.Append(value[++index]);
                            }
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}
