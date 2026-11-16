using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class GeneratedAssertionProperty
    {
        internal GeneratedAssertionProperty(string name, int child)
        {
            Name = name;
            Child = child;
        }

        internal string Name { get; }

        /// <summary>The node that the member value must satisfy.</summary>
        internal int Child { get; }
    }

    /// <summary>A required property. A readOnly property is not required in a request, a writeOnly one not in a response.</summary>
    internal sealed class GeneratedAssertionRequired
    {
        internal GeneratedAssertionRequired(string name, bool skipInRequest, bool skipInResponse)
        {
            Name = name;
            SkipInRequest = skipInRequest;
            SkipInResponse = skipInResponse;
        }

        internal string Name { get; }

        internal bool SkipInRequest { get; }

        internal bool SkipInResponse { get; }
    }

    internal sealed class GeneratedAssertionPatternProperty
    {
        internal GeneratedAssertionPatternProperty(string pattern, bool ecmaScript, int child)
        {
            Pattern = pattern;
            EcmaScript = ecmaScript;
            Child = child;
        }

        internal string Pattern { get; }

        internal bool EcmaScript { get; }

        internal int Child { get; }
    }

    /// <summary>
    /// One assertion method of the generated client. Children are referred to by node id; -1 is "no condition"
    /// (a schema that accepts everything is never a node of its own, except the target of a $ref).
    /// Equal nodes are one node, so the generated code is as small as the number of distinct schemas.
    /// </summary>
    internal sealed class GeneratedAssertionNode
    {
        internal GeneratedAssertionNode(int id)
        {
            Id = id;
        }

        internal int Id { get; }

        /// <summary>The component name of a node that a $ref points to (a comment in the generated code); empty otherwise.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>The position of the schema a named node was made from (a diagnostic about a cycle points there).</summary>
        internal OpenApiSourceLocation? Location { get; set; }

        /// <summary>The allowed types; null when every type is allowed.</summary>
        internal OpenApiSemanticAssertionTypes? Types { get; set; }

        /// <summary>Indices into <see cref="GeneratedAssertionGraph.Literals"/>; null when there is no enum.</summary>
        internal IReadOnlyList<int>? EnumLiterals { get; set; }

        /// <summary>Index into <see cref="GeneratedAssertionGraph.Literals"/>; -1 when there is no const.</summary>
        internal int ConstLiteral { get; set; } = -1;

        /// <summary>date, date-time, uuid, byte, int32 or int64; empty for any other format.</summary>
        internal string Format { get; set; } = string.Empty;

        /// <summary>The scalar keywords (sub-schema keywords are empty); null when there are none.</summary>
        internal GeneratedConstraints? Constraints { get; set; }

        internal IReadOnlyList<GeneratedAssertionProperty> Properties { get; set; } =
            Array.Empty<GeneratedAssertionProperty>();

        internal IReadOnlyList<GeneratedAssertionRequired> Required { get; set; } =
            Array.Empty<GeneratedAssertionRequired>();

        internal OpenApiSemanticAdditionalProperties AdditionalProperties { get; set; } =
            OpenApiSemanticAdditionalProperties.Open;

        /// <summary>Every declared property name (also those without a condition); only set when additionalProperties is not Open.</summary>
        internal IReadOnlyList<string> DeclaredNames { get; set; } = Array.Empty<string>();

        /// <summary>The node of the undeclared members (Typed); -1 when they are unconstrained.</summary>
        internal int AdditionalChild { get; set; } = -1;

        /// <summary>The node of the elements (after the prefix items); -1 for none.</summary>
        internal int ItemsChild { get; set; } = -1;

        internal IReadOnlyList<int> PrefixItems { get; set; } = Array.Empty<int>();

        internal IReadOnlyList<GeneratedAssertionPatternProperty> PatternProperties { get; set; } =
            Array.Empty<GeneratedAssertionPatternProperty>();

        internal int PropertyNamesChild { get; set; } = -1;

        internal bool HasContains { get; set; }

        /// <summary>The node of contains; -1 when every element satisfies it.</summary>
        internal int ContainsChild { get; set; } = -1;

        internal long MinContains { get; set; } = 1;

        internal long? MaxContains { get; set; }

        internal IReadOnlyList<GeneratedDependentRequired> DependentRequired { get; set; } =
            Array.Empty<GeneratedDependentRequired>();

        /// <summary>The nodes that must all match (a $ref target of a 3.1 schema with siblings comes first).</summary>
        internal IReadOnlyList<int> AllOf { get; set; } = Array.Empty<int>();

        /// <summary>The nodes of anyOf; empty when the keyword is absent or an always true member makes it hold.</summary>
        internal IReadOnlyList<int> AnyOf { get; set; } = Array.Empty<int>();

        /// <summary>The nodes of oneOf; -1 stands for a member that accepts everything.</summary>
        internal IReadOnlyList<int> OneOf { get; set; } = Array.Empty<int>();

        internal bool HasOneOf { get; set; }

        internal bool HasNot { get; set; }

        /// <summary>The node of not; -1 when the negated schema accepts everything (nothing is valid).</summary>
        internal int NotChild { get; set; } = -1;

        internal bool AlwaysFalse { get; set; }
    }

    /// <summary>An entry point: the node that a caller evaluates (-1: the schema accepts everything).</summary>
    internal sealed class GeneratedAssertionRoot
    {
        internal GeneratedAssertionRoot(string name, int node)
        {
            Name = name;
            Node = node;
        }

        internal string Name { get; }

        internal int Node { get; }
    }

    internal sealed class GeneratedAssertionGraph
    {
        internal GeneratedAssertionGraph(
            IReadOnlyList<GeneratedAssertionNode> nodes,
            IReadOnlyList<string> literals,
            IReadOnlyList<GeneratedAssertionRoot> roots)
        {
            Nodes = nodes;
            Literals = literals;
            Roots = roots;
        }

        internal IReadOnlyList<GeneratedAssertionNode> Nodes { get; }

        /// <summary>The JSON texts of enum / const values, each once.</summary>
        internal IReadOnlyList<string> Literals { get; }

        internal IReadOnlyList<GeneratedAssertionRoot> Roots { get; }
    }

    /// <summary>
    /// Builds a <see cref="GeneratedAssertionGraph"/> from assertion trees. A node that a $ref points to is one
    /// node of its own (so a recursive schema is a cycle); every other node is shared with an equal one.
    /// </summary>
    internal sealed class GeneratedAssertionGraphBuilder
    {
        private readonly List<GeneratedAssertionNode?> _nodes = new List<GeneratedAssertionNode?>();
        private readonly Dictionary<string, int> _nodeKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<(OpenApiSemanticAssertion Target, string Flags), int> _targets =
            new Dictionary<(OpenApiSemanticAssertion Target, string Flags), int>();
        private static readonly IReadOnlyDictionary<string, (bool ReadOnly, bool WriteOnly)> NoFlags =
            new Dictionary<string, (bool ReadOnly, bool WriteOnly)>();
        private readonly List<string> _literals = new List<string>();
        private readonly Dictionary<string, int> _literalIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<GeneratedAssertionRoot> _roots = new List<GeneratedAssertionRoot>();

        /// <summary>Adds an entry point and returns its node (-1: no condition).</summary>
        internal int AddRoot(string name, OpenApiSemanticAssertion assertion)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A root needs a name.", nameof(name));
            }

            if (_roots.Any(root => string.Equals(root.Name, name, StringComparison.Ordinal)))
            {
                throw new ArgumentException("The root '" + name + "' exists already.", nameof(name));
            }

            int node = Convert(assertion);
            _roots.Add(new GeneratedAssertionRoot(name, node));
            return node;
        }

        internal GeneratedAssertionGraph Build()
        {
            GeneratedAssertionNode[] nodes = _nodes
                .Select(static node => node ?? throw new InvalidOperationException("An assertion node was not completed."))
                .ToArray();
            RejectCyclesWithoutData(nodes);
            return new GeneratedAssertionGraph(nodes, _literals.ToArray(), _roots.ToArray());
        }

        // A method that calls itself without going down into the value (allOf / anyOf / oneOf / not on the same value)
        // would never return. Going into a member, an element or a property name always consumes data.
        private static void RejectCyclesWithoutData(GeneratedAssertionNode[] nodes)
        {
            var state = new int[nodes.Length];
            for (int start = 0; start < nodes.Length; start++)
            {
                if (state[start] == 0)
                {
                    visit(start);
                }
            }

            void visit(int id)
            {
                state[id] = 1;
                GeneratedAssertionNode node = nodes[id];
                foreach (int next in node.AllOf.Concat(node.AnyOf).Concat(node.OneOf).Concat(new[] { node.HasNot ? node.NotChild : -1 }))
                {
                    if (next < 0)
                    {
                        continue;
                    }

                    if (state[next] == 1)
                    {
                        throw new GeneratedAssertionCycleException(
                            nodes[next].Name.Length > 0 ? nodes[next].Name : "#" + next.ToString(CultureInfo.InvariantCulture),
                            nodes[next].Location);
                    }

                    if (state[next] == 0)
                    {
                        visit(next);
                    }
                }

                state[id] = 2;
            }
        }

        private int Convert(OpenApiSemanticAssertion assertion)
        {
            return Convert(assertion, NoFlags);
        }

        // inherited: the readOnly / writeOnly property names that the containing object declares in other parts (allOf, $ref).
        private int Convert(OpenApiSemanticAssertion assertion, IReadOnlyDictionary<string, (bool ReadOnly, bool WriteOnly)> inherited)
        {
            if (assertion.Name.Length > 0)
            {
                var targetKey = (assertion, FlagSignature(inherited));
                if (_targets.TryGetValue(targetKey, out int existing))
                {
                    return existing;
                }

                int id = _nodes.Count;
                _nodes.Add(null);
                _targets.Add(targetKey, id);
                var node = new GeneratedAssertionNode(id) { Name = assertion.Name, Location = assertion.Location };
                Fill(node, assertion, inherited);
                _nodes[id] = node;
                return id;
            }

            var candidate = new GeneratedAssertionNode(-1);
            Fill(candidate, assertion, inherited);
            string key = Key(candidate);
            if (key.Length == 0)
            {
                return -1;
            }

            if (_nodeKeys.TryGetValue(key, out int shared))
            {
                return shared;
            }

            int index = _nodes.Count;
            var created = new GeneratedAssertionNode(index);
            Copy(candidate, created);
            _nodes.Add(created);
            _nodeKeys.Add(key, index);
            return index;
        }

        private static string FlagSignature(IReadOnlyDictionary<string, (bool ReadOnly, bool WriteOnly)> flags)
        {
            return string.Concat(flags
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .Select(static entry => entry.Key.Length.ToString(CultureInfo.InvariantCulture) + ":" + entry.Key +
                                        (entry.Value.ReadOnly ? "R" : "-") + (entry.Value.WriteOnly ? "W" : "-")));
        }

        // The flags of the properties that a schema declares, also in its allOf parts and the schema it references.
        private static void CollectFlags(
            OpenApiSemanticAssertion assertion,
            Dictionary<string, (bool ReadOnly, bool WriteOnly)> into,
            HashSet<OpenApiSemanticAssertion> visited)
        {
            if (!visited.Add(assertion))
            {
                return;
            }

            foreach (OpenApiSemanticAssertionProperty property in assertion.Properties)
            {
                bool readOnly = property.ReadOnly;
                bool writeOnly = property.WriteOnly;
                if (into.TryGetValue(property.Name, out (bool ReadOnly, bool WriteOnly) known))
                {
                    readOnly |= known.ReadOnly;
                    writeOnly |= known.WriteOnly;
                }

                if (readOnly || writeOnly)
                {
                    into[property.Name] = (readOnly, writeOnly);
                }
            }

            foreach (OpenApiSemanticAssertionProperty property in assertion.FlagProperties)
            {
                if (property.ReadOnly || property.WriteOnly)
                {
                    into.TryGetValue(property.Name, out (bool ReadOnly, bool WriteOnly) known);
                    into[property.Name] = (property.ReadOnly || known.ReadOnly, property.WriteOnly || known.WriteOnly);
                }
            }

            foreach (OpenApiSemanticAssertion part in assertion.AllOf)
            {
                CollectFlags(part, into, visited);
            }

            if (assertion.Reference is not null)
            {
                CollectFlags(assertion.Reference, into, visited);
            }
        }

        private void Fill(
            GeneratedAssertionNode node,
            OpenApiSemanticAssertion assertion,
            IReadOnlyDictionary<string, (bool ReadOnly, bool WriteOnly)> inherited)
        {
            var effective = new Dictionary<string, (bool ReadOnly, bool WriteOnly)>();
            foreach (KeyValuePair<string, (bool ReadOnly, bool WriteOnly)> entry in inherited)
            {
                effective[entry.Key] = entry.Value;
            }

            CollectFlags(assertion, effective, new HashSet<OpenApiSemanticAssertion>());

            if (assertion.AlwaysFalse)
            {
                node.AlwaysFalse = true;
                return;
            }

            OpenApiSemanticAssertionTypes? types = assertion.Types;
            if (types.HasValue)
            {
                OpenApiSemanticAssertionTypes value = types.Value;
                if ((value & OpenApiSemanticAssertionTypes.Number) != 0)
                {
                    value &= ~OpenApiSemanticAssertionTypes.Integer;
                }

                node.Types = value == OpenApiSemanticAssertionTypes.All || value == (OpenApiSemanticAssertionTypes.All & ~OpenApiSemanticAssertionTypes.Integer)
                    ? (OpenApiSemanticAssertionTypes?)null
                    : value;
            }

            if (assertion.Enum is not null)
            {
                node.EnumLiterals = assertion.Enum.Select(value => Literal(value.RawJson)).ToArray();
            }

            if (assertion.Const.HasValue)
            {
                node.ConstLiteral = Literal(assertion.Const.Value.RawJson);
            }

            node.Format = NormalizeFormat(assertion.Format);
            if (assertion.Constraints is not null)
            {
                node.Constraints = CopyScalars(assertion.Constraints);
            }

            var members = new List<GeneratedAssertionProperty>();
            foreach (OpenApiSemanticAssertionProperty property in assertion.Properties)
            {
                int child = Convert(property.Schema);
                if (child >= 0)
                {
                    members.Add(new GeneratedAssertionProperty(property.Name, child));
                }
            }

            node.Properties = members;
            var required = new List<GeneratedAssertionRequired>();
            foreach (string name in assertion.Required)
            {
                effective.TryGetValue(name, out (bool ReadOnly, bool WriteOnly) flags);
                bool skipInRequest = flags.ReadOnly;
                bool skipInResponse = flags.WriteOnly;
                if (!(skipInRequest && skipInResponse))
                {
                    required.Add(new GeneratedAssertionRequired(name, skipInRequest, skipInResponse));
                }
            }

            node.Required = required;
            node.AdditionalProperties = assertion.AdditionalProperties;
            if (assertion.AdditionalProperties == OpenApiSemanticAdditionalProperties.Typed)
            {
                node.AdditionalChild = Convert(assertion.AdditionalPropertiesSchema!);
                if (node.AdditionalChild < 0)
                {
                    node.AdditionalProperties = OpenApiSemanticAdditionalProperties.Open;
                }
            }

            if (node.AdditionalProperties != OpenApiSemanticAdditionalProperties.Open)
            {
                node.DeclaredNames = assertion.Properties.Select(static property => property.Name).ToArray();
            }

            if (assertion.Items is not null)
            {
                node.ItemsChild = Convert(assertion.Items);
            }

            node.PrefixItems = assertion.PrefixItems.Select(Convert).ToArray();
            var patterns = new List<GeneratedAssertionPatternProperty>();
            foreach (OpenApiSemanticAssertionPatternProperty entry in assertion.PatternProperties)
            {
                // Kept when it accepts everything: a name that matches it is not an undeclared member.
                patterns.Add(new GeneratedAssertionPatternProperty(entry.Pattern, entry.EcmaScript, Convert(entry.Schema)));
            }

            node.PatternProperties = patterns;
            if (assertion.PropertyNames is not null)
            {
                node.PropertyNamesChild = Convert(assertion.PropertyNames);
            }

            if (assertion.Contains is not null)
            {
                node.HasContains = true;
                node.ContainsChild = Convert(assertion.Contains);
                node.MinContains = assertion.MinContains ?? 1;
                node.MaxContains = assertion.MaxContains;
            }

            node.DependentRequired = assertion.DependentRequired
                .Select(static entry => new GeneratedDependentRequired(entry.Property, entry.Required))
                .ToArray();

            var allOf = new List<int>();
            if (assertion.Reference is not null)
            {
                allOf.Add(Convert(assertion.Reference, effective));
            }

            foreach (OpenApiSemanticAssertion part in assertion.AllOf)
            {
                int child = Convert(part, effective);
                if (child >= 0)
                {
                    allOf.Add(child);
                }
            }

            node.AllOf = allOf;
            if (assertion.AnyOf.Count > 0)
            {
                int[] members2 = assertion.AnyOf.Select(member => Convert(member, effective)).ToArray();
                // A member that accepts everything makes anyOf hold.
                node.AnyOf = members2.Contains(-1) ? Array.Empty<int>() : members2;
            }

            if (assertion.OneOf.Count > 0)
            {
                node.HasOneOf = true;
                node.OneOf = assertion.OneOf.Select(member => Convert(member, effective)).ToArray();
            }

            if (assertion.Not is not null)
            {
                node.HasNot = true;
                node.NotChild = Convert(assertion.Not);
            }
        }

        private static void Copy(GeneratedAssertionNode from, GeneratedAssertionNode to)
        {
            to.Types = from.Types;
            to.EnumLiterals = from.EnumLiterals;
            to.ConstLiteral = from.ConstLiteral;
            to.Format = from.Format;
            to.Constraints = from.Constraints;
            to.Properties = from.Properties;
            to.Required = from.Required;
            to.AdditionalProperties = from.AdditionalProperties;
            to.DeclaredNames = from.DeclaredNames;
            to.AdditionalChild = from.AdditionalChild;
            to.ItemsChild = from.ItemsChild;
            to.PrefixItems = from.PrefixItems;
            to.PatternProperties = from.PatternProperties;
            to.PropertyNamesChild = from.PropertyNamesChild;
            to.HasContains = from.HasContains;
            to.ContainsChild = from.ContainsChild;
            to.MinContains = from.MinContains;
            to.MaxContains = from.MaxContains;
            to.DependentRequired = from.DependentRequired;
            to.AllOf = from.AllOf;
            to.AnyOf = from.AnyOf;
            to.OneOf = from.OneOf;
            to.HasOneOf = from.HasOneOf;
            to.HasNot = from.HasNot;
            to.NotChild = from.NotChild;
            to.AlwaysFalse = from.AlwaysFalse;
        }

        private int Literal(string rawJson)
        {
            if (!_literalIds.TryGetValue(rawJson, out int id))
            {
                id = _literals.Count;
                _literals.Add(rawJson);
                _literalIds.Add(rawJson, id);
            }

            return id;
        }

        private static string NormalizeFormat(string format)
        {
            switch (format)
            {
                case "date":
                case "date-time":
                case "uuid":
                case "byte":
                case "int32":
                case "int64":
                    return format;
                default:
                    return string.Empty;
            }
        }

        private static GeneratedConstraints CopyScalars(OpenApiSemanticConstraints set)
        {
            return new GeneratedConstraints
            {
                MinLength = set.MinLength,
                MaxLength = set.MaxLength,
                Pattern = set.Pattern,
                PatternEcmaScript = set.PatternEcmaScript,
                Minimum = set.Minimum,
                Maximum = set.Maximum,
                ExclusiveMinimum = set.ExclusiveMinimum,
                ExclusiveMaximum = set.ExclusiveMaximum,
                MultipleOf = set.MultipleOf,
                MinItems = set.MinItems,
                MaxItems = set.MaxItems,
                UniqueItems = set.UniqueItems,
                MinProperties = set.MinProperties,
                MaxProperties = set.MaxProperties
            };
        }

        // The structural key of a node: two nodes with the same key behave the same. Empty for a node without condition.
        private static string Key(GeneratedAssertionNode node)
        {
            var key = new StringBuilder();
            void part(string name, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    key.Append(name).Append('=').Append(value).Append(';');
                }
            }

            string ids(IEnumerable<int> values) => string.Join(",", values.Select(static value => value.ToString(CultureInfo.InvariantCulture)));
            string count(long? value) => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null!;

            part("false", node.AlwaysFalse ? "1" : null);
            part("types", node.Types.HasValue ? ((int)node.Types.Value).ToString(CultureInfo.InvariantCulture) : null);
            part("enum", node.EnumLiterals is null ? null : "[" + ids(node.EnumLiterals) + "]");
            part("const", node.ConstLiteral >= 0 ? node.ConstLiteral.ToString(CultureInfo.InvariantCulture) : null);
            part("format", node.Format);
            if (node.Constraints is not null)
            {
                GeneratedConstraints set = node.Constraints;
                part("minLength", count(set.MinLength));
                part("maxLength", count(set.MaxLength));
                part("pattern", set.Pattern is null ? null : (set.PatternEcmaScript ? "e:" : "n:") + set.Pattern.Length.ToString(CultureInfo.InvariantCulture) + ":" + set.Pattern);
                part("minimum", set.Minimum);
                part("maximum", set.Maximum);
                part("exclusiveMinimum", set.ExclusiveMinimum);
                part("exclusiveMaximum", set.ExclusiveMaximum);
                part("multipleOf", set.MultipleOf);
                part("minItems", count(set.MinItems));
                part("maxItems", count(set.MaxItems));
                part("unique", set.UniqueItems ? "1" : null);
                part("minProperties", count(set.MinProperties));
                part("maxProperties", count(set.MaxProperties));
            }

            part("properties", node.Properties.Count == 0
                ? null
                : string.Join(",", node.Properties.Select(static property =>
                    property.Name.Length.ToString(CultureInfo.InvariantCulture) + ":" + property.Name + ">" +
                    property.Child.ToString(CultureInfo.InvariantCulture))));
            part("required", node.Required.Count == 0
                ? null
                : string.Join(",", node.Required.Select(static required =>
                    required.Name.Length.ToString(CultureInfo.InvariantCulture) + ":" + required.Name +
                    (required.SkipInRequest ? "R" : "-") + (required.SkipInResponse ? "W" : "-"))));
            part("additional", node.AdditionalProperties == OpenApiSemanticAdditionalProperties.Open
                ? null
                : ((int)node.AdditionalProperties).ToString(CultureInfo.InvariantCulture) + ">" +
                  node.AdditionalChild.ToString(CultureInfo.InvariantCulture));
            part("declared", node.DeclaredNames.Count == 0
                ? null
                : string.Join(",", node.DeclaredNames.Select(static name => name.Length.ToString(CultureInfo.InvariantCulture) + ":" + name)));
            part("items", node.ItemsChild >= 0 ? node.ItemsChild.ToString(CultureInfo.InvariantCulture) : null);
            part("prefix", node.PrefixItems.Count == 0 ? null : ids(node.PrefixItems));
            part("patterns", node.PatternProperties.Count == 0
                ? null
                : string.Join(",", node.PatternProperties.Select(static entry =>
                    (entry.EcmaScript ? "e:" : "n:") + entry.Pattern.Length.ToString(CultureInfo.InvariantCulture) + ":" +
                    entry.Pattern + ">" + entry.Child.ToString(CultureInfo.InvariantCulture))));
            part("propertyNames", node.PropertyNamesChild >= 0 ? node.PropertyNamesChild.ToString(CultureInfo.InvariantCulture) : null);
            if (node.HasContains)
            {
                part("contains", node.ContainsChild.ToString(CultureInfo.InvariantCulture) + "," +
                    node.MinContains.ToString(CultureInfo.InvariantCulture) + "," + count(node.MaxContains));
            }

            part("dependent", node.DependentRequired.Count == 0
                ? null
                : string.Join(",", node.DependentRequired.Select(static entry =>
                    entry.Property.Length.ToString(CultureInfo.InvariantCulture) + ":" + entry.Property + "=" +
                    string.Join("|", entry.Required.Select(static name => name.Length.ToString(CultureInfo.InvariantCulture) + ":" + name)))));
            part("allOf", node.AllOf.Count == 0 ? null : ids(node.AllOf));
            part("anyOf", node.AnyOf.Count == 0 ? null : ids(node.AnyOf));
            part("oneOf", node.HasOneOf ? ids(node.OneOf) + "#" : null);
            part("not", node.HasNot ? node.NotChild.ToString(CultureInfo.InvariantCulture) + "#" : null);
            return key.ToString();
        }
    }

    /// <summary>A schema that, through allOf / anyOf / oneOf / not / $ref alone, requires itself to hold for the same value.</summary>
    internal sealed class GeneratedAssertionCycleException : InvalidOperationException
    {
        internal GeneratedAssertionCycleException(string schemaName, OpenApiSourceLocation? location = null)
            : base("The schema '" + schemaName + "' refers to itself without going into the value; it can never be evaluated.")
        {
            SchemaName = schemaName;
            Location = location;
        }

        /// <summary>The position of the schema that refers to itself (null when it is not known).</summary>
        internal OpenApiSourceLocation? Location { get; }

        internal string SchemaName { get; }
    }
}
