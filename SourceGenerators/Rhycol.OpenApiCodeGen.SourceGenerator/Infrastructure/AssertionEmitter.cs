using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Emits the assertions of a <see cref="GeneratedAssertionGraph"/> (Newtonsoft.Json: JToken, see
    /// AssertionEmitter.SystemTextJson.cs for System.Text.Json: JsonElement): one exception-free
    /// <c>static bool</c> method per distinct schema, in the class of the generated client next to the helpers
    /// of <see cref="ConstraintValidationEmitter"/> (they share the helper prefix, the regular expression
    /// fields and the bool cores of the keyword checks).
    ///
    /// Members written for every node and root (P is the helper prefix):
    /// <code>
    /// private static bool PAssertNode7(JToken value, bool request, ref PAFailure failure);
    /// private static bool PAssertMatches_Name(JToken value, bool request, ref PAFailure failure);
    /// private static void PAssertRoot_Name(JToken value, string path, string contract, string? parameterName);
    /// </code>
    /// A method returns false at the first violation and records the keyword, a detail and the JSON path
    /// relative to the value in <c>failure</c> (a branch of anyOf / oneOf / not and a contains probe use a scratch
    /// failure). <c>request</c> selects the direction: a readOnly property is not required in a request, a
    /// writeOnly one not in a response. <c>PAssertRoot_Name</c> is the entry that turns the failure into the
    /// exception of the contract: "request" an ArgumentException (with the parameter name), otherwise the JSON
    /// exception of the serializer, with the message "The {contract} value at {path} violates {keyword}: {detail}.".
    /// Each keyword applies to the JSON types it is defined for only (properties to objects, minLength to strings).
    /// </summary>
    internal sealed partial class AssertionEmitter
    {
        private readonly ConstraintValidationEmitter _constraints;
        private readonly JsonSerializerProfile _json;
        private readonly GeneratedAssertionGraph _graph;
        private readonly string _prefix;
        private readonly string _token;

        internal AssertionEmitter(
            ConstraintValidationEmitter constraints,
            JsonSerializerProfile json,
            GeneratedAssertionGraph graph)
        {
            _constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _prefix = constraints.HelperPrefix;
            _token = json.AnyTypeName;
        }

        /// <summary>The bool method of a root: <c>(token, bool request, ref failure)</c>.</summary>
        internal string MatchMethodName(string root) => _prefix + "AssertMatches_" + root;

        /// <summary>The entry of a root: <c>(token, string path, string contract, string? parameterName)</c>, throws.</summary>
        internal string AssertMethodName(string root) => _prefix + "AssertRoot_" + root;

        /// <summary>The type of the failure information.</summary>
        internal string FailureTypeName => _prefix + "AFailure";

        private string NodeMethodName(int id) => _prefix + "AssertNode" + id.ToString(CultureInfo.InvariantCulture);

        private string LiteralName(int index) => _prefix + "AssertLiteral" + index.ToString(CultureInfo.InvariantCulture);

        private string EnumName(int id) => _prefix + "AssertEnum" + id.ToString(CultureInfo.InvariantCulture);

        /// <summary>Writes the helpers, the literal fields, the node methods and the entries of the roots.</summary>
        internal void AppendMembers(StringBuilder source)
        {
            foreach (GeneratedAssertionRoot root in _graph.Roots)
            {
                if (!IsIdentifier(root.Name))
                {
                    throw new ArgumentException("The root name '" + root.Name + "' is not an identifier.");
                }
            }

            // The bool cores of the keyword checks, Equal and the regular expression fields are in the helper block.
            _constraints.RequireHelpers();
            source.AppendLine(_json.IsSystemTextJson ? SystemTextJsonHelperSource.Replace("__P__", _prefix) : NewtonsoftHelperSource.Replace("__P__", _prefix));
            if (_graph.Nodes.Any(static node => node.Format == "date" || node.Format == "date-time" ||
                                                node.Format == "uuid" || node.Format == "byte"))
            {
                source.AppendLine(FormatHelperSource.Replace("__P__", _prefix));
            }

            for (int index = 0; index < _graph.Literals.Count; index++)
            {
                source.Append("        private static readonly ").Append(_token).Append(' ').Append(LiteralName(index))
                    .Append(" = ").Append(_prefix).Append("AParse(")
                    .Append(GeneratedSourceEmitter.StringLiteral(_graph.Literals[index])).AppendLine(");");
            }

            // Declared after the literals: static fields are initialized in the order of the declaration.
            foreach (GeneratedAssertionNode node in _graph.Nodes.Where(static node => node.EnumLiterals is not null))
            {
                source.Append("        private static readonly ").Append(_token).Append("[] ").Append(EnumName(node.Id))
                    .Append(" = new ").Append(_token).Append("[] { ")
                    .Append(string.Join(", ", node.EnumLiterals!.Select(LiteralName))).AppendLine(" };");
            }

            foreach (GeneratedAssertionNode node in _graph.Nodes)
            {
                AppendNode(source, node);
            }

            foreach (GeneratedAssertionRoot root in _graph.Roots)
            {
                AppendRoot(source, root);
            }
        }

        private static bool IsIdentifier(string name)
        {
            if (name.Length == 0 || char.IsDigit(name[0]))
            {
                return false;
            }

            return name.All(static character => (character >= 'a' && character <= 'z') ||
                                                (character >= 'A' && character <= 'Z') ||
                                                (character >= '0' && character <= '9') || character == '_');
        }

        private void AppendRoot(StringBuilder source, GeneratedAssertionRoot root)
        {
            source.AppendLine();
            source.Append("        private static bool ").Append(MatchMethodName(root.Name)).Append('(').Append(_token)
                .Append(" value, bool request, ref ").Append(FailureTypeName).AppendLine(" failure)");
            source.AppendLine("        {");
            source.Append("            return ").Append(root.Node < 0 ? "true" : NodeMethodName(root.Node) + "(value, request, ref failure)")
                .AppendLine(";");
            source.AppendLine("        }");
            source.AppendLine();
            source.Append("        private static void ").Append(AssertMethodName(root.Name)).Append('(').Append(_token)
                .AppendLine(" value, string path, string contract, string? parameterName)");
            source.AppendLine("        {");
            if (root.Node >= 0)
            {
                source.Append("            ").Append(FailureTypeName).AppendLine(" failure = default;");
                source.Append("            if (").Append(NodeMethodName(root.Node))
                    .AppendLine("(value, contract == \"request\", ref failure))");
                source.AppendLine("            {");
                source.AppendLine("                return;");
                source.AppendLine("            }");
                source.Append("            ").Append(_prefix)
                    .AppendLine("AThrow(contract, path + (failure.Path ?? string.Empty), parameterName, ref failure);");
            }

            source.AppendLine("        }");
        }

        private void AppendNode(StringBuilder source, GeneratedAssertionNode node)
        {
            source.AppendLine();
            if (node.Name.Length > 0)
            {
                source.Append("        // The schema that a $ref points to: ")
                    .AppendLine(node.Name.Replace('\r', ' ').Replace('\n', ' '));
            }

            source.Append("        private static bool ").Append(NodeMethodName(node.Id)).Append('(').Append(_token)
                .Append(" value, bool request, ref ").Append(FailureTypeName).AppendLine(" failure)");
            source.AppendLine("        {");
            var body = new NodeWriter(this, source);
            if (!body.Write(node))
            {
                source.AppendLine("            return true;");
            }

            source.AppendLine("        }");
        }

        private static string Count(long? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) + "L" : "-1L";
        }

        private static string Literal(string text) => GeneratedSourceEmitter.StringLiteral(text);

        /// <summary>Writes the statements of one node method.</summary>
        private sealed class NodeWriter
        {
            private const string I = "            ";
            private readonly AssertionEmitter _owner;
            private readonly StringBuilder _source;
            private readonly string _p;
            private int _counter;

            internal NodeWriter(AssertionEmitter owner, StringBuilder source)
            {
                _owner = owner;
                _source = source;
                _p = owner._prefix;
            }

            private string Fail(string keyword, string detail) =>
                _p + "AFail(ref failure, " + Literal(keyword) + ", " + Literal(detail) + ")";

            private void Line(string text) => _source.Append(I).AppendLine(text);

            private string Next() => (_counter++).ToString(CultureInfo.InvariantCulture);

            // True when the method ends with an unconditional return (nothing may follow it).
            private bool _terminated;

            internal bool Write(GeneratedAssertionNode node)
            {
                if (node.AlwaysFalse)
                {
                    Line("return " + Fail("false", "no value satisfies the schema false") + ";");
                    return true;
                }

                WriteTypes(node);
                if (_terminated)
                {
                    return true;
                }

                WriteValueKeywords(node);
                WriteConstraints(node);
                bool hasObject = node.Required.Count > 0 || node.Properties.Count > 0 || node.DependentRequired.Count > 0 ||
                                 node.AdditionalProperties != OpenApiSemanticAdditionalProperties.Open ||
                                 node.PatternProperties.Count > 0 || node.PropertyNamesChild >= 0;
                if (hasObject)
                {
                    WriteObject(node);
                }

                bool hasArray = node.ItemsChild >= 0 || node.PrefixItems.Any(static child => child >= 0) || node.HasContains;
                if (hasArray)
                {
                    WriteArray(node);
                }

                WriteComposition(node);
                return _terminated;
            }

            private void WriteTypes(GeneratedAssertionNode node)
            {
                if (!node.Types.HasValue)
                {
                    return;
                }

                OpenApiSemanticAssertionTypes types = node.Types.Value;
                var tests = new List<string>();
                var names = new List<string>();
                void add(OpenApiSemanticAssertionTypes flag, string name, string test)
                {
                    if ((types & flag) != 0)
                    {
                        tests.Add(test);
                        names.Add(name);
                    }
                }

                add(OpenApiSemanticAssertionTypes.Null, "null", _p + "AIsNull(value)");
                add(OpenApiSemanticAssertionTypes.Boolean, "boolean", _p + "AIsBoolean(value)");
                add(OpenApiSemanticAssertionTypes.Integer, "integer", _p + "AIsInteger(value)");
                add(OpenApiSemanticAssertionTypes.Number, "number", _p + "IsNumber(value)");
                add(OpenApiSemanticAssertionTypes.String, "string", _p + "AIsString(value)");
                add(OpenApiSemanticAssertionTypes.Array, "array", _p + "AIsArray(value)");
                add(OpenApiSemanticAssertionTypes.Object, "object", _p + "AIsObject(value)");
                if (tests.Count == 0)
                {
                    Line("return " + Fail("type", "no JSON type is allowed") + ";");
                    _terminated = true;
                    return;
                }

                Line("if (!(" + string.Join(" || ", tests) + "))");
                Line("{");
                Line("    return " + Fail("type", "the value must be of type " + string.Join(" or ", names)) + ";");
                Line("}");
            }

            private void WriteValueKeywords(GeneratedAssertionNode node)
            {
                if (node.EnumLiterals is not null)
                {
                    Line("if (!" + _p + "AIn(value, " + _owner.EnumName(node.Id) + "))");
                    Line("{");
                    Line("    return " + Fail("enum", "the value is not one of the allowed values") + ";");
                    Line("}");
                }

                if (node.ConstLiteral >= 0)
                {
                    Line("if (!" + _p + "Equal(value, " + _owner.LiteralName(node.ConstLiteral) + "))");
                    Line("{");
                    Line("    return " + Fail("const", "the value is not the expected constant") + ";");
                    Line("}");
                }

                switch (node.Format)
                {
                    case "date":
                    case "date-time":
                    case "uuid":
                    case "byte":
                        string text = "text" + Next();
                        string helper = node.Format == "date" ? "AIsFullDate"
                            : node.Format == "date-time" ? "AIsDateTime"
                            : node.Format == "uuid" ? "AIsUuid" : "AIsBase64";
                        Line("if (" + _p + "TryString(value, out string " + text + ") && !" + _p + helper + "(" + text + "))");
                        Line("{");
                        Line("    return " + Fail("format", "the value is not a valid " + node.Format) + ";");
                        Line("}");
                        break;
                    case "int32":
                    case "int64":
                        Line("if (!" + _p + "AIntegerRange(value, " + (node.Format == "int32" ? "true" : "false") + "))");
                        Line("{");
                        Line("    return " + Fail("format", "the value is outside the range of " + node.Format) + ";");
                        Line("}");
                        break;
                }
            }

            private void WriteConstraints(GeneratedAssertionNode node)
            {
                GeneratedConstraints? set = node.Constraints;
                if (set is null)
                {
                    return;
                }

                string head = _p;
                void core(string call)
                {
                    string number = Next();
                    Line("if (!" + head + call + ", out string keyword" + number + ", out string detail" + number + "))");
                    Line("{");
                    Line("    return " + _p + "AFailWith(ref failure, keyword" + number + ", detail" + number + ", null);");
                    Line("}");
                }

                if (set.MinLength.HasValue || set.MaxLength.HasValue || set.Pattern is not null)
                {
                    string number = Next();
                    Line("if (!" + head + "StringCore(value, " + Count(set.MinLength) + ", " + Count(set.MaxLength) + ", " +
                         (set.Pattern is null ? "null" : _owner._constraints.RegexField(set.Pattern, set.PatternEcmaScript)) + ", " +
                         Literal(ConstraintValidationEmitter.Clip(set.Pattern ?? string.Empty)) +
                         ", out string keyword" + number + ", out string detail" + number +
                         ", out global::System.Exception? inner" + number + "))");
                    Line("{");
                    Line("    return " + _p + "AFailWith(ref failure, keyword" + number + ", detail" + number + ", inner" + number + ");");
                    Line("}");
                }

                bound("MinCore", set.Minimum, false);
                bound("MinCore", set.ExclusiveMinimum, true);
                bound("MaxCore", set.Maximum, false);
                bound("MaxCore", set.ExclusiveMaximum, true);
                if (set.MultipleOf is not null)
                {
                    core("MultipleCore(value, " + ConstraintValidationEmitter.NumberLiteral(set.MultipleOf) + "");
                }

                if (set.MinItems.HasValue || set.MaxItems.HasValue || set.UniqueItems)
                {
                    core("ArrayCore(value, " + Count(set.MinItems) + ", " + Count(set.MaxItems) + ", " +
                         (set.UniqueItems ? "true" : "false"));
                }

                if (set.MinProperties.HasValue || set.MaxProperties.HasValue)
                {
                    core("PropertiesCore(value, " + Count(set.MinProperties) + ", " + Count(set.MaxProperties));
                }

                void bound(string method, string? lexeme, bool exclusive)
                {
                    if (lexeme is not null)
                    {
                        core(method + "(value, " + ConstraintValidationEmitter.NumberLiteral(lexeme) + ", " +
                             (exclusive ? "true" : "false"));
                    }
                }
            }

            private string Call(int child, string value, string failure) =>
                _owner.NodeMethodName(child) + "(" + value + ", request, ref " + failure + ")";

            private void WriteObject(GeneratedAssertionNode node)
            {
                Line("if (" + _p + "AIsObject(value))");
                Line("{");
                foreach (GeneratedAssertionRequired required in node.Required)
                {
                    string condition = "!" + _p + "AHas(value, " + Literal(required.Name) + ")";
                    if (required.SkipInRequest)
                    {
                        condition += " && !request";
                    }
                    else if (required.SkipInResponse)
                    {
                        condition += " && request";
                    }

                    Line("    if (" + condition + ")");
                    Line("    {");
                    Line("        return " + Fail("required", "the required property '" + required.Name + "' is missing") + ";");
                    Line("    }");
                }

                foreach (GeneratedDependentRequired entry in node.DependentRequired)
                {
                    string number = Next();
                    Line("    if (!" + _p + "DependentCore(value, new string[] { " +
                         string.Join(", ", new[] { entry.Property }.Concat(entry.Required).Select(Literal)) +
                         " }, out string keyword" + number + ", out string detail" + number + "))");
                    Line("    {");
                    Line("        return " + _p + "AFailWith(ref failure, keyword" + number + ", detail" + number + ", null);");
                    Line("    }");
                }

                foreach (GeneratedAssertionProperty property in node.Properties)
                {
                    string child = "child" + Next();
                    Line("    if (" + _p + "AGet(value, " + Literal(property.Name) + ", out " + _owner._token + " " + child + ") && !" +
                         Call(property.Child, child, "failure") + ")");
                    Line("    {");
                    Line("        return " + _p + "AAt(ref failure, " + Literal("." + property.Name) + ");");
                    Line("    }");
                }

                bool additional = node.AdditionalProperties != OpenApiSemanticAdditionalProperties.Open;
                if (additional || node.PatternProperties.Count > 0 || node.PropertyNamesChild >= 0)
                {
                    WriteMemberLoop(node, additional);
                }

                Line("}");
            }

            private void WriteMemberLoop(GeneratedAssertionNode node, bool additional)
            {
                Line("    foreach (var property in " + _owner.ObjectProperties("value") + ")");
                Line("    {");
                Line("        string name = property.Name;");
                bool track = additional;
                if (track)
                {
                    Line("        bool known = false;");
                    if (node.DeclaredNames.Count > 0)
                    {
                        Line("        switch (name)");
                        Line("        {");
                        foreach (string name in node.DeclaredNames)
                        {
                            Line("            case " + Literal(name) + ":");
                        }

                        Line("                known = true;");
                        Line("                break;");
                        Line("        }");
                    }
                }

                foreach (GeneratedAssertionPatternProperty entry in node.PatternProperties)
                {
                    string number = Next();
                    string field = _owner._constraints.RegexField(entry.Pattern, entry.EcmaScript);
                    Line("        if (!" + _p + "AMatches(" + field + ", name, \"patternProperties\", " +
                         Literal(ConstraintValidationEmitter.Clip(entry.Pattern)) + ", ref failure, out bool matched" + number + "))");
                    Line("        {");
                    Line("            return false;");
                    Line("        }");
                    Line("        if (matched" + number + ")");
                    Line("        {");
                    if (track)
                    {
                        Line("            known = true;");
                    }

                    if (entry.Child >= 0)
                    {
                        Line("            if (!" + Call(entry.Child, "property.Value", "failure") + ")");
                        Line("            {");
                        Line("                return " + _p + "AAt(ref failure, \".\" + name);");
                        Line("            }");
                    }

                    Line("        }");
                }

                if (additional)
                {
                    Line("        if (!known)");
                    Line("        {");
                    if (node.AdditionalProperties == OpenApiSemanticAdditionalProperties.Closed)
                    {
                        Line("            " + Fail("additionalProperties", "an undeclared property is not allowed") + ";");
                        Line("            return " + _p + "AAt(ref failure, \".\" + name);");
                    }
                    else
                    {
                        Line("            if (!" + Call(node.AdditionalChild, "property.Value", "failure") + ")");
                        Line("            {");
                        Line("                return " + _p + "AAt(ref failure, \".\" + name);");
                        Line("            }");
                    }

                    Line("        }");
                }

                if (node.PropertyNamesChild >= 0)
                {
                    Line("        " + _owner.FailureTypeName + " nameFailure = default;");
                    Line("        if (!" + Call(node.PropertyNamesChild, _owner.NameToken("name"), "nameFailure") + ")");
                    Line("        {");
                    Line("            return " + Fail("propertyNames", "a property name does not satisfy the schema") + ";");
                    Line("        }");
                }

                Line("    }");
            }

            private void WriteArray(GeneratedAssertionNode node)
            {
                bool positional = node.ItemsChild >= 0 || node.PrefixItems.Any(static child => child >= 0);
                Line("if (" + _p + "AIsArray(value))");
                Line("{");
                if (positional)
                {
                    Line("    int index = 0;");
                }

                if (node.HasContains)
                {
                    Line("    long contained = 0;");
                }

                Line("    foreach (" + _owner._token + " item in " + _owner.ArrayItems("value") + ")");
                Line("    {");
                if (positional)
                {
                    string segment = "\"[\" + " + _p + "Text(index) + \"]\"";
                    if (node.PrefixItems.Count > 0)
                    {
                        Line("        switch (index)");
                        Line("        {");
                        for (int position = 0; position < node.PrefixItems.Count; position++)
                        {
                            if (node.PrefixItems[position] < 0)
                            {
                                continue;
                            }

                            Line("            case " + position.ToString(CultureInfo.InvariantCulture) + ":");
                            Line("                if (!" + Call(node.PrefixItems[position], "item", "failure") + ")");
                            Line("                {");
                            Line("                    return " + _p + "AAt(ref failure, " + segment + ");");
                            Line("                }");
                            Line("                break;");
                        }

                        if (node.ItemsChild >= 0)
                        {
                            Line("            default:");
                            Line("                if (!" + Call(node.ItemsChild, "item", "failure") + ")");
                            Line("                {");
                            Line("                    return " + _p + "AAt(ref failure, " + segment + ");");
                            Line("                }");
                            Line("                break;");
                        }

                        Line("        }");
                    }
                    else
                    {
                        Line("        if (!" + Call(node.ItemsChild, "item", "failure") + ")");
                        Line("        {");
                        Line("            return " + _p + "AAt(ref failure, " + segment + ");");
                        Line("        }");
                    }

                    Line("        index++;");
                }

                if (node.HasContains)
                {
                    if (node.ContainsChild < 0)
                    {
                        Line("        contained++;");
                    }
                    else
                    {
                        Line("        " + _owner.FailureTypeName + " probe = default;");
                        Line("        if (" + Call(node.ContainsChild, "item", "probe") + ")");
                        Line("        {");
                        Line("            contained++;");
                        Line("        }");
                    }
                }

                Line("    }");
                if (node.HasContains)
                {
                    string minimum = node.MinContains.ToString(CultureInfo.InvariantCulture);
                    if (node.MinContains > 0)
                    {
                        Line("    if (contained < " + minimum + ")");
                        Line("    {");
                        Line("        return " + _p + "AFail(ref failure, " + Literal(node.MinContains == 1 ? "contains" : "minContains") +
                             ", \"only \" + " + _p + "Text(contained) + \" items satisfy the schema, at least " + minimum + " required\");");
                        Line("    }");
                    }

                    if (node.MaxContains.HasValue)
                    {
                        string maximum = node.MaxContains.Value.ToString(CultureInfo.InvariantCulture);
                        Line("    if (contained > " + maximum + ")");
                        Line("    {");
                        Line("        return " + _p + "AFail(ref failure, \"maxContains\", " + _p +
                             "Text(contained) + \" items satisfy the schema, at most " + maximum + " allowed\");");
                        Line("    }");
                    }
                }

                Line("}");
            }

            private void WriteComposition(GeneratedAssertionNode node)
            {
                foreach (int child in node.AllOf)
                {
                    Line("if (!" + Call(child, "value", "failure") + ")");
                    Line("{");
                    Line("    return false;");
                    Line("}");
                }

                if (node.AnyOf.Count > 0)
                {
                    Line("{");
                    Line("    " + _owner.FailureTypeName + " branch = default;");
                    Line("    if (!(" + string.Join(" || ", node.AnyOf.Select(child => Call(child, "value", "branch"))) + "))");
                    Line("    {");
                    Line("        return " + Fail("anyOf", "the value does not satisfy any of the schemas") + ";");
                    Line("    }");
                    Line("}");
                }

                if (node.HasOneOf)
                {
                    Line("{");
                    Line("    " + _owner.FailureTypeName + " branch = default;");
                    Line("    int matched = 0;");
                    for (int index = 0; index < node.OneOf.Count; index++)
                    {
                        int child = node.OneOf[index];
                        if (child < 0)
                        {
                            Line("    matched++;");
                        }
                        else
                        {
                            Line("    if (" + (index > 0 ? "matched < 2 && " : string.Empty) + Call(child, "value", "branch") + ")");
                            Line("    {");
                            Line("        matched++;");
                            Line("    }");
                        }
                    }

                    Line("    if (matched != 1)");
                    Line("    {");
                    Line("        return " + _p + "AFail(ref failure, \"oneOf\", matched == 0 ? \"the value does not satisfy any of the schemas\" : \"the value satisfies more than one of the schemas\");");
                    Line("    }");
                    Line("}");
                }

                if (node.HasNot)
                {
                    if (node.NotChild < 0)
                    {
                        Line("return " + Fail("not", "the value must not satisfy the schema") + ";");
                        _terminated = true;
                    }
                    else
                    {
                        Line("{");
                        Line("    " + _owner.FailureTypeName + " branch = default;");
                        Line("    if (" + Call(node.NotChild, "value", "branch") + ")");
                        Line("    {");
                        Line("        return " + Fail("not", "the value must not satisfy the schema") + ";");
                        Line("    }");
                        Line("}");
                    }
                }
            }
        }
    }
}
