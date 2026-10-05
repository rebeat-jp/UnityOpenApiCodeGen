using System;
using System.Collections.Generic;
using System.Globalization;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    /// <summary>
    /// Lexes the small YAML 1.2 subset accepted by the source generator.
    /// The parser consumes this stream and never re-scans the source text.
    /// </summary>
    internal sealed class YamlLexer
    {
        private readonly string source;
        private readonly string sourcePath;
        private readonly List<YamlToken> tokens = new List<YamlToken>();
        private readonly List<FlowFrame> flowFrames = new List<FlowFrame>();
        private int index;
        private int line = 1;
        private bool blockScalarPending;
        private int blockScalarParentIndent;
        private int blockScalarContentIndent = -1;
        private int lineSequenceDashCount;
        private bool lineHasBlockMappingColon;
        private bool hasLastLineToken;
        private YamlTokenKind lastLineTokenKind;

        private enum FlowFrameKind
        {
            Sequence,
            Mapping
        }

        private enum FlowFrameState
        {
            SequenceValue,
            MappingKey,
            MappingValue
        }

        private sealed class FlowFrame
        {
            internal FlowFrame(FlowFrameKind kind)
            {
                Kind = kind;
                State = kind == FlowFrameKind.Mapping
                    ? FlowFrameState.MappingKey
                    : FlowFrameState.SequenceValue;
            }

            internal FlowFrameKind Kind { get; }

            internal FlowFrameState State { get; set; }
        }

        private YamlLexer(string source, string sourcePath)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.sourcePath = sourcePath ?? string.Empty;
            if (source.Length > YamlParserLimits.MaximumInputCharacters)
            {
                throw CreateExceptionAt(
                    YamlDiagnosticCode.LimitExceeded,
                    "The YAML input exceeds the maximum supported size.",
                    1,
                    1,
                    0);
            }
        }

        internal static IReadOnlyList<YamlToken> Tokenize(string source, string sourcePath)
        {
            return new YamlLexer(source, sourcePath).Lex();
        }

        internal IReadOnlyList<YamlToken> Lex()
        {
            // The BOM is part of the UTF-16 offset space, but not part of the first line's column space.
            if (source.Length > 0 && source[0] == '\uFEFF')
            {
                index = 1;
            }

            while (index < source.Length)
            {
                int lineStart = index;
                int lineEnd = FindLineEnd(lineStart);
                if (blockScalarPending && ScanBlockScalarLine(lineStart, lineEnd))
                {
                    continue;
                }

                ScanNormalLine(lineStart, lineEnd);
            }

            int currentLineStart = FindCurrentLineStart();
            AddToken(
                YamlTokenKind.EndOfInput,
                string.Empty,
                source.Length,
                line,
                source.Length - currentLineStart + 1,
                0);
            return tokens;
        }

        private bool ScanBlockScalarLine(int lineStart, int lineEnd)
        {
            int indent = CountSpaces(lineStart, lineEnd);
            bool blank = indent == lineEnd - lineStart;
            if (!blank)
            {
                if (blockScalarContentIndent < 0)
                {
                    if (indent <= blockScalarParentIndent)
                    {
                        StopBlockScalarPending();
                        return false;
                    }

                    blockScalarContentIndent = indent;
                }
                else if (indent < blockScalarContentIndent)
                {
                    StopBlockScalarPending();
                    return false;
                }
            }

            // Keep the complete physical line (including its indentation) in one token. The line break
            // is a separate token, so token spans remain monotonic and non-overlapping.
            AddToken(
                YamlTokenKind.BlockScalarText,
                source.Substring(lineStart, lineEnd - lineStart),
                lineStart,
                line,
                1,
                lineEnd - lineStart);
            ConsumeLineEnd(lineEnd, lineStart);
            return true;
        }

        private void ScanNormalLine(int lineStart, int lineEnd)
        {
            lineSequenceDashCount = 0;
            lineHasBlockMappingColon = false;
            hasLastLineToken = false;
            int contentStart = lineStart;
            while (contentStart < lineEnd && (source[contentStart] == ' ' || source[contentStart] == '\t'))
            {
                if (source[contentStart] == '\t')
                {
                    throw CreateExceptionAt(
                        YamlDiagnosticCode.InvalidIndentation,
                        "Tabs are not allowed in YAML indentation.",
                        line,
                        contentStart - lineStart + 1,
                        contentStart);
                }

                contentStart++;
            }

            int indent = contentStart - lineStart;
            if (indent > 0)
            {
                AddToken(
                    YamlTokenKind.Indent,
                    indent.ToString(CultureInfo.InvariantCulture),
                    lineStart,
                    line,
                    1,
                    indent);
            }

            if (contentStart == lineEnd)
            {
                ConsumeLineEnd(lineEnd, lineStart);
                return;
            }

            if (source[contentStart] == '#')
            {
                AddToken(
                    YamlTokenKind.Comment,
                    source.Substring(contentStart, lineEnd - contentStart),
                    contentStart,
                    line,
                    contentStart - lineStart + 1,
                    lineEnd - contentStart);
                ConsumeLineEnd(lineEnd, lineStart);
                return;
            }

            if (indent == 0 && flowFrames.Count == 0 && IsDocumentMarker(contentStart, lineEnd, "---"))
            {
                AddToken(YamlTokenKind.DocumentStart, "---", contentStart, line, contentStart - lineStart + 1, 3);
                ScanTrailingComment(contentStart + 3, lineEnd, lineStart);
                ConsumeLineEnd(lineEnd, lineStart);
                return;
            }

            if (indent == 0 && flowFrames.Count == 0 && IsDocumentMarker(contentStart, lineEnd, "..."))
            {
                AddToken(YamlTokenKind.DocumentEnd, "...", contentStart, line, contentStart - lineStart + 1, 3);
                ScanTrailingComment(contentStart + 3, lineEnd, lineStart);
                ConsumeLineEnd(lineEnd, lineStart);
                return;
            }

            if (source[contentStart] == '%')
            {
                throw CreateExceptionAt(
                    YamlDiagnosticCode.UnsupportedDirective,
                    "YAML directives are not supported.",
                    line,
                    contentStart - lineStart + 1,
                    contentStart);
            }

            ScanContent(contentStart, lineEnd, lineStart, indent);
            ConsumeLineEnd(lineEnd, lineStart);
        }

        private void ScanContent(int contentStart, int lineEnd, int lineStart, int indent)
        {
            int cursor = contentStart;
            while (cursor < lineEnd)
            {
                while (cursor < lineEnd && (source[cursor] == ' ' || source[cursor] == '\t'))
                {
                    cursor++;
                }

                if (cursor >= lineEnd)
                {
                    break;
                }

                char current = source[cursor];
                if (current == '#' && (cursor == contentStart || IsWhitespace(source[cursor - 1])))
                {
                    AddToken(
                        YamlTokenKind.Comment,
                        source.Substring(cursor, lineEnd - cursor),
                        cursor,
                        line,
                        cursor - lineStart + 1,
                        lineEnd - cursor);
                    break;
                }

                if (current == '!' && IsTokenStart(cursor, contentStart))
                {
                    throw CreateExceptionAt(
                        YamlDiagnosticCode.UnsupportedTag,
                        "Explicit and custom YAML tags are not supported.",
                        line,
                        cursor - lineStart + 1,
                        cursor);
                }

                if (current == '\'' || current == '"')
                {
                    cursor = ScanQuoted(cursor, lineEnd, lineStart);
                    continue;
                }

                if (current == '&' || current == '*')
                {
                    YamlTokenKind anchorKind = current == '&' ? YamlTokenKind.Anchor : YamlTokenKind.Alias;
                    int start = cursor++;
                    while (cursor < lineEnd && IsAnchorCharacter(source[cursor]))
                    {
                        cursor++;
                    }

                    if (cursor == start + 1)
                    {
                        throw CreateExceptionAt(
                            YamlDiagnosticCode.InvalidScalar,
                            "An anchor or alias must contain a name.",
                            line,
                            start - lineStart + 1,
                            start);
                    }

                    AddToken(
                        anchorKind,
                        source.Substring(start, cursor - start),
                        start,
                        line,
                        start - lineStart + 1,
                        cursor - start);
                    continue;
                }

                YamlTokenKind punctuation;
                switch (current)
                {
                    case '{':
                        punctuation = IsFlowCollectionStart(cursor, contentStart, false)
                            ? YamlTokenKind.FlowMappingStart
                            : YamlTokenKind.Scalar;
                        break;
                    case '}':
                        punctuation = flowFrames.Count != 0
                            ? YamlTokenKind.FlowMappingEnd
                            : YamlTokenKind.Scalar;
                        break;
                    case '[':
                        punctuation = IsFlowCollectionStart(cursor, contentStart, false)
                            ? YamlTokenKind.FlowSequenceStart
                            : YamlTokenKind.Scalar;
                        break;
                    case ']':
                        punctuation = flowFrames.Count != 0
                            ? YamlTokenKind.FlowSequenceEnd
                            : YamlTokenKind.Scalar;
                        break;
                    case ',':
                        punctuation = flowFrames.Count != 0
                            ? YamlTokenKind.Comma
                            : YamlTokenKind.Scalar;
                        break;
                    case '-':
                        punctuation = IsDashToken(cursor, contentStart, lineEnd)
                            ? YamlTokenKind.Dash
                            : YamlTokenKind.Scalar;
                        break;
                    case ':':
                        punctuation = IsFlowMappingSeparator(cursor, lineEnd) ||
                                      (flowFrames.Count == 0 && IsMappingColon(cursor, lineEnd))
                            ? YamlTokenKind.Colon
                            : YamlTokenKind.Scalar;
                        break;
                    default: punctuation = YamlTokenKind.Scalar; break;
                }

                if (punctuation != YamlTokenKind.Scalar)
                {
                    AddToken(
                        punctuation,
                        current.ToString(),
                        cursor,
                        line,
                        cursor - lineStart + 1,
                        1);
                    UpdateFlowState(punctuation, cursor, lineStart);
                    cursor++;
                    continue;
                }

                int startPlain = cursor;
                bool plainHasContent = false;
                while (cursor < lineEnd)
                {
                    char character = source[cursor];
                    if (character == '#' && (cursor == startPlain || IsWhitespace(source[cursor - 1]))) break;
                    if ((character == '{' || character == '[') &&
                        IsFlowCollectionStart(cursor, contentStart, plainHasContent))
                    {
                        break;
                    }

                    if ((character == ',' || character == '}' || character == ']') && flowFrames.Count != 0)
                    {
                        break;
                    }

                    if ((character == ':' && (IsFlowMappingSeparator(cursor, lineEnd) ||
                                              (flowFrames.Count == 0 && IsMappingColon(cursor, lineEnd)))) ||
                        (character == '-' && !plainHasContent && IsDashToken(cursor, contentStart, lineEnd)))
                    {
                        break;
                    }

                    plainHasContent = true;
                    cursor++;
                }

                int valueEnd = cursor;
                while (valueEnd > startPlain && IsWhitespace(source[valueEnd - 1])) valueEnd--;
                if (valueEnd == startPlain)
                {
                    // This can only occur for malformed punctuation. Advance to avoid an infinite loop;
                    // the parser will report the structural error using the punctuation token.
                    cursor++;
                    continue;
                }

                string value = source.Substring(startPlain, valueEnd - startPlain);
                YamlTokenKind tokenKind = YamlTokenKind.Scalar;
                if (value.Length > 0 && (value[0] == '|' || value[0] == '>'))
                {
                    if (!IsBlockScalarHeader(value))
                    {
                        throw CreateExceptionAt(
                            YamlDiagnosticCode.InvalidScalar,
                            "The block scalar header contains invalid indicators.",
                            line,
                            startPlain - lineStart + 1,
                            startPlain);
                    }

                    tokenKind = YamlTokenKind.BlockScalarHeader;
                }

                if (tokenKind == YamlTokenKind.BlockScalarHeader && flowFrames.Count != 0)
                {
                    throw CreateExceptionAt(
                        YamlDiagnosticCode.InvalidScalar,
                        "Block scalars are not supported inside flow collections.",
                        line,
                        startPlain - lineStart + 1,
                        startPlain);
                }

                AddToken(
                    tokenKind,
                    value,
                    startPlain,
                    line,
                    startPlain - lineStart + 1,
                    value.Length);
                if (tokenKind == YamlTokenKind.BlockScalarHeader)
                {
                    blockScalarPending = true;
                    blockScalarParentIndent = indent +
                                               (lineHasBlockMappingColon ? lineSequenceDashCount * 2 : 0);
                    int explicitIndent = GetExplicitBlockScalarIndent(value);
                    blockScalarContentIndent = explicitIndent == 0
                        ? -1
                        : blockScalarParentIndent + explicitIndent;
                }
            }
        }

        private int ScanQuoted(int start, int lineEnd, int lineStart)
        {
            char quote = source[start];
            int cursor = start + 1;
            while (cursor < lineEnd)
            {
                if (source[cursor] == quote)
                {
                    cursor++;
                    if (quote == '\'' && cursor < lineEnd && source[cursor] == '\'')
                    {
                        cursor++;
                        continue;
                    }

                    AddToken(
                        YamlTokenKind.Scalar,
                        source.Substring(start, cursor - start),
                        start,
                        line,
                        start - lineStart + 1,
                        cursor - start);
                    return cursor;
                }

                if (quote == '"' && source[cursor] == '\\')
                {
                    cursor++;
                    if (cursor >= lineEnd)
                    {
                        throw CreateExceptionAt(
                            YamlDiagnosticCode.InvalidScalar,
                            "A double-quoted YAML escape sequence is incomplete.",
                            line,
                            start - lineStart + 1,
                            start);
                    }

                    if (source[cursor] == 'u')
                    {
                        cursor++;
                        for (int offset = 0; offset < 4; offset++)
                        {
                            if (cursor >= lineEnd || !IsHexDigit(source[cursor]))
                            {
                                throw CreateExceptionAt(
                                    YamlDiagnosticCode.InvalidScalar,
                                    "A YAML Unicode escape must contain four hexadecimal digits.",
                                    line,
                                    start - lineStart + 1,
                                    start);
                            }

                            cursor++;
                        }
                    }
                    else
                    {
                        cursor++;
                    }

                    continue;
                }

                cursor++;
            }

            throw CreateExceptionAt(
                YamlDiagnosticCode.InvalidScalar,
                "A quoted YAML scalar is not terminated.",
                line,
                start - lineStart + 1,
                start);
        }

        private void ScanTrailingComment(int start, int lineEnd, int lineStart)
        {
            int cursor = start;
            while (cursor < lineEnd && IsWhitespace(source[cursor])) cursor++;
            if (cursor < lineEnd && source[cursor] == '#')
            {
                AddToken(
                    YamlTokenKind.Comment,
                    source.Substring(cursor, lineEnd - cursor),
                    cursor,
                    line,
                    cursor - lineStart + 1,
                    lineEnd - cursor);
            }
            else if (cursor < lineEnd)
            {
                throw CreateExceptionAt(
                    YamlDiagnosticCode.InvalidDocument,
                    "Unexpected content follows a YAML document marker.",
                    line,
                    cursor - lineStart + 1,
                    cursor);
            }
        }

        private int FindLineEnd(int start)
        {
            int cursor = start;
            while (cursor < source.Length && source[cursor] != '\r' && source[cursor] != '\n') cursor++;
            return cursor;
        }

        private int FindCurrentLineStart()
        {
            int cursor = source.Length - 1;
            while (cursor >= 0 && source[cursor] != '\r' && source[cursor] != '\n') cursor--;
            int start = cursor + 1;
            if (start == 0 && source.Length > 0 && source[0] == '\uFEFF') start = 1;
            return start;
        }

        private int GetNewlineLength(int lineEnd)
        {
            if (lineEnd >= source.Length) return 0;
            if (source[lineEnd] == '\r' && lineEnd + 1 < source.Length && source[lineEnd + 1] == '\n') return 2;
            return 1;
        }

        private void ConsumeLineEnd(int lineEnd, int lineStart)
        {
            int newlineLength = GetNewlineLength(lineEnd);
            if (newlineLength > 0)
            {
                AddToken(
                    YamlTokenKind.NewLine,
                    "\n",
                    lineEnd,
                    line,
                    lineEnd - lineStart + 1,
                    newlineLength);
                index = lineEnd + newlineLength;
                line++;
            }
            else
            {
                index = lineEnd;
            }
        }

        private bool IsDocumentMarker(int start, int lineEnd, string marker)
        {
            if (lineEnd - start < marker.Length ||
                !source.Substring(start, marker.Length).Equals(marker, StringComparison.Ordinal)) return false;
            int after = start + marker.Length;
            return after == lineEnd || IsWhitespace(source[after]) || source[after] == '#';
        }

        private bool IsDashToken(int at, int contentStart, int lineEnd)
        {
            if (flowFrames.Count != 0 || source[at] != '-') return false;
            if (at != contentStart &&
                (!IsWhitespace(source[at - 1]) || !hasLastLineToken || lastLineTokenKind != YamlTokenKind.Dash))
            {
                return false;
            }

            return at + 1 == lineEnd || IsWhitespace(source[at + 1]) || source[at + 1] == '#';
        }

        private bool IsFlowMappingSeparator(int at, int lineEnd)
        {
            if (flowFrames.Count == 0 || source[at] != ':') return false;
            FlowFrame frame = flowFrames[flowFrames.Count - 1];
            if (frame.Kind != FlowFrameKind.Mapping || frame.State != FlowFrameState.MappingKey)
            {
                return false;
            }

            // A colon followed by '/' is the scheme delimiter of a URL, not a flow-map separator.
            // Otherwise the key/value expectation allows both `a: b` and the compact `a:b` form.
            return at + 1 >= lineEnd || source[at + 1] != '/';
        }

        private bool IsFlowCollectionStart(int at, int contentStart, bool plainHasContent)
        {
            if (plainHasContent) return false;
            if (flowFrames.Count != 0) return true;
            if (at == contentStart) return true;

            // Outside an active flow collection, an opening brace/bracket is a collection only when it
            // starts the value introduced by a block colon, sequence dash, or anchor. Otherwise it is
            // part of the surrounding plain scalar (for example, an OpenAPI path template).
            return hasLastLineToken &&
                   (lastLineTokenKind == YamlTokenKind.Colon || lastLineTokenKind == YamlTokenKind.Dash ||
                    lastLineTokenKind == YamlTokenKind.Anchor);
        }

        private bool IsMappingColon(int at, int lineEnd)
        {
            if (source[at] != ':') return false;
            if (at + 1 >= lineEnd) return true;
            char next = source[at + 1];
            return IsWhitespace(next) || next == ',' || next == ']' || next == '}' || next == '#';
        }

        private bool IsTokenStart(int at, int contentStart)
        {
            return at == contentStart || IsWhitespace(source[at - 1]) || source[at - 1] == ',' ||
                   source[at - 1] == '[' || source[at - 1] == '{';
        }

        private static bool IsBlockScalarHeader(string value)
        {
            if (value.Length == 0 || (value[0] != '|' && value[0] != '>')) return false;
            bool hasIndent = false;
            bool hasChomping = false;
            for (int index = 1; index < value.Length; index++)
            {
                char indicator = value[index];
                if (indicator >= '1' && indicator <= '9')
                {
                    if (hasIndent) return false;
                    hasIndent = true;
                }
                else if (indicator == '+' || indicator == '-')
                {
                    if (hasChomping) return false;
                    hasChomping = true;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private int CountSpaces(int start, int end)
        {
            int count = 0;
            while (start + count < end && source[start + count] == ' ') count++;
            if (start + count < end && source[start + count] == '\t')
            {
                throw CreateExceptionAt(
                    YamlDiagnosticCode.InvalidIndentation,
                    "Tabs are not allowed in YAML indentation.",
                    line,
                    count + 1,
                    start + count);
            }

            return count;
        }

        private static bool IsAnchorCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_' || value == '-';
        }

        private static bool IsWhitespace(char value)
        {
            return value == ' ' || value == '\t';
        }

        private static bool IsHexDigit(char value)
        {
            return (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') ||
                   (value >= 'A' && value <= 'F');
        }

        private void AddToken(YamlTokenKind kind, string value, int offset, int tokenLine, int tokenColumn, int length)
        {
            if (tokens.Count >= YamlParserLimits.MaximumTokens)
            {
                throw CreateExceptionAt(
                    YamlDiagnosticCode.LimitExceeded,
                    "The YAML token count exceeds the supported maximum.",
                    tokenLine,
                    tokenColumn,
                    offset);
            }

            tokens.Add(new YamlToken(
                kind,
                value,
                new YamlSourceSpan(
                    new YamlSourceLocation(tokenLine, tokenColumn, offset),
                    length,
                    sourcePath)));

            if (tokenLine == line && kind != YamlTokenKind.Indent && kind != YamlTokenKind.NewLine &&
                kind != YamlTokenKind.Comment && kind != YamlTokenKind.EndOfInput)
            {
                hasLastLineToken = true;
                lastLineTokenKind = kind;
            }
        }

        private void UpdateFlowState(YamlTokenKind punctuation, int offset, int lineStart)
        {
            if (flowFrames.Count == 0 && punctuation == YamlTokenKind.Dash)
            {
                lineSequenceDashCount++;
            }

            if (flowFrames.Count == 0 && punctuation == YamlTokenKind.Colon)
            {
                lineHasBlockMappingColon = true;
            }

            switch (punctuation)
            {
                case YamlTokenKind.FlowMappingStart:
                    PushFlowFrame(FlowFrameKind.Mapping, offset, lineStart);
                    break;
                case YamlTokenKind.FlowSequenceStart:
                    PushFlowFrame(FlowFrameKind.Sequence, offset, lineStart);
                    break;
                case YamlTokenKind.FlowMappingEnd:
                    PopFlowFrame(FlowFrameKind.Mapping);
                    break;
                case YamlTokenKind.FlowSequenceEnd:
                    PopFlowFrame(FlowFrameKind.Sequence);
                    break;
                case YamlTokenKind.Colon:
                    if (flowFrames.Count != 0 && flowFrames[flowFrames.Count - 1].Kind == FlowFrameKind.Mapping &&
                        flowFrames[flowFrames.Count - 1].State == FlowFrameState.MappingKey)
                    {
                        flowFrames[flowFrames.Count - 1].State = FlowFrameState.MappingValue;
                    }

                    break;
                case YamlTokenKind.Comma:
                    if (flowFrames.Count != 0 && flowFrames[flowFrames.Count - 1].Kind == FlowFrameKind.Mapping &&
                        flowFrames[flowFrames.Count - 1].State == FlowFrameState.MappingValue)
                    {
                        flowFrames[flowFrames.Count - 1].State = FlowFrameState.MappingKey;
                    }

                    break;
            }
        }

        private static int GetExplicitBlockScalarIndent(string value)
        {
            for (int index = 1; index < value.Length; index++)
            {
                char indicator = value[index];
                if (indicator >= '1' && indicator <= '9') return indicator - '0';
            }

            return 0;
        }

        private void StopBlockScalarPending()
        {
            blockScalarPending = false;
            blockScalarParentIndent = 0;
            blockScalarContentIndent = -1;
        }

        private void PushFlowFrame(FlowFrameKind kind, int offset, int lineStart)
        {
            if (flowFrames.Count >= YamlParserLimits.MaximumDepth)
            {
                throw CreateExceptionAt(
                    YamlDiagnosticCode.LimitExceeded,
                    "The YAML nesting depth exceeds the supported maximum.",
                    line,
                    offset - lineStart + 1,
                    offset);
            }

            flowFrames.Add(new FlowFrame(kind));
        }

        private void PopFlowFrame(FlowFrameKind expectedKind)
        {
            if (flowFrames.Count != 0 && flowFrames[flowFrames.Count - 1].Kind == expectedKind)
            {
                flowFrames.RemoveAt(flowFrames.Count - 1);
            }
        }

        private YamlParseException CreateExceptionAt(
            YamlDiagnosticCode code,
            string message,
            int exceptionLine,
            int exceptionColumn,
            int offset)
        {
            return new YamlParseException(code, message, sourcePath, exceptionLine, exceptionColumn, offset);
        }
    }
}
