namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    // The members of the generated code that depend on the JSON library (the token type and how its kinds,
    // objects and arrays are read), and the format checks that work on a string.
    internal sealed partial class AssertionEmitter
    {
        private string ObjectProperties(string value)
        {
            return _json.IsSystemTextJson
                ? value + ".EnumerateObject()"
                : "((global::Newtonsoft.Json.Linq.JObject)" + value + ").Properties()";
        }

        private string ArrayItems(string value)
        {
            return _json.IsSystemTextJson
                ? value + ".EnumerateArray()"
                : "((global::Newtonsoft.Json.Linq.JArray)" + value + ")";
        }

        // A string token for propertyNames.
        private string NameToken(string name)
        {
            return _json.IsSystemTextJson
                ? "global::System.Text.Json.JsonSerializer.SerializeToElement(" + name + ")"
                : "new global::Newtonsoft.Json.Linq.JValue(" + name + ")";
        }

        private const string NewtonsoftHelperSource = @"
        private struct __P__AFailure
        {
            internal string? Keyword;
            internal string? Detail;
            internal string? Path;
            internal global::System.Exception? Inner;
        }

        private static bool __P__AFail(ref __P__AFailure failure, string keyword, string detail)
        {
            return __P__AFailWith(ref failure, keyword, detail, null);
        }

        private static bool __P__AFailWith(ref __P__AFailure failure, string keyword, string detail, global::System.Exception? inner)
        {
            failure.Keyword = keyword;
            failure.Detail = detail;
            failure.Path = string.Empty;
            failure.Inner = inner;
            return false;
        }

        // A child failed: the path of the member is put in front of the path that the child recorded.
        private static bool __P__AAt(ref __P__AFailure failure, string segment)
        {
            failure.Path = segment + (failure.Path ?? string.Empty);
            return false;
        }

        private static void __P__AThrow(string contract, string path, string? parameterName, ref __P__AFailure failure)
        {
            string message = ""The "" + contract + "" value at "" + path + "" violates "" + failure.Keyword + "": "" + failure.Detail + ""."";
            // Only a match timeout is kept as the inner exception (a nested failure may contain data of the value).
            global::System.Exception? inner = failure.Inner as global::System.Text.RegularExpressions.RegexMatchTimeoutException;
            if (contract == ""request"")
            {
                throw new global::System.ArgumentException(message, parameterName, inner);
            }
            throw inner == null ? new global::Newtonsoft.Json.JsonSerializationException(message) : new global::Newtonsoft.Json.JsonSerializationException(message, inner);
        }

        private static global::Newtonsoft.Json.Linq.JToken __P__AParse(string json)
        {
            using (var reader = new global::Newtonsoft.Json.JsonTextReader(new global::System.IO.StringReader(json)) { DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None })
            {
                return global::Newtonsoft.Json.Linq.JToken.ReadFrom(reader);
            }
        }

        private static bool __P__AIsNull(global::Newtonsoft.Json.Linq.JToken token)
        {
            return token.Type == global::Newtonsoft.Json.Linq.JTokenType.Null || token.Type == global::Newtonsoft.Json.Linq.JTokenType.Undefined;
        }

        private static bool __P__AIsBoolean(global::Newtonsoft.Json.Linq.JToken token)
        {
            return token.Type == global::Newtonsoft.Json.Linq.JTokenType.Boolean;
        }

        private static bool __P__AIsString(global::Newtonsoft.Json.Linq.JToken token)
        {
            switch (token.Type)
            {
                case global::Newtonsoft.Json.Linq.JTokenType.String:
                case global::Newtonsoft.Json.Linq.JTokenType.Guid:
                case global::Newtonsoft.Json.Linq.JTokenType.Uri:
                case global::Newtonsoft.Json.Linq.JTokenType.Bytes:
                case global::Newtonsoft.Json.Linq.JTokenType.Date:
                case global::Newtonsoft.Json.Linq.JTokenType.TimeSpan:
                    return true;
                default:
                    return false;
            }
        }

        private static bool __P__AIsArray(global::Newtonsoft.Json.Linq.JToken token)
        {
            return token.Type == global::Newtonsoft.Json.Linq.JTokenType.Array;
        }

        private static bool __P__AIsObject(global::Newtonsoft.Json.Linq.JToken token)
        {
            return token.Type == global::Newtonsoft.Json.Linq.JTokenType.Object;
        }

        // A number is an integer when its value is integral (1.0 and 1e2 are).
        private static bool __P__AIsInteger(global::Newtonsoft.Json.Linq.JToken token)
        {
            if (token.Type == global::Newtonsoft.Json.Linq.JTokenType.Integer)
            {
                return true;
            }
            if (token.Type != global::Newtonsoft.Json.Linq.JTokenType.Float)
            {
                return false;
            }
            if (__P__TryDecimal(token, out decimal number))
            {
                return number == decimal.Truncate(number);
            }
            double value = __P__ToDouble(token);
            return !double.IsNaN(value) && !double.IsInfinity(value) && global::System.Math.Floor(value) == value;
        }

        // format: int32 / int64 bound the integral numbers (a value that is no integer is left to the type check).
        private static bool __P__AIntegerRange(global::Newtonsoft.Json.Linq.JToken token, bool int32)
        {
            if (!__P__IsNumber(token) || !__P__AIsInteger(token))
            {
                return true;
            }
            if (!__P__TryDecimal(token, out decimal number))
            {
                return false;
            }
            return int32 ? (number >= int.MinValue && number <= int.MaxValue) : (number >= long.MinValue && number <= long.MaxValue);
        }

        private static bool __P__AHas(global::Newtonsoft.Json.Linq.JToken obj, string name)
        {
            return ((global::Newtonsoft.Json.Linq.JObject)obj).Property(name) != null;
        }

        private static bool __P__AGet(global::Newtonsoft.Json.Linq.JToken obj, string name, out global::Newtonsoft.Json.Linq.JToken child)
        {
            bool found = ((global::Newtonsoft.Json.Linq.JObject)obj).TryGetValue(name, out global::Newtonsoft.Json.Linq.JToken? member);
            child = member!;
            return found;
        }

        private static bool __P__AIn(global::Newtonsoft.Json.Linq.JToken token, global::Newtonsoft.Json.Linq.JToken[] values)
        {
            foreach (var value in values)
            {
                if (__P__Equal(token, value))
                {
                    return true;
                }
            }
            return false;
        }

        // A match timeout is a failure of the keyword (the only exception the assertions catch).
        private static bool __P__AMatches(global::System.Text.RegularExpressions.Regex regex, string input, string keyword, string pattern, ref __P__AFailure failure, out bool matched)
        {
            try
            {
                matched = regex.IsMatch(input);
                return true;
            }
            catch (global::System.Text.RegularExpressions.RegexMatchTimeoutException exception)
            {
                matched = false;
                return __P__AFailWith(ref failure, keyword, ""matching the pattern '"" + pattern + ""' timed out"", exception);
            }
        }
";

        private const string SystemTextJsonHelperSource = @"
        private struct __P__AFailure
        {
            internal string? Keyword;
            internal string? Detail;
            internal string? Path;
            internal global::System.Exception? Inner;
        }

        private static bool __P__AFail(ref __P__AFailure failure, string keyword, string detail)
        {
            return __P__AFailWith(ref failure, keyword, detail, null);
        }

        private static bool __P__AFailWith(ref __P__AFailure failure, string keyword, string detail, global::System.Exception? inner)
        {
            failure.Keyword = keyword;
            failure.Detail = detail;
            failure.Path = string.Empty;
            failure.Inner = inner;
            return false;
        }

        // A child failed: the path of the member is put in front of the path that the child recorded.
        private static bool __P__AAt(ref __P__AFailure failure, string segment)
        {
            failure.Path = segment + (failure.Path ?? string.Empty);
            return false;
        }

        private static void __P__AThrow(string contract, string path, string? parameterName, ref __P__AFailure failure)
        {
            string message = ""The "" + contract + "" value at "" + path + "" violates "" + failure.Keyword + "": "" + failure.Detail + ""."";
            // Only a match timeout is kept as the inner exception (a nested failure may contain data of the value).
            global::System.Exception? inner = failure.Inner as global::System.Text.RegularExpressions.RegexMatchTimeoutException;
            if (contract == ""request"")
            {
                throw new global::System.ArgumentException(message, parameterName, inner);
            }
            throw inner == null ? new global::System.Text.Json.JsonException(message) : new global::System.Text.Json.JsonException(message, inner);
        }

        private static global::System.Text.Json.JsonElement __P__AParse(string json)
        {
            using (var document = global::System.Text.Json.JsonDocument.Parse(json))
            {
                return document.RootElement.Clone();
            }
        }

        private static bool __P__AIsNull(global::System.Text.Json.JsonElement token)
        {
            return token.ValueKind == global::System.Text.Json.JsonValueKind.Null || token.ValueKind == global::System.Text.Json.JsonValueKind.Undefined;
        }

        private static bool __P__AIsBoolean(global::System.Text.Json.JsonElement token)
        {
            return token.ValueKind == global::System.Text.Json.JsonValueKind.True || token.ValueKind == global::System.Text.Json.JsonValueKind.False;
        }

        private static bool __P__AIsString(global::System.Text.Json.JsonElement token)
        {
            return token.ValueKind == global::System.Text.Json.JsonValueKind.String;
        }

        private static bool __P__AIsArray(global::System.Text.Json.JsonElement token)
        {
            return token.ValueKind == global::System.Text.Json.JsonValueKind.Array;
        }

        private static bool __P__AIsObject(global::System.Text.Json.JsonElement token)
        {
            return token.ValueKind == global::System.Text.Json.JsonValueKind.Object;
        }

        // A number is an integer when its value is integral (1.0 and 1e2 are).
        private static bool __P__AIsInteger(global::System.Text.Json.JsonElement token)
        {
            if (token.ValueKind != global::System.Text.Json.JsonValueKind.Number)
            {
                return false;
            }
            if (token.TryGetInt64(out _))
            {
                return true;
            }
            if (__P__TryDecimal(token, out decimal number))
            {
                return number == decimal.Truncate(number);
            }
            return token.TryGetDouble(out double value) && !double.IsNaN(value) && !double.IsInfinity(value) && global::System.Math.Floor(value) == value;
        }

        // format: int32 / int64 bound the integral numbers (a value that is no integer is left to the type check).
        private static bool __P__AIntegerRange(global::System.Text.Json.JsonElement token, bool int32)
        {
            if (!__P__IsNumber(token) || !__P__AIsInteger(token))
            {
                return true;
            }
            if (!__P__TryDecimal(token, out decimal number))
            {
                return false;
            }
            return int32 ? (number >= int.MinValue && number <= int.MaxValue) : (number >= long.MinValue && number <= long.MaxValue);
        }

        private static bool __P__AHas(global::System.Text.Json.JsonElement obj, string name)
        {
            return obj.TryGetProperty(name, out _);
        }

        private static bool __P__AGet(global::System.Text.Json.JsonElement obj, string name, out global::System.Text.Json.JsonElement child)
        {
            return obj.TryGetProperty(name, out child);
        }

        private static bool __P__AIn(global::System.Text.Json.JsonElement token, global::System.Text.Json.JsonElement[] values)
        {
            foreach (var value in values)
            {
                if (__P__Equal(token, value))
                {
                    return true;
                }
            }
            return false;
        }

        // A match timeout is a failure of the keyword (the only exception the assertions catch).
        private static bool __P__AMatches(global::System.Text.RegularExpressions.Regex regex, string input, string keyword, string pattern, ref __P__AFailure failure, out bool matched)
        {
            try
            {
                matched = regex.IsMatch(input);
                return true;
            }
            catch (global::System.Text.RegularExpressions.RegexMatchTimeoutException exception)
            {
                matched = false;
                return __P__AFailWith(ref failure, keyword, ""matching the pattern '"" + pattern + ""' timed out"", exception);
            }
        }
";

        // The format checks of the typed contracts (date, date-time, uuid) and base64 (byte).
        private const string FormatHelperSource = @"
        private static bool __P__AIsAsciiDigit(char value) => value >= '0' && value <= '9';

        private static bool __P__AIsTwoDigits(string value, int start) =>
            __P__AIsAsciiDigit(value[start]) && __P__AIsAsciiDigit(value[start + 1]);

        private static int __P__ATwoDigits(string value, int start) =>
            (value[start] - '0') * 10 + value[start + 1] - '0';

        private static bool __P__AIsFullDate(string value)
        {
            if (value.Length != 10 || value[4] != '-' || value[7] != '-' ||
                !__P__AIsTwoDigits(value, 0) || !__P__AIsTwoDigits(value, 2) ||
                !__P__AIsTwoDigits(value, 5) || !__P__AIsTwoDigits(value, 8)) return false;
            return global::System.DateTime.TryParseExact(value, ""yyyy-MM-dd"",
                global::System.Globalization.CultureInfo.InvariantCulture,
                global::System.Globalization.DateTimeStyles.None, out _);
        }

        private static bool __P__AIsDateTime(string value)
        {
            if (value.Length < 20 || !__P__AIsFullDate(value.Substring(0, 10)) ||
                (value[10] != 'T' && value[10] != 't') ||
                value[13] != ':' || value[16] != ':' ||
                !__P__AIsTwoDigits(value, 11) || !__P__AIsTwoDigits(value, 14) ||
                !__P__AIsTwoDigits(value, 17) ||
                __P__ATwoDigits(value, 11) > 23 ||
                __P__ATwoDigits(value, 14) > 59 ||
                __P__ATwoDigits(value, 17) > 60) return false;
            int index = 19;
            if (value[index] == '.')
            {
                index++;
                int fractionalStart = index;
                while (index < value.Length && __P__AIsAsciiDigit(value[index])) index++;
                if (index == fractionalStart) return false;
            }
            if (index == value.Length - 1 && (value[index] == 'Z' || value[index] == 'z')) return true;
            return index == value.Length - 6 && (value[index] == '+' || value[index] == '-') &&
                   value[index + 3] == ':' && __P__AIsTwoDigits(value, index + 1) &&
                   __P__AIsTwoDigits(value, index + 4) &&
                   __P__ATwoDigits(value, index + 1) <= 23 &&
                   __P__ATwoDigits(value, index + 4) <= 59;
        }

        private static bool __P__AIsUuid(string value)
        {
            if (value.Length != 36) return false;
            for (int index = 0; index < value.Length; index++)
            {
                if (index == 8 || index == 13 || index == 18 || index == 23)
                {
                    if (value[index] != '-') return false;
                }
                else if (!((value[index] >= '0' && value[index] <= '9') ||
                           (value[index] >= 'a' && value[index] <= 'f') ||
                           (value[index] >= 'A' && value[index] <= 'F'))) return false;
            }
            return true;
        }

        // format: byte is base64 (Json.NET reads and writes byte[] as base64).
        private static bool __P__AIsBase64(string value)
        {
            if (value.Length % 4 != 0) return false;
            int end = value.Length;
            if (end > 0 && value[end - 1] == '=') end--;
            if (end > 0 && value[end - 1] == '=') end--;
            for (int index = 0; index < end; index++)
            {
                char character = value[index];
                if (!((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||
                      (character >= '0' && character <= '9') || character == '+' || character == '/')) return false;
            }
            return true;
        }
";
    }
}
