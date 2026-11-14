using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// 実行時 constraint 検証の System.Text.Json 版。キーワード・メッセージ・数値規則は Newtonsoft.Json 版と同じで、
    /// JToken の代わりに JsonElement を扱う。型付き値（リクエスト、パラメータ、ヘッダー）は client の共有 options で
    /// JsonElement にし、レスポンス本文は契約メソッドが JsonDocument として検査する。decimal 型の値は正確な字句で比較する。
    /// The System.Text.Json flavour of the run-time constraint checks: the same keywords, messages and
    /// numeric rules as the Newtonsoft.Json output, working on a JsonElement instead of a JToken.
    /// A typed value (request, parameter, header) is turned into a JsonElement with the shared options of
    /// the client; a response body is checked as a parsed JsonDocument by its contract methods.
    /// </summary>
    internal sealed partial class ConstraintValidationEmitter
    {
        private void AppendSystemTextJsonValidator(StringBuilder source, GeneratedTypeModel type, int index)
        {
            source.Append("        private static void ").Append(_prefix).Append("Validate")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .AppendLine("(global::System.Text.Json.JsonElement token, string path, string contract)");
            source.AppendLine("        {");
            source.AppendLine("            if (token.ValueKind == global::System.Text.Json.JsonValueKind.Null || token.ValueKind == global::System.Text.Json.JsonValueKind.Undefined)");
            source.AppendLine("            {");
            source.AppendLine("                return;");
            source.AppendLine("            }");

            GeneratedDtoModel? dto = null;
            IReadOnlyList<GeneratedConstraints> own = type.ConstraintSets;
            if (type.Kind == GeneratedTypeKind.Named && _dtos.TryGetValue(type.Name, out GeneratedDtoModel? found))
            {
                dto = found;
                own = type.ConstraintSets.Concat(dto.ConstraintSets).ToArray();
            }

            // A decimal (or a list of decimals) is checked with its exact text.
            bool exact = own.Count > 0 &&
                         (type.Kind == GeneratedTypeKind.Decimal ||
                          (type.Kind == GeneratedTypeKind.Array && type.ItemType!.Kind == GeneratedTypeKind.Decimal));
            if (exact)
            {
                source.AppendLine("            bool exactBefore = " + _prefix + "Exact;");
                source.AppendLine("            " + _prefix + "Exact = true;");
                source.AppendLine("            try");
                source.AppendLine("            {");
            }

            AppendOwnChecks(source, exact ? "                " : "            ", own, "token", "path", "contract", contractScope: false);
            if (exact)
            {
                source.AppendLine("            }");
                source.AppendLine("            finally");
                source.AppendLine("            {");
                source.AppendLine("                " + _prefix + "Exact = exactBefore;");
                source.AppendLine("            }");
            }
            switch (type.Kind)
            {
                case GeneratedTypeKind.Array:
                    if (NeedsValidation(type.ItemType!))
                    {
                        source.AppendLine("            if (token.ValueKind == global::System.Text.Json.JsonValueKind.Array)");
                        source.AppendLine("            {");
                        source.AppendLine("                int index = 0;");
                        source.AppendLine("                foreach (var item in token.EnumerateArray())");
                        source.AppendLine("                {");
                        source.Append("                    ").Append(GetValidatorName(type.ItemType!))
                            .AppendLine("(item, path + \"[\" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\", contract);");
                        source.AppendLine("                    index++;");
                        source.AppendLine("                }");
                        source.AppendLine("            }");
                    }

                    break;
                case GeneratedTypeKind.Map:
                    if (NeedsValidation(type.ValueType!))
                    {
                        source.AppendLine("            if (token.ValueKind == global::System.Text.Json.JsonValueKind.Object)");
                        source.AppendLine("            {");
                        source.AppendLine("                foreach (var property in token.EnumerateObject())");
                        source.AppendLine("                {");
                        source.Append("                    ").Append(GetValidatorName(type.ValueType!))
                            .AppendLine("(property.Value, path + \".\" + property.Name, contract);");
                        source.AppendLine("                }");
                        source.AppendLine("            }");
                    }

                    break;
                case GeneratedTypeKind.Named:
                    if (dto is not null)
                    {
                        AppendSystemTextJsonDtoDescent(source, dto);
                    }

                    break;
            }

            source.AppendLine("        }");
        }

        private void AppendSystemTextJsonDtoDescent(StringBuilder source, GeneratedDtoModel dto)
        {
            GeneratedDtoPropertyModel[] members = dto.Properties
                .Where(property => !property.Annotations.ReadOnly && NeedsValidation(property.Type))
                .ToArray();
            bool extension = dto.ExtensionDataValueType is not null && NeedsValidation(dto.ExtensionDataValueType);
            if (members.Length == 0 && !extension)
            {
                return;
            }

            source.AppendLine("            if (token.ValueKind == global::System.Text.Json.JsonValueKind.Object)");
            source.AppendLine("            {");
            foreach (GeneratedDtoPropertyModel member in members)
            {
                string wire = GeneratedSourceEmitter.StringLiteral(member.WireName);
                source.AppendLine("                {");
                source.Append("                    if (token.TryGetProperty(").Append(wire).AppendLine(", out var memberToken))");
                source.AppendLine("                    {");
                source.Append("                        ").Append(GetValidatorName(member.Type))
                    .Append("(memberToken, path + \".\" + ").Append(wire).AppendLine(", contract);");
                source.AppendLine("                    }");
                source.AppendLine("                }");
            }

            if (extension)
            {
                source.AppendLine("                foreach (var property in token.EnumerateObject())");
                source.AppendLine("                {");
                source.AppendLine("                    switch (property.Name)");
                source.AppendLine("                    {");
                foreach (GeneratedDtoPropertyModel declared in dto.Properties)
                {
                    source.Append("                        case ")
                        .Append(GeneratedSourceEmitter.StringLiteral(declared.WireName)).AppendLine(":");
                }

                if (dto.Properties.Count > 0)
                {
                    source.AppendLine("                            break;");
                }

                source.AppendLine("                        default:");
                source.Append("                            ").Append(GetValidatorName(dto.ExtensionDataValueType!))
                    .AppendLine("(property.Value, path + \".\" + property.Name, contract);");
                source.AppendLine("                            break;");
                source.AppendLine("                    }");
                source.AppendLine("                }");
            }

            source.AppendLine("            }");
        }

        // The numbers follow the Newtonsoft.Json rules: an integer lexeme is exact (decimal when it fits), a
        // number written with a fraction or an exponent is a double, rounded to 15 significant digits.
        private const string SystemTextJsonHelperSource = @"
        private static void __P__Fail(string contract, string path, string keyword, string detail, global::System.Exception? inner = null)
        {
            string message = ""The "" + contract + "" value at "" + path + "" violates "" + keyword + "": "" + detail + ""."";
            // A nested failure may contain data of the value, so only a match timeout is kept as the inner exception.
            if (!(inner is global::System.Text.RegularExpressions.RegexMatchTimeoutException))
            {
                inner = null;
            }
            if (contract == ""request"")
            {
                throw new global::System.ArgumentException(message, inner);
            }
            throw inner == null ? new global::System.Text.Json.JsonException(message) : new global::System.Text.Json.JsonException(message, inner);
        }

        private static bool __P__IsTrial(global::System.Exception exception)
        {
            return exception is global::System.Text.Json.JsonException ||
                   exception is global::System.FormatException ||
                   exception is global::System.InvalidCastException ||
                   exception is global::System.OverflowException ||
                   exception is global::System.ArgumentException;
        }

        private static bool __P__Matches(global::System.Action<global::System.Text.Json.JsonElement, string> validator, global::System.Text.Json.JsonElement token, string path, out global::System.Exception? failure)
        {
            try
            {
                validator(token, path);
                failure = null;
                return true;
            }
            catch (global::System.Exception exception) when (__P__IsTrial(exception))
            {
                failure = exception;
                return false;
            }
        }

        // The typed value is serialized with the options of the client, like the request body.
        private static void __P__Check(global::System.Action<global::System.Text.Json.JsonElement, string, string> validator, object? value, string path, string contract, string? parameterName)
        {
            if (value == null)
            {
                return;
            }
            global::System.Text.Json.JsonElement token = value is global::System.Text.Json.JsonElement existing
                ? existing
                : global::System.Text.Json.JsonSerializer.SerializeToElement(value, value.GetType(), __OPTIONS__);
            try
            {
                validator(token, path, contract);
            }
            catch (global::System.ArgumentException exception) when (contract == ""request"" && exception.ParamName == null)
            {
                throw new global::System.ArgumentException(exception.Message, parameterName, exception);
            }
        }

        private static string __P__Text(long value)
        {
            return value.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool __P__TryString(global::System.Text.Json.JsonElement token, out string value)
        {
            value = string.Empty;
            if (token.ValueKind != global::System.Text.Json.JsonValueKind.String)
            {
                return false;
            }
            value = token.GetString() ?? string.Empty;
            return true;
        }

        // JSON Schema counts Unicode code points: a surrogate pair is one character.
        private static long __P__Length(string value)
        {
            long count = 0;
            for (int index = 0; index < value.Length; index++)
            {
                count++;
                if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    index++;
                }
            }
            return count;
        }

        private static void __P__String(global::System.Text.Json.JsonElement token, string path, string contract, long minLength, long maxLength, global::System.Text.RegularExpressions.Regex? regex, string pattern)
        {
            if (!__P__TryString(token, out string value))
            {
                return;
            }
            if (minLength >= 0 || maxLength >= 0)
            {
                long length = __P__Length(value);
                if (minLength >= 0 && length < minLength)
                {
                    __P__Fail(contract, path, ""minLength"", ""the length "" + __P__Text(length) + "" is less than "" + __P__Text(minLength));
                }
                if (maxLength >= 0 && length > maxLength)
                {
                    __P__Fail(contract, path, ""maxLength"", ""the length "" + __P__Text(length) + "" is greater than "" + __P__Text(maxLength));
                }
            }
            if (regex != null)
            {
                bool matched;
                try
                {
                    matched = regex.IsMatch(value);
                }
                catch (global::System.Text.RegularExpressions.RegexMatchTimeoutException exception)
                {
                    __P__Fail(contract, path, ""pattern"", ""matching the pattern '"" + pattern + ""' timed out"", exception);
                    return;
                }
                if (!matched)
                {
                    __P__Fail(contract, path, ""pattern"", ""the value does not match the pattern '"" + pattern + ""'"");
                }
            }
        }

        private static bool __P__IsNumber(global::System.Text.Json.JsonElement token)
        {
            return token.ValueKind == global::System.Text.Json.JsonValueKind.Number;
        }

        // A double is rounded to 15 significant digits (what (decimal)double does), so 0.1 + 0.2 equals 0.3.
        private static bool __P__FromDouble(double number, out decimal value)
        {
            value = 0m;
            double magnitude = global::System.Math.Abs(number);
            if (double.IsNaN(number) || double.IsInfinity(number) || magnitude >= 7.9e28 || (magnitude != 0d && magnitude < 1e-27))
            {
                return false;
            }
            value = (decimal)number;
            return true;
        }

        private static bool __P__IsIntegerLexeme(string raw)
        {
            return raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
        }

        // Set while the checks of a decimal typed value run: its text is the exact value of the CLR decimal (as
        // Json.NET holds a decimal), not a double that has to be rounded to 15 digits.
#pragma warning disable CS0649
        [global::System.ThreadStatic]
        private static bool __P__Exact;
#pragma warning restore CS0649

        private static bool __P__TryDecimal(global::System.Text.Json.JsonElement token, out decimal value)
        {
            value = 0m;
            string raw = token.GetRawText();
            if (__P__Exact)
            {
                return decimal.TryParse(raw, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out value);
            }
            if (__P__IsIntegerLexeme(raw))
            {
                return decimal.TryParse(raw, global::System.Globalization.NumberStyles.AllowLeadingSign, global::System.Globalization.CultureInfo.InvariantCulture, out value);
            }
            return token.TryGetDouble(out double number) && __P__FromDouble(number, out value);
        }

        private static double __P__ToDouble(global::System.Text.Json.JsonElement token)
        {
            if (!token.TryGetDouble(out double value))
            {
                throw new global::System.Text.Json.JsonException(""The number is outside the range of a double."");
            }
            return value;
        }

        private static int __P__Compare(global::System.Text.Json.JsonElement token, decimal limit)
        {
            if (__P__TryDecimal(token, out decimal value))
            {
                return value.CompareTo(limit);
            }
            return __P__ToDouble(token).CompareTo((double)limit);
        }

        private static void __P__Bound(string path, string contract, int comparison, bool lower, bool exclusive, string limit)
        {
            bool violated = lower ? (exclusive ? comparison <= 0 : comparison < 0) : (exclusive ? comparison >= 0 : comparison > 0);
            if (!violated)
            {
                return;
            }
            string keyword = lower ? (exclusive ? ""exclusiveMinimum"" : ""minimum"") : (exclusive ? ""exclusiveMaximum"" : ""maximum"");
            string relation = lower ? (exclusive ? ""greater than "" : ""at least "") : (exclusive ? ""less than "" : ""at most "");
            __P__Fail(contract, path, keyword, ""the value must be "" + relation + limit);
        }

        private static void __P__Min(global::System.Text.Json.JsonElement token, string path, string contract, decimal limit, bool exclusive)
        {
            if (__P__IsNumber(token))
            {
                __P__Bound(path, contract, __P__Compare(token, limit), true, exclusive, limit.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static void __P__Min(global::System.Text.Json.JsonElement token, string path, string contract, double limit, bool exclusive)
        {
            if (__P__IsNumber(token))
            {
                __P__Bound(path, contract, __P__ToDouble(token).CompareTo(limit), true, exclusive, limit.ToString(""R"", global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static void __P__Max(global::System.Text.Json.JsonElement token, string path, string contract, decimal limit, bool exclusive)
        {
            if (__P__IsNumber(token))
            {
                __P__Bound(path, contract, __P__Compare(token, limit), false, exclusive, limit.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static void __P__Max(global::System.Text.Json.JsonElement token, string path, string contract, double limit, bool exclusive)
        {
            if (__P__IsNumber(token))
            {
                __P__Bound(path, contract, __P__ToDouble(token).CompareTo(limit), false, exclusive, limit.ToString(""R"", global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        // Double arithmetic is approximate: a quotient within 1e-9 (relative) of an integer counts as one.
        private static bool __P__NearInteger(double quotient)
        {
            if (double.IsNaN(quotient) || double.IsInfinity(quotient))
            {
                return false;
            }
            return global::System.Math.Abs(quotient - global::System.Math.Round(quotient)) <= 1e-9 * global::System.Math.Max(1d, global::System.Math.Abs(quotient));
        }

        private static void __P__Multiple(global::System.Text.Json.JsonElement token, string path, string contract, decimal divisor)
        {
            if (!__P__IsNumber(token))
            {
                return;
            }
            bool multiple;
            if (__P__TryDecimal(token, out decimal value))
            {
                try
                {
                    multiple = value % divisor == 0m;
                }
                catch (global::System.OverflowException)
                {
                    multiple = __P__NearInteger(__P__ToDouble(token) / (double)divisor);
                }
            }
            else
            {
                multiple = __P__NearInteger(__P__ToDouble(token) / (double)divisor);
            }
            if (!multiple)
            {
                __P__Fail(contract, path, ""multipleOf"", ""the value is not a multiple of "" + divisor.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static void __P__Multiple(global::System.Text.Json.JsonElement token, string path, string contract, double divisor)
        {
            if (__P__IsNumber(token) && !__P__NearInteger(__P__ToDouble(token) / divisor))
            {
                __P__Fail(contract, path, ""multipleOf"", ""the value is not a multiple of "" + divisor.ToString(""R"", global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static int __P__PropertyCount(global::System.Text.Json.JsonElement token)
        {
            int count = 0;
            foreach (var property in token.EnumerateObject())
            {
                count++;
            }
            return count;
        }

        private static int __P__Hash(global::System.Text.Json.JsonElement token)
        {
            unchecked
            {
                switch (token.ValueKind)
                {
                    case global::System.Text.Json.JsonValueKind.Number:
                        // Equal compares as decimal when both fit and as double otherwise, so the hash must not depend on
                        // which one applies: the double rounded to 15 significant digits is the common key.
                        double key = __P__ToDouble(token);
                        return (key == 0d ? 0d : key).ToString(""E14"", global::System.Globalization.CultureInfo.InvariantCulture).GetHashCode();
                    case global::System.Text.Json.JsonValueKind.String:
                        return (token.GetString() ?? string.Empty).GetHashCode();
                    case global::System.Text.Json.JsonValueKind.True:
                        return 1;
                    case global::System.Text.Json.JsonValueKind.False:
                        return 2;
                    case global::System.Text.Json.JsonValueKind.Array:
                        int arrayHash = 17;
                        foreach (var item in token.EnumerateArray())
                        {
                            arrayHash = arrayHash * 31 + __P__Hash(item);
                        }
                        return arrayHash;
                    case global::System.Text.Json.JsonValueKind.Object:
                        int objectHash = 19;
                        foreach (var property in token.EnumerateObject())
                        {
                            objectHash += property.Name.GetHashCode() ^ (__P__Hash(property.Value) * 7);
                        }
                        return objectHash;
                    default:
                        return (int)token.ValueKind;
                }
            }
        }

        // JSON value equality: 1 equals 1.0, arrays compare by position and objects by member (order does not matter).
        private static bool __P__Equal(global::System.Text.Json.JsonElement left, global::System.Text.Json.JsonElement right)
        {
            if (__P__IsNumber(left) && __P__IsNumber(right))
            {
                if (__P__TryDecimal(left, out decimal leftNumber) && __P__TryDecimal(right, out decimal rightNumber))
                {
                    return leftNumber == rightNumber;
                }
                return __P__ToDouble(left) == __P__ToDouble(right);
            }
            if (left.ValueKind != right.ValueKind)
            {
                return false;
            }
            switch (left.ValueKind)
            {
                case global::System.Text.Json.JsonValueKind.String:
                    return global::System.String.Equals(left.GetString(), right.GetString(), global::System.StringComparison.Ordinal);
                case global::System.Text.Json.JsonValueKind.Array:
                    if (left.GetArrayLength() != right.GetArrayLength())
                    {
                        return false;
                    }
                    using (var leftItems = left.EnumerateArray().GetEnumerator())
                    using (var rightItems = right.EnumerateArray().GetEnumerator())
                    {
                        while (leftItems.MoveNext() && rightItems.MoveNext())
                        {
                            if (!__P__Equal(leftItems.Current, rightItems.Current))
                            {
                                return false;
                            }
                        }
                    }
                    return true;
                case global::System.Text.Json.JsonValueKind.Object:
                    if (__P__PropertyCount(left) != __P__PropertyCount(right))
                    {
                        return false;
                    }
                    foreach (var property in left.EnumerateObject())
                    {
                        if (!right.TryGetProperty(property.Name, out var other) || !__P__Equal(property.Value, other))
                        {
                            return false;
                        }
                    }
                    return true;
                default:
                    return true;
            }
        }

        private static void __P__Array(global::System.Text.Json.JsonElement token, string path, string contract, long minItems, long maxItems, bool unique)
        {
            if (token.ValueKind != global::System.Text.Json.JsonValueKind.Array)
            {
                return;
            }
            int count = token.GetArrayLength();
            if (minItems >= 0 && count < minItems)
            {
                __P__Fail(contract, path, ""minItems"", ""the array has "" + __P__Text(count) + "" items, fewer than "" + __P__Text(minItems));
            }
            if (maxItems >= 0 && count > maxItems)
            {
                __P__Fail(contract, path, ""maxItems"", ""the array has "" + __P__Text(count) + "" items, more than "" + __P__Text(maxItems));
            }
            if (!unique)
            {
                return;
            }
            // Items are compared as JSON values (1 equals 1.0, object members in any order); hashing keeps it linear for distinct items.
            var items = new global::System.Collections.Generic.List<global::System.Text.Json.JsonElement>(count);
            foreach (var item in token.EnumerateArray())
            {
                items.Add(item);
            }
            var buckets = new global::System.Collections.Generic.Dictionary<int, global::System.Collections.Generic.List<int>>();
            for (int index = 0; index < items.Count; index++)
            {
                int hash = __P__Hash(items[index]);
                if (!buckets.TryGetValue(hash, out var bucket))
                {
                    bucket = new global::System.Collections.Generic.List<int>();
                    buckets.Add(hash, bucket);
                }
                foreach (int other in bucket)
                {
                    if (__P__Equal(items[other], items[index]))
                    {
                        __P__Fail(contract, path, ""uniqueItems"", ""the items at index "" + __P__Text(other) + "" and "" + __P__Text(index) + "" are equal"");
                        return;
                    }
                }
                bucket.Add(index);
            }
        }

        private static void __P__Properties(global::System.Text.Json.JsonElement token, string path, string contract, long minProperties, long maxProperties)
        {
            if (token.ValueKind != global::System.Text.Json.JsonValueKind.Object)
            {
                return;
            }
            int count = __P__PropertyCount(token);
            if (minProperties >= 0 && count < minProperties)
            {
                __P__Fail(contract, path, ""minProperties"", ""the object has "" + __P__Text(count) + "" properties, fewer than "" + __P__Text(minProperties));
            }
            if (maxProperties >= 0 && count > maxProperties)
            {
                __P__Fail(contract, path, ""maxProperties"", ""the object has "" + __P__Text(count) + "" properties, more than "" + __P__Text(maxProperties));
            }
        }

        private static void __P__Dependent(global::System.Text.Json.JsonElement token, string path, string contract, string[] names)
        {
            if (token.ValueKind != global::System.Text.Json.JsonValueKind.Object || !token.TryGetProperty(names[0], out _))
            {
                return;
            }
            for (int index = 1; index < names.Length; index++)
            {
                if (!token.TryGetProperty(names[index], out _))
                {
                    __P__Fail(contract, path, ""dependentRequired"", ""a property that is present requires the property '"" + names[index] + ""' (required by '"" + names[0] + ""')"");
                }
            }
        }

        private static bool __P__NameMatches(global::System.Text.RegularExpressions.Regex regex, string name, string contract, string path, string pattern)
        {
            try
            {
                return regex.IsMatch(name);
            }
            catch (global::System.Text.RegularExpressions.RegexMatchTimeoutException exception)
            {
                __P__Fail(contract, path, ""patternProperties"", ""matching the pattern '"" + pattern + ""' timed out"", exception);
                return false;
            }
        }

        private static void __P__PatternProperties(global::System.Text.Json.JsonElement token, string path, string contract, global::System.Text.RegularExpressions.Regex regex, string pattern, global::System.Action<global::System.Text.Json.JsonElement, string> validator)
        {
            if (token.ValueKind != global::System.Text.Json.JsonValueKind.Object)
            {
                return;
            }
            foreach (var property in token.EnumerateObject())
            {
                if (!__P__NameMatches(regex, property.Name, contract, path, pattern))
                {
                    continue;
                }
                string memberPath = path + ""."" + property.Name;
                if (!__P__Matches(validator, property.Value, memberPath, out var failure))
                {
                    __P__Fail(contract, memberPath, ""patternProperties"", ""the value does not satisfy the schema of the pattern '"" + pattern + ""'"", failure);
                }
            }
        }

        private static void __P__PropertyNames(global::System.Text.Json.JsonElement token, string path, string contract, global::System.Action<global::System.Text.Json.JsonElement, string> validator)
        {
            if (token.ValueKind != global::System.Text.Json.JsonValueKind.Object)
            {
                return;
            }
            foreach (var property in token.EnumerateObject())
            {
                if (!__P__Matches(validator, global::System.Text.Json.JsonSerializer.SerializeToElement(property.Name), path, out var failure))
                {
                    __P__Fail(contract, path, ""propertyNames"", ""a property name does not satisfy the schema"");
                }
            }
        }

        private static void __P__Contains(global::System.Text.Json.JsonElement token, string path, string contract, long minContains, long maxContains, global::System.Action<global::System.Text.Json.JsonElement, string> validator)
        {
            if (token.ValueKind != global::System.Text.Json.JsonValueKind.Array)
            {
                return;
            }
            long matches = 0;
            int index = 0;
            foreach (var item in token.EnumerateArray())
            {
                if (__P__Matches(validator, item, path + ""["" + __P__Text(index) + ""]"", out _))
                {
                    matches++;
                }
                index++;
            }
            if (matches < minContains)
            {
                __P__Fail(contract, path, minContains == 1 ? ""contains"" : ""minContains"", ""only "" + __P__Text(matches) + "" items satisfy the schema, at least "" + __P__Text(minContains) + "" required"");
            }
            if (maxContains >= 0 && matches > maxContains)
            {
                __P__Fail(contract, path, ""maxContains"", __P__Text(matches) + "" items satisfy the schema, at most "" + __P__Text(maxContains) + "" allowed"");
            }
        }

        private static void __P__Prefix(global::System.Text.Json.JsonElement token, string path, string contract, global::System.Action<global::System.Text.Json.JsonElement, string>[] positional, global::System.Action<global::System.Text.Json.JsonElement, string>? rest)
        {
            if (token.ValueKind != global::System.Text.Json.JsonValueKind.Array)
            {
                return;
            }
            int index = 0;
            foreach (var item in token.EnumerateArray())
            {
                var validator = index < positional.Length ? positional[index] : rest;
                if (validator != null)
                {
                    string itemPath = path + ""["" + __P__Text(index) + ""]"";
                    if (!__P__Matches(validator, item, itemPath, out var failure))
                    {
                        __P__Fail(contract, itemPath, index < positional.Length ? ""prefixItems"" : ""items"", ""the item does not satisfy the schema of its position"", failure);
                    }
                }
                index++;
            }
        }
";
    }
}
