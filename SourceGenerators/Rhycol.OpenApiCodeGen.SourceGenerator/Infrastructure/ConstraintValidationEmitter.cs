using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Emits the run-time checks of the validation keywords (P6: G29, G30): minLength / maxLength /
    /// pattern, the number bounds and multipleOf, minItems / maxItems / uniqueItems, minProperties /
    /// maxProperties, patternProperties, propertyNames, contains, prefixItems and dependentRequired.
    ///
    /// Responses: the checks of a type are statements inside its JSON contract method (see
    /// ResponseJsonContractPlan), so a oneOf / anyOf candidate and a not schema are decided by the
    /// constraints as well.
    /// Requests, parameters, form fields, response headers and text bodies: the typed value is turned
    /// into a JToken (Newtonsoft.Json) or a JsonElement (System.Text.Json, see
    /// ConstraintValidationEmitter.SystemTextJson.cs) and walked by a generated validator per type
    /// (Validate*), which checks the constraints of the type and descends into array items, map values
    /// and DTO members.
    ///
    /// All numbers are compared as decimal when they fit and as double otherwise; a double is first
    /// rounded to 15 significant digits, which is what makes 0.1 + 0.2 a multiple of 0.1. Regular
    /// expressions are static readonly fields created once (not RegexOptions.Compiled: IL2CPP and other AOT
    /// targets cannot emit IL) with a match timeout.
    /// </summary>
    internal sealed partial class ConstraintValidationEmitter
    {
        private const string NewtonsoftActionType = "global::System.Action<global::Newtonsoft.Json.Linq.JToken, string>";
        private const string SystemTextJsonActionType = "global::System.Action<global::System.Text.Json.JsonElement, string>";

        private readonly IReadOnlyDictionary<string, GeneratedDtoModel> _dtos;
        private readonly string _prefix;
        private readonly List<GeneratedTypeModel> _validatorTypes = new List<GeneratedTypeModel>();
        private readonly Dictionary<GeneratedTypeModel, int> _validatorIndices =
            new Dictionary<GeneratedTypeModel, int>();
        private readonly Dictionary<GeneratedTypeModel, bool> _needsCache =
            new Dictionary<GeneratedTypeModel, bool>();
        private readonly Dictionary<string, string> _regexFields =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<(string Field, string Pattern, bool EcmaScript)> _regexes =
            new List<(string Field, string Pattern, bool EcmaScript)>();
        private readonly JsonSerializerProfile _json;
        private bool _helpersNeeded;

        internal ConstraintValidationEmitter(OpenApiGenerationModel model)
            : this(model, JsonSerializerProfile.Newtonsoft)
        {
        }

        internal ConstraintValidationEmitter(OpenApiGenerationModel model, JsonSerializerProfile json)
        {
            _dtos = model.Dtos.ToDictionary(static dto => dto.Name, StringComparer.Ordinal);
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _prefix = HttpClientParameterSerializationEmitter.CreatePrefix(model, "__OacgConstraint_");
        }

        /// <summary>
        /// Name of the JSON contract method of a type (set by ResponseJsonContractPlan). The checks that
        /// apply a sub-schema (contains, patternProperties, ...) run it as a trial.
        /// </summary>
        internal Func<GeneratedTypeModel, string>? ContractMethodName { get; set; }

        /// <summary>
        /// Name of the shared JsonSerializerOptions of the client (System.Text.Json only): the typed
        /// checks serialize a value with them, like the request body is serialized.
        /// </summary>
        internal string? JsonOptionsName { get; set; }

        private string ActionTypeName => _json.IsSystemTextJson ? SystemTextJsonActionType : NewtonsoftActionType;

        // ---- reachability --------------------------------------------------------------------------

        /// <summary>True when the type, or a type it contains, has a validation keyword to check.</summary>
        internal bool NeedsValidation(GeneratedTypeModel type)
        {
            GeneratedTypeModel key = type.WithNullable(false);
            if (_needsCache.TryGetValue(key, out bool cached))
            {
                return cached;
            }

            bool result = Reaches(type, new HashSet<string>(StringComparer.Ordinal));
            _needsCache[key] = result;
            return result;
        }

        private bool Reaches(GeneratedTypeModel type, HashSet<string> visitedDtos)
        {
            if (type.Kind == GeneratedTypeKind.File)
            {
                return false;
            }

            if (type.ConstraintSets.Count > 0)
            {
                return true;
            }

            switch (type.Kind)
            {
                case GeneratedTypeKind.Array:
                    return Reaches(type.ItemType!, visitedDtos);
                case GeneratedTypeKind.Map:
                    return Reaches(type.ValueType!, visitedDtos);
                case GeneratedTypeKind.Named:
                    if (!_dtos.TryGetValue(type.Name, out GeneratedDtoModel? dto) || !visitedDtos.Add(type.Name))
                    {
                        return false;
                    }

                    return dto.ConstraintSets.Count > 0 ||
                           dto.Properties.Any(property =>
                               !property.Annotations.ReadOnly && Reaches(property.Type, visitedDtos)) ||
                           (dto.ExtensionDataValueType is not null && Reaches(dto.ExtensionDataValueType, visitedDtos));
                default:
                    return false;
            }
        }

        /// <summary>
        /// Calls <paramref name="register"/> for the type of every sub-schema (contains, propertyNames,
        /// patternProperties, prefixItems) found in <paramref name="root"/>. They are evaluated through the
        /// JSON contract method of their type, which therefore has to exist.
        /// </summary>
        internal void CollectSubTypes(GeneratedTypeModel root, Action<GeneratedTypeModel> register)
        {
            var visitedTypes = new HashSet<GeneratedTypeModel>();
            var visitedDtos = new HashSet<string>(StringComparer.Ordinal);
            Walk(root);

            void Walk(GeneratedTypeModel type)
            {
                if (!visitedTypes.Add(type.WithNullable(false)))
                {
                    return;
                }

                RegisterSets(type.ConstraintSets);
                switch (type.Kind)
                {
                    case GeneratedTypeKind.Array:
                        Walk(type.ItemType!);
                        break;
                    case GeneratedTypeKind.Map:
                        Walk(type.ValueType!);
                        break;
                    case GeneratedTypeKind.Named:
                        if (_dtos.TryGetValue(type.Name, out GeneratedDtoModel? dto) && visitedDtos.Add(type.Name))
                        {
                            RegisterSets(dto.ConstraintSets);
                            foreach (GeneratedDtoPropertyModel property in dto.Properties)
                            {
                                if (!property.Annotations.ReadOnly)
                                {
                                    Walk(property.Type);
                                }
                            }

                            if (dto.ExtensionDataValueType is not null)
                            {
                                Walk(dto.ExtensionDataValueType);
                            }
                        }

                        break;
                }
            }

            void RegisterSets(IReadOnlyList<GeneratedConstraints> sets)
            {
                foreach (GeneratedConstraints set in sets)
                {
                    foreach (GeneratedTypeModel subType in set.SubTypes())
                    {
                        register(subType);
                    }
                }
            }
        }

        // ---- typed checks (requests, parameters, headers, text bodies) -------------------------------

        /// <summary>
        /// Writes a statement that validates <paramref name="valueExpression"/> (any typed value, null is
        /// skipped). <paramref name="contract"/> is the C# literal "request" or "response"; a request
        /// violation is an ArgumentException that names <paramref name="parameterExpression"/>.
        /// </summary>
        internal void AppendTypedCheck(
            StringBuilder source,
            string indent,
            GeneratedTypeModel type,
            string valueExpression,
            string pathLiteral,
            string contract,
            string parameterExpression)
        {
            if (!NeedsValidation(type))
            {
                return;
            }

            _helpersNeeded = true;
            source.Append(indent).Append(_prefix).Append("Check(")
                .Append(GetValidatorName(type)).Append(", ")
                .Append(valueExpression).Append(", ")
                .Append(pathLiteral).Append(", ")
                .Append(contract).Append(", ")
                .Append(parameterExpression).AppendLine(");");
        }

        internal void AppendParameterChecks(StringBuilder source, GeneratedOperationModel operation)
        {
            foreach (GeneratedParameterModel parameter in operation.Parameters)
            {
                AppendTypedCheck(
                    source,
                    "            ",
                    parameter.Type,
                    parameter.Name,
                    "\"$\"",
                    "\"request\"",
                    "nameof(" + parameter.Name + ")");
            }
        }

        private string GetValidatorName(GeneratedTypeModel type)
        {
            type = type.WithNullable(false);
            if (!_validatorIndices.TryGetValue(type, out int index))
            {
                index = _validatorTypes.Count;
                _validatorIndices.Add(type, index);
                _validatorTypes.Add(type);
                foreach (GeneratedTypeModel child in GetChildTypes(type))
                {
                    GetValidatorName(child);
                }
            }

            return _prefix + "Validate" + index.ToString(CultureInfo.InvariantCulture);
        }

        // The types the validator of a type descends into (only those that have something to check).
        private IEnumerable<GeneratedTypeModel> GetChildTypes(GeneratedTypeModel type)
        {
            switch (type.Kind)
            {
                case GeneratedTypeKind.Array:
                    if (NeedsValidation(type.ItemType!))
                    {
                        yield return type.ItemType!;
                    }

                    break;
                case GeneratedTypeKind.Map:
                    if (NeedsValidation(type.ValueType!))
                    {
                        yield return type.ValueType!;
                    }

                    break;
                case GeneratedTypeKind.Named:
                    if (_dtos.TryGetValue(type.Name, out GeneratedDtoModel? dto))
                    {
                        foreach (GeneratedDtoPropertyModel property in dto.Properties)
                        {
                            if (!property.Annotations.ReadOnly && NeedsValidation(property.Type))
                            {
                                yield return property.Type;
                            }
                        }

                        if (dto.ExtensionDataValueType is not null && NeedsValidation(dto.ExtensionDataValueType))
                        {
                            yield return dto.ExtensionDataValueType;
                        }
                    }

                    break;
            }
        }

        internal void AppendMethods(StringBuilder source)
        {
            for (int index = 0; index < _validatorTypes.Count; index++)
            {
                source.AppendLine();
                AppendValidator(source, _validatorTypes[index], index);
            }
        }

        private void AppendValidator(StringBuilder source, GeneratedTypeModel type, int index)
        {
            if (_json.IsSystemTextJson)
            {
                AppendSystemTextJsonValidator(source, type, index);
                return;
            }

            source.Append("        private static void ").Append(_prefix).Append("Validate")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .AppendLine("(global::Newtonsoft.Json.Linq.JToken token, string path, string contract)");
            source.AppendLine("        {");
            source.AppendLine("            if (token.Type == global::Newtonsoft.Json.Linq.JTokenType.Null)");
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

            AppendOwnChecks(source, "            ", own, "token", "path", "contract", contractScope: false);
            switch (type.Kind)
            {
                case GeneratedTypeKind.Array:
                    if (NeedsValidation(type.ItemType!))
                    {
                        source.AppendLine("            if (token is global::Newtonsoft.Json.Linq.JArray array)");
                        source.AppendLine("            {");
                        source.AppendLine("                for (int index = 0; index < array.Count; index++)");
                        source.AppendLine("                {");
                        source.Append("                    ").Append(GetValidatorName(type.ItemType!))
                            .AppendLine("(array[index], path + \"[\" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\", contract);");
                        source.AppendLine("                }");
                        source.AppendLine("            }");
                    }

                    break;
                case GeneratedTypeKind.Map:
                    if (NeedsValidation(type.ValueType!))
                    {
                        source.AppendLine("            if (token is global::Newtonsoft.Json.Linq.JObject map)");
                        source.AppendLine("            {");
                        source.AppendLine("                foreach (var property in map.Properties())");
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
                        AppendDtoDescent(source, dto);
                    }

                    break;
            }

            source.AppendLine("        }");
        }

        private void AppendDtoDescent(StringBuilder source, GeneratedDtoModel dto)
        {
            GeneratedDtoPropertyModel[] members = dto.Properties
                .Where(property => !property.Annotations.ReadOnly && NeedsValidation(property.Type))
                .ToArray();
            bool extension = dto.ExtensionDataValueType is not null && NeedsValidation(dto.ExtensionDataValueType);
            if (members.Length == 0 && !extension)
            {
                return;
            }

            source.AppendLine("            if (token is global::Newtonsoft.Json.Linq.JObject objectToken)");
            source.AppendLine("            {");
            foreach (GeneratedDtoPropertyModel member in members)
            {
                string wire = GeneratedSourceEmitter.StringLiteral(member.WireName);
                source.AppendLine("                {");
                source.Append("                    var memberToken = objectToken[").Append(wire).AppendLine("];");
                source.AppendLine("                    if (memberToken != null)");
                source.AppendLine("                    {");
                source.Append("                        ").Append(GetValidatorName(member.Type))
                    .Append("(memberToken, path + \".\" + ").Append(wire).AppendLine(", contract);");
                source.AppendLine("                    }");
                source.AppendLine("                }");
            }

            if (extension)
            {
                source.AppendLine("                foreach (var property in objectToken.Properties())");
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

        // ---- own checks (statements) -----------------------------------------------------------------

        /// <summary>
        /// Writes the checks of the constraint sets against the JToken / JsonElement <paramref name="token"/>
        /// (the helpers of the serializer in use are overloaded by that type). Each check ignores a token of
        /// another JSON type. <paramref name="contract"/> is a C# expression
        /// ("response" literal inside a contract method, the contract variable in a validator).
        /// </summary>
        internal void AppendOwnChecks(
            StringBuilder source,
            string indent,
            IReadOnlyList<GeneratedConstraints> sets,
            string token,
            string path,
            string contract,
            bool contractScope)
        {
            foreach (GeneratedConstraints set in sets)
            {
                _helpersNeeded = true;
                string head = indent + _prefix;
                string common = token + ", " + path + ", " + contract;
                if (set.MinLength.HasValue || set.MaxLength.HasValue || set.Pattern is not null)
                {
                    source.Append(head).Append("String(").Append(common).Append(", ")
                        .Append(Count(set.MinLength)).Append(", ").Append(Count(set.MaxLength)).Append(", ")
                        .Append(set.Pattern is null ? "null" : GetRegexField(set.Pattern, set.PatternEcmaScript)).Append(", ")
                        .Append(GeneratedSourceEmitter.StringLiteral(Clip(set.Pattern ?? string.Empty)))
                        .AppendLine(");");
                }

                AppendBound(source, head, common, "Min", set.Minimum, false);
                AppendBound(source, head, common, "Min", set.ExclusiveMinimum, true);
                AppendBound(source, head, common, "Max", set.Maximum, false);
                AppendBound(source, head, common, "Max", set.ExclusiveMaximum, true);
                if (set.MultipleOf is not null)
                {
                    source.Append(head).Append("Multiple(").Append(common).Append(", ")
                        .Append(NumberLiteral(set.MultipleOf)).AppendLine(");");
                }

                if (set.MinItems.HasValue || set.MaxItems.HasValue || set.UniqueItems)
                {
                    source.Append(head).Append("Array(").Append(common).Append(", ")
                        .Append(Count(set.MinItems)).Append(", ").Append(Count(set.MaxItems)).Append(", ")
                        .Append(set.UniqueItems ? "true" : "false").AppendLine(");");
                }

                if (set.MinProperties.HasValue || set.MaxProperties.HasValue)
                {
                    source.Append(head).Append("Properties(").Append(common).Append(", ")
                        .Append(Count(set.MinProperties)).Append(", ").Append(Count(set.MaxProperties)).AppendLine(");");
                }

                foreach (GeneratedDependentRequired entry in set.DependentRequired)
                {
                    source.Append(head).Append("Dependent(").Append(common).Append(", new string[] { ")
                        .Append(string.Join(", ", new[] { entry.Property }.Concat(entry.Required)
                            .Select(GeneratedSourceEmitter.StringLiteral)))
                        .AppendLine(" });");
                }

                foreach (GeneratedPatternProperty entry in set.PatternProperties)
                {
                    source.Append(head).Append("PatternProperties(").Append(common).Append(", ")
                        .Append(GetRegexField(entry.Pattern, entry.EcmaScript)).Append(", ")
                        .Append(GeneratedSourceEmitter.StringLiteral(Clip(entry.Pattern))).Append(", ")
                        .Append(Matcher(entry.Type, contractScope)).AppendLine(");");
                }

                if (set.PropertyNames is not null)
                {
                    source.Append(head).Append("PropertyNames(").Append(common).Append(", ")
                        .Append(Matcher(set.PropertyNames, contractScope)).AppendLine(");");
                }

                if (set.Contains is not null)
                {
                    source.Append(head).Append("Contains(").Append(common).Append(", ")
                        .Append((set.MinContains ?? 1).ToString(CultureInfo.InvariantCulture)).Append(", ")
                        .Append(Count(set.MaxContains)).Append(", ")
                        .Append(Matcher(set.Contains, contractScope)).AppendLine(");");
                }

                if (set.PrefixItems.Count > 0 || set.ItemsAfterPrefix is not null)
                {
                    source.Append(head).Append("Prefix(").Append(common).Append(", new ").Append(ActionTypeName)
                        .Append("[] { ")
                        .Append(string.Join(", ", set.PrefixItems.Select(item => Matcher(item, contractScope))))
                        .Append(" }, ")
                        .Append(set.ItemsAfterPrefix is null ? "null" : Matcher(set.ItemsAfterPrefix, contractScope))
                        .AppendLine(");");
                }
            }
        }

        private void AppendBound(
            StringBuilder source,
            string head,
            string common,
            string method,
            string? lexeme,
            bool exclusive)
        {
            if (lexeme is null)
            {
                return;
            }

            source.Append(head).Append(method).Append('(').Append(common).Append(", ")
                .Append(NumberLiteral(lexeme)).Append(", ").Append(exclusive ? "true" : "false").AppendLine(");");
        }

        private static string Count(long? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) + "L" : "-1L";
        }

        internal static string Clip(string value)
        {
            return value.Length <= 80 ? value : value.Substring(0, 80) + "...";
        }

        // A C# literal for a JSON number: decimal when it fits (exact), otherwise double.
        internal static string NumberLiteral(string lexeme)
        {
            if (decimal.TryParse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal exact) &&
                double.TryParse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out double approximate) &&
                (exact == 0m ? approximate == 0d : Math.Abs(approximate) >= 1e-27))
            {
                return exact.ToString(CultureInfo.InvariantCulture) + "m";
            }

            double value = double.Parse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture);
            return value.ToString("R", CultureInfo.InvariantCulture) + "D";
        }

        // The sub-schema is evaluated by the JSON contract method of its type, as a trial.
        private string Matcher(GeneratedTypeModel type, bool contractScope)
        {
            if (ContractMethodName is null)
            {
                throw new InvalidOperationException("The JSON contract plan did not register its method names.");
            }

            if (_json.IsSystemTextJson)
            {
                // The System.Text.Json contract methods need neither the JSON text nor line information.
                return "(oacgToken, oacgPath) => " + ContractMethodName(type) + "(oacgToken, oacgPath, " +
                       (type.Nullable ? "true" : "false") + ")";
            }

            return "(oacgToken, oacgPath) => " + ContractMethodName(type) + "(oacgToken, oacgPath, " +
                   (type.Nullable ? "true" : "false") + ", " +
                   (contractScope ? "json" : "string.Empty") + ", " +
                   (contractScope ? "lineStarts" : "new global::System.Collections.Generic.List<int>()") +
                   ", new global::System.Collections.Generic.List<global::System.Tuple<int, int, string>>())";
        }

        private string GetRegexField(string pattern, bool ecmaScript)
        {
            string key = (ecmaScript ? "e:" : "n:") + pattern;
            if (!_regexFields.TryGetValue(key, out string? field))
            {
                field = _prefix + "Regex" + _regexes.Count.ToString(CultureInfo.InvariantCulture);
                _regexFields.Add(key, field);
                _regexes.Add((field, pattern, ecmaScript));
            }

            return field;
        }

        // ---- helpers ---------------------------------------------------------------------------------

        /// <summary>Writes the static helpers and the regular expression fields; call after every other member.</summary>
        internal void AppendHelpers(StringBuilder source)
        {
            if (!_helpersNeeded)
            {
                return;
            }

            // The mode was decided when the specification was parsed: ECMAScript (\d \w \s \b are ASCII-only) or the
            // .NET default with CultureInvariant for patterns that need .NET syntax. The match timeout protects
            // against catastrophic backtracking; compiled regular expressions are not used (IL2CPP / AOT cannot emit IL).
            foreach ((string field, string pattern, bool ecmaScript) in _regexes)
            {
                source.AppendLine();
                source.Append("        private static readonly global::System.Text.RegularExpressions.Regex ")
                    .Append(field).Append(" = new global::System.Text.RegularExpressions.Regex(")
                    .Append(GeneratedSourceEmitter.StringLiteral(pattern))
                    .Append(", global::System.Text.RegularExpressions.RegexOptions.")
                    .Append(ecmaScript ? "ECMAScript" : "CultureInvariant")
                    .AppendLine(", global::System.TimeSpan.FromSeconds(1));");
            }

            if (_json.IsSystemTextJson)
            {
                source.AppendLine(SystemTextJsonHelperSource
                    .Replace("__OPTIONS__", JsonOptionsName ?? throw new InvalidOperationException("The JSON options name was not registered."))
                    .Replace("__P__", _prefix));
                return;
            }

            source.AppendLine(HelperSource.Replace("__P__", _prefix));
        }

        private const string HelperSource = @"
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
            throw inner == null ? new global::Newtonsoft.Json.JsonSerializationException(message) : new global::Newtonsoft.Json.JsonSerializationException(message, inner);
        }

        // One serializer for the typed checks, configured like the request serializer (DefaultSettings, date converter).
        private static readonly global::Newtonsoft.Json.JsonSerializer __P__Serializer = __P__CreateSerializer();

        private static global::Newtonsoft.Json.JsonSerializer __P__CreateSerializer()
        {
            var serializer = global::Newtonsoft.Json.JsonSerializer.Create();
            serializer.Converters.Add(new DateOnlyJsonConverter());
            return serializer;
        }

        private static bool __P__IsTrial(global::System.Exception exception)
        {
            return exception is global::Newtonsoft.Json.JsonException ||
                   exception is global::System.FormatException ||
                   exception is global::System.InvalidCastException ||
                   exception is global::System.OverflowException ||
                   exception is global::System.ArgumentException;
        }

        private static bool __P__Matches(global::System.Action<global::Newtonsoft.Json.Linq.JToken, string> validator, global::Newtonsoft.Json.Linq.JToken token, string path, out global::System.Exception? failure)
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

        private static void __P__Check(global::System.Action<global::Newtonsoft.Json.Linq.JToken, string, string> validator, object? value, string path, string contract, string? parameterName)
        {
            if (value == null)
            {
                return;
            }
            global::Newtonsoft.Json.Linq.JToken token;
            if (value is global::Newtonsoft.Json.Linq.JToken existing)
            {
                token = existing;
            }
            else
            {
                token = global::Newtonsoft.Json.Linq.JToken.FromObject(value, __P__Serializer);
            }
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

        private static bool __P__TryString(global::Newtonsoft.Json.Linq.JToken token, out string value)
        {
            value = string.Empty;
            switch (token.Type)
            {
                case global::Newtonsoft.Json.Linq.JTokenType.String:
                case global::Newtonsoft.Json.Linq.JTokenType.Guid:
                case global::Newtonsoft.Json.Linq.JTokenType.Uri:
                    value = ((global::Newtonsoft.Json.Linq.JValue)token).Value?.ToString() ?? string.Empty;
                    return true;
                case global::Newtonsoft.Json.Linq.JTokenType.Bytes:
                    value = ((global::Newtonsoft.Json.Linq.JValue)token).Value is byte[] bytes ? global::System.Convert.ToBase64String(bytes) : string.Empty;
                    return true;
                default:
                    return false;
            }
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

        private static void __P__String(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, long minLength, long maxLength, global::System.Text.RegularExpressions.Regex? regex, string pattern)
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

        private static bool __P__IsNumber(global::Newtonsoft.Json.Linq.JToken token)
        {
            return token.Type == global::Newtonsoft.Json.Linq.JTokenType.Integer || token.Type == global::Newtonsoft.Json.Linq.JTokenType.Float;
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

        private static bool __P__TryDecimal(global::Newtonsoft.Json.Linq.JToken token, out decimal value)
        {
            value = 0m;
            object? raw = ((global::Newtonsoft.Json.Linq.JValue)token).Value;
            switch (raw)
            {
                case long longValue:
                    value = longValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case ulong unsignedValue:
                    value = unsignedValue;
                    return true;
                case decimal decimalValue:
                    value = decimalValue;
                    return true;
                case double doubleValue:
                    return __P__FromDouble(doubleValue, out value);
                case float floatValue:
                    return __P__FromDouble(double.Parse(floatValue.ToString(""R"", global::System.Globalization.CultureInfo.InvariantCulture), global::System.Globalization.CultureInfo.InvariantCulture), out value);
                default:
                    return decimal.TryParse(global::System.Convert.ToString(raw, global::System.Globalization.CultureInfo.InvariantCulture), global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out value);
            }
        }

        private static double __P__ToDouble(global::Newtonsoft.Json.Linq.JToken token)
        {
            object? raw = ((global::Newtonsoft.Json.Linq.JValue)token).Value;
            if (raw is double doubleValue)
            {
                return doubleValue;
            }
            if (raw is float floatValue)
            {
                return floatValue;
            }
            return double.Parse(global::System.Convert.ToString(raw, global::System.Globalization.CultureInfo.InvariantCulture) ?? ""0"", global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int __P__Compare(global::Newtonsoft.Json.Linq.JToken token, decimal limit)
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

        private static void __P__Min(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, decimal limit, bool exclusive)
        {
            if (__P__IsNumber(token))
            {
                __P__Bound(path, contract, __P__Compare(token, limit), true, exclusive, limit.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static void __P__Min(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, double limit, bool exclusive)
        {
            if (__P__IsNumber(token))
            {
                __P__Bound(path, contract, __P__ToDouble(token).CompareTo(limit), true, exclusive, limit.ToString(""R"", global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static void __P__Max(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, decimal limit, bool exclusive)
        {
            if (__P__IsNumber(token))
            {
                __P__Bound(path, contract, __P__Compare(token, limit), false, exclusive, limit.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static void __P__Max(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, double limit, bool exclusive)
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

        private static void __P__Multiple(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, decimal divisor)
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

        private static void __P__Multiple(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, double divisor)
        {
            if (__P__IsNumber(token) && !__P__NearInteger(__P__ToDouble(token) / divisor))
            {
                __P__Fail(contract, path, ""multipleOf"", ""the value is not a multiple of "" + divisor.ToString(""R"", global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static int __P__Hash(global::Newtonsoft.Json.Linq.JToken token)
        {
            unchecked
            {
                switch (token.Type)
                {
                    case global::Newtonsoft.Json.Linq.JTokenType.Integer:
                    case global::Newtonsoft.Json.Linq.JTokenType.Float:
                        return __P__TryDecimal(token, out decimal number) ? number.GetHashCode() : __P__ToDouble(token).GetHashCode();
                    case global::Newtonsoft.Json.Linq.JTokenType.String:
                        return (((global::Newtonsoft.Json.Linq.JValue)token).Value?.ToString() ?? string.Empty).GetHashCode();
                    case global::Newtonsoft.Json.Linq.JTokenType.Boolean:
                        return object.Equals(((global::Newtonsoft.Json.Linq.JValue)token).Value, true) ? 1 : 2;
                    case global::Newtonsoft.Json.Linq.JTokenType.Array:
                        int arrayHash = 17;
                        foreach (var item in (global::Newtonsoft.Json.Linq.JArray)token)
                        {
                            arrayHash = arrayHash * 31 + __P__Hash(item);
                        }
                        return arrayHash;
                    case global::Newtonsoft.Json.Linq.JTokenType.Object:
                        int objectHash = 19;
                        foreach (var property in ((global::Newtonsoft.Json.Linq.JObject)token).Properties())
                        {
                            objectHash += property.Name.GetHashCode() ^ (__P__Hash(property.Value) * 7);
                        }
                        return objectHash;
                    default:
                        return (int)token.Type;
                }
            }
        }

        // JSON value equality: 1 equals 1.0, arrays compare by position and objects by member (order does not matter).
        private static bool __P__Equal(global::Newtonsoft.Json.Linq.JToken left, global::Newtonsoft.Json.Linq.JToken right)
        {
            if (__P__IsNumber(left) && __P__IsNumber(right))
            {
                if (__P__TryDecimal(left, out decimal leftNumber) && __P__TryDecimal(right, out decimal rightNumber))
                {
                    return leftNumber == rightNumber;
                }
                return __P__ToDouble(left) == __P__ToDouble(right);
            }
            if (left.Type != right.Type)
            {
                return false;
            }
            if (left is global::Newtonsoft.Json.Linq.JArray leftArray && right is global::Newtonsoft.Json.Linq.JArray rightArray)
            {
                if (leftArray.Count != rightArray.Count)
                {
                    return false;
                }
                for (int index = 0; index < leftArray.Count; index++)
                {
                    if (!__P__Equal(leftArray[index], rightArray[index]))
                    {
                        return false;
                    }
                }
                return true;
            }
            if (left is global::Newtonsoft.Json.Linq.JObject leftObject && right is global::Newtonsoft.Json.Linq.JObject rightObject)
            {
                if (leftObject.Count != rightObject.Count)
                {
                    return false;
                }
                foreach (var property in leftObject.Properties())
                {
                    var other = rightObject[property.Name];
                    if (other == null || !__P__Equal(property.Value, other))
                    {
                        return false;
                    }
                }
                return true;
            }
            return global::Newtonsoft.Json.Linq.JToken.DeepEquals(left, right);
        }

        private static void __P__Array(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, long minItems, long maxItems, bool unique)
        {
            if (!(token is global::Newtonsoft.Json.Linq.JArray array))
            {
                return;
            }
            if (minItems >= 0 && array.Count < minItems)
            {
                __P__Fail(contract, path, ""minItems"", ""the array has "" + __P__Text(array.Count) + "" items, fewer than "" + __P__Text(minItems));
            }
            if (maxItems >= 0 && array.Count > maxItems)
            {
                __P__Fail(contract, path, ""maxItems"", ""the array has "" + __P__Text(array.Count) + "" items, more than "" + __P__Text(maxItems));
            }
            if (!unique)
            {
                return;
            }
            // Items are compared as JSON values (1 equals 1.0, object members in any order); hashing keeps it linear for distinct items.
            var buckets = new global::System.Collections.Generic.Dictionary<int, global::System.Collections.Generic.List<int>>();
            for (int index = 0; index < array.Count; index++)
            {
                int hash = __P__Hash(array[index]);
                if (!buckets.TryGetValue(hash, out var bucket))
                {
                    bucket = new global::System.Collections.Generic.List<int>();
                    buckets.Add(hash, bucket);
                }
                foreach (int other in bucket)
                {
                    if (__P__Equal(array[other], array[index]))
                    {
                        __P__Fail(contract, path, ""uniqueItems"", ""the items at index "" + __P__Text(other) + "" and "" + __P__Text(index) + "" are equal"");
                        return;
                    }
                }
                bucket.Add(index);
            }
        }

        private static void __P__Properties(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, long minProperties, long maxProperties)
        {
            if (!(token is global::Newtonsoft.Json.Linq.JObject objectToken))
            {
                return;
            }
            if (minProperties >= 0 && objectToken.Count < minProperties)
            {
                __P__Fail(contract, path, ""minProperties"", ""the object has "" + __P__Text(objectToken.Count) + "" properties, fewer than "" + __P__Text(minProperties));
            }
            if (maxProperties >= 0 && objectToken.Count > maxProperties)
            {
                __P__Fail(contract, path, ""maxProperties"", ""the object has "" + __P__Text(objectToken.Count) + "" properties, more than "" + __P__Text(maxProperties));
            }
        }

        private static void __P__Dependent(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, string[] names)
        {
            if (!(token is global::Newtonsoft.Json.Linq.JObject objectToken) || objectToken.Property(names[0]) == null)
            {
                return;
            }
            for (int index = 1; index < names.Length; index++)
            {
                if (objectToken.Property(names[index]) == null)
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

        private static void __P__PatternProperties(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, global::System.Text.RegularExpressions.Regex regex, string pattern, global::System.Action<global::Newtonsoft.Json.Linq.JToken, string> validator)
        {
            if (!(token is global::Newtonsoft.Json.Linq.JObject objectToken))
            {
                return;
            }
            foreach (var property in objectToken.Properties())
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

        private static void __P__PropertyNames(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, global::System.Action<global::Newtonsoft.Json.Linq.JToken, string> validator)
        {
            if (!(token is global::Newtonsoft.Json.Linq.JObject objectToken))
            {
                return;
            }
            foreach (var property in objectToken.Properties())
            {
                if (!__P__Matches(validator, new global::Newtonsoft.Json.Linq.JValue(property.Name), path, out var failure))
                {
                    __P__Fail(contract, path, ""propertyNames"", ""a property name does not satisfy the schema"");
                }
            }
        }

        private static void __P__Contains(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, long minContains, long maxContains, global::System.Action<global::Newtonsoft.Json.Linq.JToken, string> validator)
        {
            if (!(token is global::Newtonsoft.Json.Linq.JArray array))
            {
                return;
            }
            long matches = 0;
            for (int index = 0; index < array.Count; index++)
            {
                if (__P__Matches(validator, array[index], path + ""["" + __P__Text(index) + ""]"", out _))
                {
                    matches++;
                }
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

        private static void __P__Prefix(global::Newtonsoft.Json.Linq.JToken token, string path, string contract, global::System.Action<global::Newtonsoft.Json.Linq.JToken, string>[] positional, global::System.Action<global::Newtonsoft.Json.Linq.JToken, string>? rest)
        {
            if (!(token is global::Newtonsoft.Json.Linq.JArray array))
            {
                return;
            }
            for (int index = 0; index < array.Count; index++)
            {
                var validator = index < positional.Length ? positional[index] : rest;
                if (validator == null)
                {
                    continue;
                }
                string itemPath = path + ""["" + __P__Text(index) + ""]"";
                if (!__P__Matches(validator, array[index], itemPath, out var failure))
                {
                    __P__Fail(contract, itemPath, index < positional.Length ? ""prefixItems"" : ""items"", ""the item does not satisfy the schema of its position"", failure);
                }
            }
        }
";
    }
}
