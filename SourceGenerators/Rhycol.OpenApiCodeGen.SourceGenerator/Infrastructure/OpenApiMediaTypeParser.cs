using System;
using System.Collections.Generic;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal static class OpenApiMediaTypeParser
    {
        internal static bool TryParse(
            string value,
            out ParsedOpenApiMediaType mediaType,
            out string error)
        {
            mediaType = default;
            error = string.Empty;
            if (value is null)
            {
                error = "The media type must not be null.";
                return false;
            }

            int index = 0;
            SkipOptionalWhitespace(value, ref index);
            if (!TryReadToken(value, ref index, out string type) ||
                index >= value.Length ||
                value[index] != '/')
            {
                error = "The media type must contain a valid type and subtype.";
                return false;
            }

            index++;
            if (!TryReadToken(value, ref index, out string subtype))
            {
                error = "The media type must contain a valid subtype.";
                return false;
            }

            var parameters = new List<ParsedOpenApiMediaTypeParameter>();
            var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SkipOptionalWhitespace(value, ref index);
            while (index < value.Length)
            {
                if (value[index] != ';')
                {
                    error = "Unexpected characters follow the media type subtype.";
                    return false;
                }

                index++;
                SkipOptionalWhitespace(value, ref index);
                if (!TryReadToken(value, ref index, out string name))
                {
                    error = "Each media type parameter must have a valid name.";
                    return false;
                }

                if (!parameterNames.Add(name))
                {
                    error = "The media type parameter '" + name + "' is declared more than once.";
                    return false;
                }

                SkipOptionalWhitespace(value, ref index);
                if (index >= value.Length || value[index] != '=')
                {
                    error = "The media type parameter '" + name + "' must have a value.";
                    return false;
                }

                index++;
                SkipOptionalWhitespace(value, ref index);
                if (!TryReadParameterValue(
                        value,
                        ref index,
                        out string normalizedValue,
                        out string unquotedValue))
                {
                    error = "The media type parameter '" + name + "' has an invalid value.";
                    return false;
                }

                parameters.Add(new ParsedOpenApiMediaTypeParameter(
                    name.ToLowerInvariant(),
                    normalizedValue,
                    unquotedValue));
                SkipOptionalWhitespace(value, ref index);
            }

            mediaType = new ParsedOpenApiMediaType(
                type.ToLowerInvariant(),
                subtype.ToLowerInvariant(),
                parameters);
            return true;
        }

        private static bool TryReadToken(string value, ref int index, out string token)
        {
            int start = index;
            while (index < value.Length && IsTokenCharacter(value[index]))
            {
                index++;
            }

            token = value.Substring(start, index - start);
            return token.Length > 0;
        }

        private static bool TryReadParameterValue(
            string value,
            ref int index,
            out string normalizedValue,
            out string unquotedValue)
        {
            normalizedValue = string.Empty;
            unquotedValue = string.Empty;
            if (index >= value.Length)
            {
                return false;
            }

            if (value[index] != '"')
            {
                if (!TryReadToken(value, ref index, out string token))
                {
                    return false;
                }

                normalizedValue = token;
                unquotedValue = token;
                return true;
            }

            var decoded = new StringBuilder();
            var encoded = new StringBuilder();
            encoded.Append('"');
            index++;
            while (index < value.Length)
            {
                char character = value[index++];
                if (character == '"')
                {
                    encoded.Append('"');
                    normalizedValue = encoded.ToString();
                    unquotedValue = decoded.ToString();
                    return true;
                }

                if (character == '\\')
                {
                    if (index >= value.Length || !IsQuotedCharacter(value[index]))
                    {
                        return false;
                    }

                    char escaped = value[index++];
                    encoded.Append('\\').Append(escaped);
                    decoded.Append(escaped);
                    continue;
                }

                if (!IsQuotedCharacter(character))
                {
                    return false;
                }

                encoded.Append(character);
                decoded.Append(character);
            }

            return false;
        }

        private static bool IsTokenCharacter(char character)
        {
            return (character >= '0' && character <= '9') ||
                   (character >= 'A' && character <= 'Z') ||
                   (character >= 'a' && character <= 'z') ||
                   character == '!' || character == '#' || character == '$' ||
                   character == '%' || character == '&' || character == '\'' ||
                   character == '*' || character == '+' || character == '-' ||
                   character == '.' || character == '^' || character == '_' ||
                   character == '`' || character == '|' || character == '~';
        }

        private static bool IsQuotedCharacter(char character)
        {
            return character == '\t' ||
                   (character >= ' ' && character != (char)127);
        }

        private static void SkipOptionalWhitespace(string value, ref int index)
        {
            while (index < value.Length && (value[index] == ' ' || value[index] == '\t'))
            {
                index++;
            }
        }
    }

    internal readonly struct ParsedOpenApiMediaType
    {
        private readonly IReadOnlyList<ParsedOpenApiMediaTypeParameter> _parameters;

        internal ParsedOpenApiMediaType(
            string type,
            string subtype,
            IReadOnlyList<ParsedOpenApiMediaTypeParameter> parameters)
        {
            Type = type;
            Subtype = subtype;
            _parameters = parameters;
        }

        internal string Type { get; }

        internal string Subtype { get; }

        internal bool IsJson =>
            string.Equals(Type, "application", StringComparison.Ordinal) &&
            (string.Equals(Subtype, "json", StringComparison.Ordinal) ||
             Subtype.EndsWith("+json", StringComparison.Ordinal));

        internal string? Charset
        {
            get
            {
                foreach (ParsedOpenApiMediaTypeParameter parameter in _parameters)
                {
                    if (string.Equals(parameter.Name, "charset", StringComparison.OrdinalIgnoreCase))
                    {
                        return parameter.UnquotedValue;
                    }
                }

                return null;
            }
        }

        internal string ToNormalizedString(string? normalizedCharset = null)
        {
            var builder = new StringBuilder();
            builder.Append(Type).Append('/').Append(Subtype);
            foreach (ParsedOpenApiMediaTypeParameter parameter in _parameters)
            {
                builder.Append("; ").Append(parameter.Name).Append('=');
                builder.Append(
                    normalizedCharset is not null &&
                    string.Equals(parameter.Name, "charset", StringComparison.OrdinalIgnoreCase)
                        ? normalizedCharset
                        : parameter.NormalizedValue);
            }

            return builder.ToString();
        }
    }

    internal readonly struct ParsedOpenApiMediaTypeParameter
    {
        internal ParsedOpenApiMediaTypeParameter(
            string name,
            string normalizedValue,
            string unquotedValue)
        {
            Name = name;
            NormalizedValue = normalizedValue;
            UnquotedValue = unquotedValue;
        }

        internal string Name { get; }

        internal string NormalizedValue { get; }

        internal string UnquotedValue { get; }
    }
}
