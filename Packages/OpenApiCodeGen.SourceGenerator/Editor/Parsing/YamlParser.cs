using System;
using System.Collections.Generic;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    /// <summary>
    /// Parses the supported YAML subset directly from <see cref="YamlToken"/> values.
    /// No parser operation reads or searches the original source text.
    /// </summary>
    internal sealed class YamlParser
    {
        private readonly IReadOnlyList<YamlToken> tokens;
        private readonly string sourcePath;
        private readonly Dictionary<int, int> lineIndents = new Dictionary<int, int>();
        private readonly Dictionary<string, SpecNode> anchors = new Dictionary<string, SpecNode>(StringComparer.Ordinal);
        private readonly HashSet<string> anchorsInProgress = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> aliasesInProgress = new HashSet<string>(StringComparer.Ordinal);
        private int position;
        private int aliasCount;
        private int expandedNodeCount;

        private YamlParser(IReadOnlyList<YamlToken> tokens, string sourcePath)
        {
            this.tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            this.sourcePath = sourcePath ?? string.Empty;
            for (int index = 0; index < tokens.Count; index++)
            {
                YamlToken token = tokens[index];
                if (token.Kind == YamlTokenKind.Indent)
                {
                    lineIndents[token.Line] = ParseIndent(token.Value);
                }
            }
        }

        internal static SpecNode Parse(string source, string sourcePath)
        {
            IReadOnlyList<YamlToken> tokens = YamlLexer.Tokenize(source, sourcePath);
            return new YamlParser(tokens, sourcePath).ParseDocument();
        }

        private SpecNode ParseDocument()
        {
            SkipTrivia();
            if (AtEnd)
            {
                throw Error(
                    YamlDiagnosticCode.InvalidDocument,
                    "A YAML document must contain a value.",
                    null,
                    1,
                    1,
                    0);
            }

            if (Current.Kind == YamlTokenKind.DocumentEnd)
            {
                throw Error(
                    YamlDiagnosticCode.InvalidDocument,
                    "A YAML document end marker cannot appear before a document value.",
                    Current);
            }

            if (Current.Kind == YamlTokenKind.DocumentStart)
            {
                YamlToken marker = Current;
                position++;
                ConsumeLineEnd();
                SkipTrivia();
                if (AtEnd || Current.Kind == YamlTokenKind.DocumentEnd)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidDocument,
                        "A YAML document must contain a value after '---'.",
                        marker);
                }
            }

            if (Current.Kind == YamlTokenKind.DocumentStart)
            {
                throw Error(
                    YamlDiagnosticCode.MultipleDocuments,
                    "Multiple YAML documents are not supported.",
                    Current);
            }

            int rootIndent = CurrentIndent();
            SpecNode root = ParseBlock(rootIndent, 0);
            SkipTrivia();
            if (!AtEnd && Current.Kind == YamlTokenKind.DocumentEnd)
            {
                position++;
                ConsumeLineEnd();
                SkipTrivia();
            }

            if (!AtEnd)
            {
                if (Current.Kind == YamlTokenKind.DocumentStart)
                {
                    throw Error(
                        YamlDiagnosticCode.MultipleDocuments,
                        "Multiple YAML documents are not supported.",
                        Current);
                }

                if (CurrentIndent() > rootIndent)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidIndentation,
                        "Content after the YAML document root has inconsistent indentation.",
                        Current);
                }

                throw Error(
                    YamlDiagnosticCode.InvalidDocument,
                    "Unexpected content follows the YAML document root.",
                    Current);
            }

            return root;
        }

        private SpecNode ParseBlock(int expectedIndent, int depth)
        {
            SkipTrivia();
            if (AtEnd)
            {
                throw Error(
                    YamlDiagnosticCode.InvalidDocument,
                    "A YAML value was expected.",
                    null,
                    1,
                    1,
                    0);
            }

            int actualIndent = CurrentIndent();
            YamlToken indentToken = Current;
            if (actualIndent < expectedIndent)
            {
                throw Error(
                    YamlDiagnosticCode.InvalidIndentation,
                    "A nested YAML value must be indented further than its parent.",
                    indentToken);
            }

            if (actualIndent != expectedIndent)
            {
                throw Error(
                    YamlDiagnosticCode.InvalidIndentation,
                    "A nested YAML value has inconsistent indentation.",
                    indentToken);
            }

            ConsumeIndentToken();
            if (AtEnd)
            {
                throw Error(
                    YamlDiagnosticCode.InvalidDocument,
                    "A YAML value was expected.",
                    indentToken);
            }

            YamlToken start = Current;
            EnsureDepth(depth, start);
            if (Current.Kind == YamlTokenKind.Dash)
            {
                return ParseSequence(expectedIndent, depth);
            }

            if (HasMappingColonOnLine(position))
            {
                return ParseMapping(expectedIndent, depth);
            }

            return ParseInlineNode(depth);
        }

        private SpecNode ParseMapping(int expectedIndent, int depth)
        {
            YamlToken first = Current;
            var properties = new List<SpecProperty>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                SkipTrivia();
                if (AtEnd || IsDocumentMarker(Current)) break;
                int indent = CurrentIndent();
                if (indent != expectedIndent)
                {
                    if (indent > expectedIndent)
                    {
                        throw Error(
                            YamlDiagnosticCode.InvalidIndentation,
                            "A mapping entry has inconsistent indentation.",
                            Current);
                    }

                    break;
                }

                if (Current.Kind == YamlTokenKind.Dash) break;
                if (!HasMappingColonOnLine(position)) break;

                ParseMappingEntry(expectedIndent, depth, properties, names);
            }

            return new SpecObjectNode(first.Line, first.Column, properties);
        }

        private void ParseMappingEntry(
            int mappingIndent,
            int depth,
            List<SpecProperty> properties,
            HashSet<string> names)
        {
            ConsumeIndentToken();
            if (AtEnd)
            {
                throw Error(
                    YamlDiagnosticCode.InvalidMapping,
                    "A mapping key and ':' were expected.",
                    null,
                    1,
                    1,
                    0);
            }

            int keyStart = position;
            int colonIndex = FindTopLevelColonToken(keyStart);
            if (colonIndex < 0)
            {
                throw Error(
                    YamlDiagnosticCode.InvalidMapping,
                    "A mapping key and ':' were expected.",
                    Current);
            }

            YamlToken keyToken = tokens[keyStart];
            if (keyToken.Kind != YamlTokenKind.Scalar || keyToken.Value.StartsWith("?", StringComparison.Ordinal))
            {
                throw Error(
                    YamlDiagnosticCode.ComplexKey,
                    "Only simple string YAML mapping keys are supported.",
                    keyToken);
            }

            for (int index = keyStart + 1; index < colonIndex; index++)
            {
                if (tokens[index].Kind != YamlTokenKind.Comment && tokens[index].Kind != YamlTokenKind.Indent)
                {
                    throw Error(
                        YamlDiagnosticCode.ComplexKey,
                        "Only simple string YAML mapping keys are supported.",
                        tokens[index]);
                }
            }

            string name = ResolveStringKey(keyToken);
            if (name == "<<")
            {
                throw Error(
                    YamlDiagnosticCode.InvalidMapping,
                    "The YAML merge key '<<' is not supported.",
                    keyToken);
            }

            if (!names.Add(name))
            {
                throw Error(
                    YamlDiagnosticCode.DuplicateKey,
                    "Duplicate YAML mapping key '" + name + "'.",
                    keyToken);
            }

            YamlToken colon = tokens[colonIndex];
            position = colonIndex + 1;
            SpecNode value;
            if (!AtEnd && Current.Line == keyToken.Line && Current.Kind != YamlTokenKind.Comment &&
                Current.Kind != YamlTokenKind.NewLine)
            {
                value = ParseInlineNode(depth + 1);
                EnsureOnlyLineTrivia(keyToken.Line, YamlDiagnosticCode.InvalidMapping);
                ConsumeLineEnd();
            }
            else
            {
                ConsumeLineEnd();
                SkipTrivia();
                if (!AtEnd && !IsDocumentMarker(Current) && CurrentIndent() > mappingIndent)
                {
                    value = ParseBlock(CurrentIndent(), depth + 1);
                }
                else
                {
                    value = new SpecNullNode(colon.Line, colon.Column + 1);
                }
            }

            properties.Add(new SpecProperty(name, keyToken.Line, keyToken.Column, value));
        }

        private SpecNode ParseSequence(int expectedIndent, int depth)
        {
            YamlToken first = Current;
            var items = new List<SpecNode>();
            while (true)
            {
                SkipTrivia();
                if (AtEnd) break;
                int lineIndent = CurrentIndent();
                YamlToken lineStart = Current;
                if (lineIndent > expectedIndent)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidIndentation,
                        "A sequence item has inconsistent indentation.",
                        lineStart);
                }

                ConsumeIndentToken();
                if (AtEnd || IsDocumentMarker(Current) || Current.Kind != YamlTokenKind.Dash ||
                    lineIndent != expectedIndent)
                {
                    break;
                }

                YamlToken dash = Current;
                int dashLine = dash.Line;
                position++;
                SpecNode value;
                if (!AtEnd && Current.Line == dashLine && Current.Kind != YamlTokenKind.Comment &&
                    Current.Kind != YamlTokenKind.NewLine)
                {
                    if (Current.Kind == YamlTokenKind.Dash)
                    {
                        value = ParseCompactSequence(expectedIndent, depth + 1);
                    }
                    else if (HasMappingColonOnLine(position))
                    {
                        value = ParseCompactMapping(expectedIndent, depth + 1, dashLine);
                    }
                    else
                    {
                        value = ParseInlineNode(depth + 1);
                    }

                    EnsureOnlyLineTrivia(dashLine, YamlDiagnosticCode.InvalidSequence);
                    ConsumeLineEnd();
                }
                else
                {
                    ConsumeLineEnd();
                    SkipTrivia();
                    if (!AtEnd && !IsDocumentMarker(Current) && CurrentIndent() > expectedIndent)
                    {
                        value = ParseBlock(CurrentIndent(), depth + 1);
                    }
                    else
                    {
                        value = new SpecNullNode(dash.Line, dash.Column + 1);
                    }
                }

                items.Add(value);
            }

            return new SpecArrayNode(first.Line, first.Column, items);
        }

        private SpecNode ParseCompactSequence(int parentIndent, int depth)
        {
            YamlToken first = Current;
            int nestedIndent = parentIndent + 2;
            var items = new List<SpecNode>();
            while (true)
            {
                SkipTrivia();
                if (AtEnd) break;
                int lineIndent = CurrentIndent();
                YamlToken lineStart = Current;
                if (Current.Line != first.Line && lineIndent > parentIndent && lineIndent != nestedIndent)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidIndentation,
                        "A nested sequence item has inconsistent indentation.",
                        lineStart);
                }

                ConsumeIndentToken();
                if (AtEnd || Current.Kind != YamlTokenKind.Dash)
                {
                    break;
                }

                if (Current.Line != first.Line && lineIndent != nestedIndent)
                {
                    break;
                }

                YamlToken dash = Current;
                int dashLine = dash.Line;
                position++;
                SpecNode value;
                if (!AtEnd && Current.Line == dashLine && Current.Kind != YamlTokenKind.Comment &&
                    Current.Kind != YamlTokenKind.NewLine)
                {
                    if (Current.Kind == YamlTokenKind.Dash)
                    {
                        value = ParseCompactSequence(nestedIndent, depth + 1);
                    }
                    else if (HasMappingColonOnLine(position))
                    {
                        value = ParseCompactMapping(nestedIndent, depth + 1, dashLine);
                    }
                    else
                    {
                        value = ParseInlineNode(depth + 1);
                    }

                    EnsureOnlyLineTrivia(dashLine, YamlDiagnosticCode.InvalidSequence);
                    ConsumeLineEnd();
                }
                else
                {
                    ConsumeLineEnd();
                    SkipTrivia();
                    if (!AtEnd && !IsDocumentMarker(Current) && CurrentIndent() > nestedIndent)
                    {
                        value = ParseBlock(CurrentIndent(), depth + 1);
                    }
                    else
                    {
                        value = new SpecNullNode(dash.Line, dash.Column + 1);
                    }
                }

                items.Add(value);
            }

            return new SpecArrayNode(first.Line, first.Column, items);
        }

        private SpecNode ParseCompactMapping(int sequenceIndent, int depth, int sequenceLine)
        {
            int mappingIndent = sequenceIndent + 2;
            var properties = new List<SpecProperty>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            YamlToken first = Current;
            ParseMappingEntry(mappingIndent, depth, properties, names);
            while (true)
            {
                SkipTrivia();
                if (AtEnd || IsDocumentMarker(Current))
                {
                    break;
                }

                int currentIndent = CurrentIndent();
                if (currentIndent <= sequenceIndent) break;
                if (currentIndent != mappingIndent)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidIndentation,
                        "A compact sequence mapping has inconsistent indentation.",
                        Current);
                }

                if (Current.Kind == YamlTokenKind.Dash) break;

                if (!HasMappingColonOnLine(position))
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidIndentation,
                        "A compact sequence mapping has inconsistent indentation.",
                        Current);
                }

                ParseMappingEntry(mappingIndent, depth, properties, names);
            }

            return new SpecObjectNode(sequenceLine, first.Column, properties);
        }

        private SpecNode ParseInlineNode(int depth, bool inFlow = false)
        {
            if (AtEnd)
            {
                throw Error(
                    YamlDiagnosticCode.InvalidScalar,
                    "A YAML value was expected.",
                    null,
                    1,
                    1,
                    0);
            }

            YamlToken token = Current;
            EnsureDepth(depth, token);
            switch (token.Kind)
            {
                case YamlTokenKind.Scalar:
                    position++;
                    return YamlScalarResolver.Resolve(token.Value, sourcePath, token.Line, token.Column, token.Offset);
                case YamlTokenKind.BlockScalarHeader:
                    position++;
                    return ParseBlockScalar(token, GetBlockScalarParentIndent(token), depth);
                case YamlTokenKind.Anchor:
                    return ParseAnchor(depth, inFlow);
                case YamlTokenKind.Alias:
                    position++;
                    return ExpandAlias(token);
                case YamlTokenKind.FlowSequenceStart:
                case YamlTokenKind.FlowMappingStart:
                    return ParseFlow(depth);
                default:
                    throw Error(
                        YamlDiagnosticCode.InvalidScalar,
                        "Unexpected YAML token while parsing a value.",
                        token);
            }
        }

        private SpecNode ParseAnchor(int depth, bool inFlow)
        {
            YamlToken anchorToken = Current;
            string name = anchorToken.Value.Substring(1);
            if (anchors.ContainsKey(name) || !anchorsInProgress.Add(name))
            {
                throw Error(
                    YamlDiagnosticCode.AnchorRedefinition,
                    "Anchor '" + name + "' is already defined.",
                    anchorToken);
            }

            position++;
            SpecNode anchored;
            if (!AtEnd && Current.Line == anchorToken.Line && Current.Kind != YamlTokenKind.Comment &&
                Current.Kind != YamlTokenKind.NewLine)
            {
                anchored = ParseInlineNode(depth + 1, inFlow);
                if (!(inFlow && !AtEnd && IsFlowSeparator(Current)))
                {
                    EnsureOnlyLineTrivia(anchorToken.Line, YamlDiagnosticCode.InvalidMapping);
                    ConsumeLineEnd();
                }
            }
            else
            {
                ConsumeLineEnd();
                SkipTrivia();
                if (AtEnd || IsDocumentMarker(Current) || CurrentIndent() <= CurrentIndentForLine(anchorToken.Line))
                {
                    anchorsInProgress.Remove(name);
                    throw Error(
                        YamlDiagnosticCode.InvalidMapping,
                        "An anchor without an inline value must be followed by a nested value.",
                        anchorToken);
                }

                anchored = ParseBlock(CurrentIndent(), depth + 1);
            }

            anchorsInProgress.Remove(name);
            if (anchors.Count >= YamlParserLimits.MaximumAnchors)
            {
                throw Error(
                    YamlDiagnosticCode.LimitExceeded,
                    "The YAML anchor count exceeds the supported maximum.",
                    anchorToken);
            }

            anchors.Add(name, anchored);
            return anchored;
        }

        private SpecNode ExpandAlias(YamlToken aliasToken)
        {
            string name = aliasToken.Value.Substring(1);
            if (++aliasCount > YamlParserLimits.MaximumAliases)
            {
                throw Error(
                    YamlDiagnosticCode.LimitExceeded,
                    "The YAML alias count exceeds the supported maximum.",
                    aliasToken);
            }

            if (anchorsInProgress.Contains(name) || aliasesInProgress.Contains(name))
            {
                throw Error(
                    YamlDiagnosticCode.AliasCycle,
                    "The YAML alias graph contains a cycle involving '" + name + "'.",
                    aliasToken);
            }

            SpecNode anchor;
            if (!anchors.TryGetValue(name, out anchor))
            {
                throw Error(
                    YamlDiagnosticCode.UndefinedAlias,
                    "YAML alias '" + name + "' refers to an undefined anchor.",
                    aliasToken);
            }

            aliasesInProgress.Add(name);
            SpecNode expanded = CloneWithRootLocation(anchor, aliasToken.Line, aliasToken.Column);
            aliasesInProgress.Remove(name);
            return expanded;
        }

        private SpecNode CloneWithRootLocation(SpecNode node, int line, int column)
        {
            if (++expandedNodeCount > YamlParserLimits.MaximumExpandedNodes)
            {
                throw Error(
                    YamlDiagnosticCode.LimitExceeded,
                    "The expanded YAML node count exceeds the supported maximum.",
                    null,
                    line,
                    column,
                    0);
            }

            SpecObjectNode objectNode = node as SpecObjectNode;
            if (objectNode != null)
            {
                var properties = new List<SpecProperty>(objectNode.Properties.Count);
                for (int index = 0; index < objectNode.Properties.Count; index++)
                {
                    SpecProperty property = objectNode.Properties[index];
                    properties.Add(new SpecProperty(
                        property.Name,
                        property.Line,
                        property.Column,
                        CloneWithRootLocation(property.Value, property.Value.Line, property.Value.Column)));
                }

                return new SpecObjectNode(line, column, properties);
            }

            SpecArrayNode arrayNode = node as SpecArrayNode;
            if (arrayNode != null)
            {
                var items = new List<SpecNode>(arrayNode.Items.Count);
                for (int index = 0; index < arrayNode.Items.Count; index++)
                {
                    SpecNode item = arrayNode.Items[index];
                    items.Add(CloneWithRootLocation(item, item.Line, item.Column));
                }

                return new SpecArrayNode(line, column, items);
            }

            SpecStringNode stringNode = node as SpecStringNode;
            if (stringNode != null) return new SpecStringNode(line, column, stringNode.Value);
            SpecNumberNode numberNode = node as SpecNumberNode;
            if (numberNode != null) return new SpecNumberNode(line, column, numberNode.IsInteger, numberNode.Value);
            SpecBooleanNode booleanNode = node as SpecBooleanNode;
            if (booleanNode != null) return new SpecBooleanNode(line, column, booleanNode.Value);
            return new SpecNullNode(line, column);
        }

        private SpecNode ParseFlow(int depth)
        {
            if (Current.Kind == YamlTokenKind.FlowSequenceStart)
            {
                return ParseFlowSequence(depth);
            }

            if (Current.Kind == YamlTokenKind.FlowMappingStart)
            {
                return ParseFlowMapping(depth);
            }

            throw Error(YamlDiagnosticCode.InvalidScalar, "A flow collection was expected.", Current);
        }

        private SpecNode ParseFlowSequence(int depth)
        {
            YamlToken start = Current;
            EnsureDepth(depth, start);
            position++;
            var items = new List<SpecNode>();
            SkipFlowTrivia();
            if (AtEnd)
            {
                throw Error(YamlDiagnosticCode.InvalidSequence, "A flow sequence is not terminated.", start);
            }

            if (Current.Kind == YamlTokenKind.FlowSequenceEnd)
            {
                position++;
                return new SpecArrayNode(start.Line, start.Column, items);
            }

            while (true)
            {
                items.Add(ParseFlowValue(depth + 1));
                SkipFlowTrivia();
                if (AtEnd)
                {
                    throw Error(YamlDiagnosticCode.InvalidSequence, "A flow sequence is not terminated.", start);
                }

                if (Current.Kind == YamlTokenKind.FlowSequenceEnd)
                {
                    position++;
                    return new SpecArrayNode(start.Line, start.Column, items);
                }

                if (Current.Kind != YamlTokenKind.Comma)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidSequence,
                        "A comma was expected in a flow sequence.",
                        Current);
                }

                position++;
                SkipFlowTrivia();
                if (AtEnd || Current.Kind == YamlTokenKind.FlowSequenceEnd)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidSequence,
                        "Trailing commas are not allowed in flow sequences.",
                        CurrentOr(start));
                }
            }
        }

        private SpecNode ParseFlowMapping(int depth)
        {
            YamlToken start = Current;
            EnsureDepth(depth, start);
            position++;
            var properties = new List<SpecProperty>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            SkipFlowTrivia();
            if (AtEnd)
            {
                throw Error(YamlDiagnosticCode.InvalidMapping, "A flow mapping is not terminated.", start);
            }

            if (Current.Kind == YamlTokenKind.FlowMappingEnd)
            {
                position++;
                return new SpecObjectNode(start.Line, start.Column, properties);
            }

            while (true)
            {
                YamlToken keyToken = Current;
                if (keyToken.Kind != YamlTokenKind.Scalar || keyToken.Value.StartsWith("?", StringComparison.Ordinal))
                {
                    throw Error(
                        YamlDiagnosticCode.ComplexKey,
                        "Only simple string YAML mapping keys are supported.",
                        keyToken);
                }

                position++;
                SkipFlowTrivia();
                if (AtEnd || Current.Kind != YamlTokenKind.Colon)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidMapping,
                        "A key and ':' were expected in a flow mapping.",
                        CurrentOr(keyToken));
                }

                position++;
                string name = ResolveStringKey(keyToken);
                if (name == "<<")
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidMapping,
                        "The YAML merge key '<<' is not supported.",
                        keyToken);
                }

                if (!names.Add(name))
                {
                    throw Error(
                        YamlDiagnosticCode.DuplicateKey,
                        "Duplicate YAML mapping key '" + name + "'.",
                        keyToken);
                }

                SkipFlowTrivia();
                if (AtEnd || Current.Kind == YamlTokenKind.Comma || Current.Kind == YamlTokenKind.FlowMappingEnd)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidMapping,
                        "A flow mapping value was expected.",
                        CurrentOr(keyToken));
                }

                SpecNode value = ParseFlowValue(depth + 1);
                properties.Add(new SpecProperty(name, keyToken.Line, keyToken.Column, value));
                SkipFlowTrivia();
                if (AtEnd)
                {
                    throw Error(YamlDiagnosticCode.InvalidMapping, "A flow mapping is not terminated.", start);
                }

                if (Current.Kind == YamlTokenKind.FlowMappingEnd)
                {
                    position++;
                    return new SpecObjectNode(start.Line, start.Column, properties);
                }

                if (Current.Kind != YamlTokenKind.Comma)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidMapping,
                        "A comma was expected in a flow mapping.",
                        Current);
                }

                position++;
                SkipFlowTrivia();
                if (AtEnd || Current.Kind == YamlTokenKind.FlowMappingEnd)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidMapping,
                        "Trailing commas are not allowed in flow mappings.",
                        CurrentOr(start));
                }
            }
        }

        private SpecNode ParseFlowValue(int depth)
        {
            SkipFlowTrivia();
            if (AtEnd)
            {
                throw Error(
                    YamlDiagnosticCode.InvalidScalar,
                    "A flow value was expected.",
                    null,
                    1,
                    1,
                    0);
            }

            return ParseInlineNode(depth, true);
        }

        private SpecNode ParseBlockScalar(YamlToken header, int parentIndent, int depth)
        {
            EnsureDepth(depth, header);
            char style = header.Value[0];
            bool strip = header.Value.IndexOf('-', 1) >= 0;
            bool keep = header.Value.IndexOf('+', 1) >= 0;
            int explicitIndent = 0;
            for (int index = 1; index < header.Value.Length; index++)
            {
                char indicator = header.Value[index];
                if (indicator >= '1' && indicator <= '9')
                {
                    explicitIndent = indicator - '0';
                }
                else if (indicator != '+' && indicator != '-')
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidScalar,
                        "The block scalar header contains an invalid indicator.",
                        header);
                }
            }

            var rawLines = new List<string>();
            SkipBlockScalarHeaderLine();
            while (!AtEnd)
            {
                if (Current.Kind != YamlTokenKind.BlockScalarText)
                {
                    break;
                }

                rawLines.Add(Current.Value);
                position++;
                if (!AtEnd && Current.Kind == YamlTokenKind.NewLine)
                {
                    position++;
                }
            }

            int contentIndent = explicitIndent == 0 ? int.MaxValue : parentIndent + explicitIndent;
            if (contentIndent == int.MaxValue)
            {
                for (int index = 0; index < rawLines.Count; index++)
                {
                    string raw = rawLines[index];
                    int indent = CountLeadingSpaces(raw);
                    if (indent < raw.Length)
                    {
                        contentIndent = indent;
                        break;
                    }
                }

                if (contentIndent == int.MaxValue) contentIndent = parentIndent + 1;
            }

            var contentLines = new List<string>(rawLines.Count);
            var moreIndentedLines = new List<bool>(rawLines.Count);
            for (int index = 0; index < rawLines.Count; index++)
            {
                string raw = rawLines[index];
                int indent = CountLeadingSpaces(raw);
                if (indent < raw.Length && indent < contentIndent)
                {
                    throw Error(
                        YamlDiagnosticCode.InvalidIndentation,
                        "A block scalar line is less indented than its content indentation.",
                        header);
                }

                if (indent == raw.Length)
                {
                    contentLines.Add(string.Empty);
                    moreIndentedLines.Add(false);
                }
                else
                {
                    int remove = Math.Min(contentIndent, raw.Length);
                    contentLines.Add(raw.Substring(remove));
                    moreIndentedLines.Add(indent > contentIndent);
                }
            }

            string value = style == '|'
                ? string.Join("\n", contentLines)
                : FoldBlockLines(contentLines, moreIndentedLines);
            if (contentLines.Count > 0) value += "\n";
            if (strip)
            {
                value = value.TrimEnd('\n');
            }
            else if (!keep)
            {
                value = value.TrimEnd('\n') + (contentLines.Count == 0 ? string.Empty : "\n");
            }

            if (value.Length > YamlParserLimits.MaximumScalarCharacters)
            {
                throw Error(
                    YamlDiagnosticCode.LimitExceeded,
                    "The YAML scalar exceeds the supported maximum size.",
                    header);
            }

            return new SpecStringNode(header.Line, header.Column, value);
        }

        private int GetBlockScalarParentIndent(YamlToken header)
        {
            int parentIndent = CurrentIndentForLine(header.Line);
            int sequenceDashCount = 0;
            bool hasMappingColon = false;
            int index = position - 2;
            while (index >= 0 && tokens[index].Line == header.Line)
            {
                if (tokens[index].Kind == YamlTokenKind.Dash)
                {
                    sequenceDashCount++;
                }
                else if (tokens[index].Kind == YamlTokenKind.Colon)
                {
                    hasMappingColon = true;
                }

                index--;
            }

            return parentIndent + (hasMappingColon ? sequenceDashCount * 2 : 0);
        }

        private void SkipBlockScalarHeaderLine()
        {
            while (!AtEnd && Current.Line == tokens[Math.Max(0, position - 1)].Line && Current.Kind == YamlTokenKind.Comment)
            {
                position++;
            }

            if (!AtEnd && Current.Kind == YamlTokenKind.NewLine)
            {
                position++;
            }
        }

        private static string FoldBlockLines(IReadOnlyList<string> lines, IReadOnlyList<bool> moreIndentedLines)
        {
            var builder = new StringBuilder();
            for (int index = 0; index < lines.Count; index++)
            {
                if (index > 0)
                {
                    bool previousBlank = lines[index - 1].Length == 0;
                    bool currentBlank = lines[index].Length == 0;
                    bool previousMoreIndented = moreIndentedLines[index - 1];
                    bool currentMoreIndented = moreIndentedLines[index];
                    builder.Append(
                        previousBlank || currentBlank || previousMoreIndented || currentMoreIndented
                            ? '\n'
                            : ' ');
                }

                builder.Append(lines[index]);
            }

            return builder.ToString();
        }

        private string ResolveStringKey(YamlToken token)
        {
            string value;
            if (!YamlScalarResolver.TryGetString(
                    token.Value,
                    sourcePath,
                    token.Line,
                    token.Column,
                    token.Offset,
                    out value))
            {
                throw Error(
                    YamlDiagnosticCode.ComplexKey,
                    "YAML mapping keys must be strings.",
                    token);
            }

            return value;
        }

        private void EnsureOnlyLineTrivia(int line, YamlDiagnosticCode code)
        {
            if (!AtEnd && Current.Line == line && Current.Kind != YamlTokenKind.Comment &&
                Current.Kind != YamlTokenKind.NewLine)
            {
                throw Error(code, "Unexpected content follows the YAML value.", Current);
            }
        }

        private static bool IsFlowSeparator(YamlToken token)
        {
            return token.Kind == YamlTokenKind.Comma || token.Kind == YamlTokenKind.FlowMappingEnd ||
                   token.Kind == YamlTokenKind.FlowSequenceEnd;
        }

        private bool HasMappingColonOnLine(int start)
        {
            return FindTopLevelColonToken(start) >= 0;
        }

        private int FindTopLevelColonToken(int start)
        {
            int flowDepth = 0;
            for (int index = start; index < tokens.Count; index++)
            {
                YamlTokenKind kind = tokens[index].Kind;
                if (kind == YamlTokenKind.NewLine || kind == YamlTokenKind.EndOfInput) break;
                if (kind == YamlTokenKind.Comment) break;
                if (kind == YamlTokenKind.FlowSequenceStart || kind == YamlTokenKind.FlowMappingStart)
                {
                    flowDepth++;
                }
                else if (kind == YamlTokenKind.FlowSequenceEnd || kind == YamlTokenKind.FlowMappingEnd)
                {
                    flowDepth--;
                }
                else if (kind == YamlTokenKind.Colon && flowDepth == 0)
                {
                    return index;
                }
            }

            return -1;
        }

        private void SkipTrivia()
        {
            while (!AtEnd)
            {
                if (Current.Kind == YamlTokenKind.Comment || Current.Kind == YamlTokenKind.NewLine)
                {
                    position++;
                    continue;
                }

                // An indentation token followed immediately by a comment or line break belongs to a
                // blank/comment-only line, not to the next YAML node.
                if (Current.Kind == YamlTokenKind.Indent && position + 1 < tokens.Count &&
                    (tokens[position + 1].Kind == YamlTokenKind.Comment ||
                     tokens[position + 1].Kind == YamlTokenKind.NewLine))
                {
                    position++;
                    continue;
                }

                break;
            }
        }

        private void SkipFlowTrivia()
        {
            while (!AtEnd && (Current.Kind == YamlTokenKind.Comment || Current.Kind == YamlTokenKind.NewLine ||
                               Current.Kind == YamlTokenKind.Indent))
            {
                position++;
            }
        }

        private void ConsumeLineEnd()
        {
            while (!AtEnd && Current.Kind == YamlTokenKind.Comment)
            {
                position++;
            }

            if (!AtEnd && Current.Kind == YamlTokenKind.NewLine)
            {
                position++;
            }
        }

        private void ConsumeIndentToken()
        {
            if (!AtEnd && Current.Kind == YamlTokenKind.Indent)
            {
                position++;
            }
        }

        private int CurrentIndent()
        {
            return AtEnd ? 0 : CurrentIndentForLine(Current.Line);
        }

        private int CurrentIndentForLine(int line)
        {
            int indent;
            return lineIndents.TryGetValue(line, out indent) ? indent : 0;
        }

        private static int ParseIndent(string value)
        {
            int indent;
            return int.TryParse(value, out indent) ? indent : 0;
        }

        private static int CountLeadingSpaces(string value)
        {
            int count = 0;
            while (count < value.Length && value[count] == ' ') count++;
            return count;
        }

        private static bool IsDocumentMarker(YamlToken token)
        {
            return token.Kind == YamlTokenKind.DocumentStart || token.Kind == YamlTokenKind.DocumentEnd;
        }

        private void EnsureDepth(int depth, YamlToken token)
        {
            if (depth > YamlParserLimits.MaximumDepth)
            {
                throw Error(
                    YamlDiagnosticCode.LimitExceeded,
                    "The YAML nesting depth exceeds the supported maximum.",
                    token);
            }
        }

        private YamlToken CurrentOr(YamlToken fallback)
        {
            return AtEnd ? fallback : Current;
        }

        private YamlParseException Error(
            YamlDiagnosticCode code,
            string message,
            YamlToken token,
            int fallbackLine = 1,
            int fallbackColumn = 1,
            int fallbackOffset = 0)
        {
            if (token != null)
            {
                return new YamlParseException(code, message, sourcePath, token.Line, token.Column, token.Offset);
            }

            return new YamlParseException(code, message, sourcePath, fallbackLine, fallbackColumn, fallbackOffset);
        }

        private bool AtEnd => position >= tokens.Count || Current.Kind == YamlTokenKind.EndOfInput;

        private YamlToken Current => tokens[position];
    }
}
