#nullable enable

using System;
using System.IO;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    /// <summary>
    /// Adds the versionDefines entry that defines OPENAPI_CODEGEN_SOURCE_GENERATOR to the asmdef that
    /// contains a generated definition. Unlike a PlayerSettings scripting define, the entry does not
    /// depend on the active build target or Build Profile.
    /// The asmdef text is edited in place so unknown fields, the BOM, the line endings and the layout
    /// of the existing file are preserved. An asmdef that cannot be understood is never rewritten.
    /// </summary>
    internal sealed class TargetAssemblyVersionDefineWriter
    {
        internal const string PackageName = "jp.rhycol.openapicodegen.source-generator";
        internal const string Expression = "0.0.0";
        internal const string Define = "OPENAPI_CODEGEN_SOURCE_GENERATOR";

        private const string VersionDefinesKey = "versionDefines";
        private const string DefaultUnit = "  ";
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        private readonly IAtomicFileWriter fileWriter;

        internal TargetAssemblyVersionDefineWriter(IAtomicFileWriter fileWriter)
        {
            this.fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));
        }

        /// <summary>The JSON object to add to "versionDefines" by hand.</summary>
        internal static string ManualEntryJson =>
            "{ \"name\": \"" + PackageName + "\", \"expression\": \"" + Expression +
            "\", \"define\": \"" + Define + "\" }";

        /// <summary>
        /// Makes sure the asmdef declares the define. Returns true when the file was written and false
        /// when an entry with the same name and define already exists (its expression is not compared).
        /// </summary>
        /// <exception cref="SafeGenerationException">
        /// The asmdef could not be read, parsed or written. The file is left unmodified and the message
        /// shows the entry to add by hand.
        /// </exception>
        internal bool EnsureVersionDefine(string asmdefPath)
        {
            if (string.IsNullOrWhiteSpace(asmdefPath))
            {
                throw new ArgumentException("An asmdef path is required.", nameof(asmdefPath));
            }

            byte[] bytes;
            string text;
            bool hasBom;
            try
            {
                bytes = File.ReadAllBytes(asmdefPath);
                hasBom = bytes.Length >= Utf8Bom.Length &&
                    bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2];
                text = StrictUtf8.GetString(
                    bytes,
                    hasBom ? Utf8Bom.Length : 0,
                    bytes.Length - (hasBom ? Utf8Bom.Length : 0));
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is DecoderFallbackException)
            {
                throw Failure(asmdefPath, "the file could not be read");
            }

            SpecObjectNode root;
            try
            {
                root = StrictJsonSpecParser.Parse(text, asmdefPath) as SpecObjectNode
                    ?? throw Failure(asmdefPath, "the root is not a JSON object");
            }
            catch (NormalizedSpecException)
            {
                throw Failure(asmdefPath, "the file is not valid JSON");
            }

            SpecProperty? versionDefines = FindProperty(root, VersionDefinesKey);
            if (versionDefines != null)
            {
                if (!(versionDefines.Value is SpecArrayNode array))
                {
                    throw Failure(asmdefPath, "\"versionDefines\" is not an array");
                }

                foreach (SpecNode item in array.Items)
                {
                    if (!(item is SpecObjectNode entry))
                    {
                        throw Failure(asmdefPath, "\"versionDefines\" contains a non-object item");
                    }

                    if (IsString(entry, "name", PackageName) && IsString(entry, "define", Define))
                    {
                        return false;
                    }
                }
            }

            string updated = Insert(asmdefPath, text, versionDefines != null);
            byte[] body = StrictUtf8.GetBytes(updated);
            byte[] output;
            if (hasBom)
            {
                output = new byte[Utf8Bom.Length + body.Length];
                Buffer.BlockCopy(Utf8Bom, 0, output, 0, Utf8Bom.Length);
                Buffer.BlockCopy(body, 0, output, Utf8Bom.Length, body.Length);
            }
            else
            {
                output = body;
            }

            try
            {
                fileWriter.WriteAllBytesAtomically(asmdefPath, output);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                throw Failure(asmdefPath, "the file could not be written");
            }

            return true;
        }

        private static SafeGenerationException Failure(string asmdefPath, string reason)
        {
            return new SafeGenerationException(
                "The target asmdef '" + Path.GetFileName(asmdefPath) +
                "' could not be updated automatically (" + reason + "), so it was not modified. " +
                "Add this entry to its \"versionDefines\" by hand so that " + Define +
                " is defined for every build target: " + ManualEntryJson);
        }

        private static SpecProperty? FindProperty(SpecObjectNode node, string name)
        {
            foreach (SpecProperty property in node.Properties)
            {
                if (string.Equals(property.Name, name, StringComparison.Ordinal))
                {
                    return property;
                }
            }

            return null;
        }

        private static bool IsString(SpecObjectNode node, string name, string expected)
        {
            SpecProperty? property = FindProperty(node, name);
            return property != null &&
                property.Value is SpecStringNode value &&
                string.Equals(value.Value, expected, StringComparison.Ordinal);
        }

        private static string Insert(string asmdefPath, string text, bool versionDefinesExists)
        {
            Layout layout = Scan(text);
            if (layout.RootOpen < 0 || layout.RootClose < 0)
            {
                throw Failure(asmdefPath, "the root object could not be located");
            }

            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            if (versionDefinesExists)
            {
                if (layout.KeyOffset < 0 || layout.ArrayOpen < 0 || layout.ArrayClose < 0)
                {
                    throw Failure(asmdefPath, "\"versionDefines\" could not be located");
                }

                string keyIndent = LineIndent(text, layout.KeyOffset);
                string unit = keyIndent.Length != 0 ? keyIndent : DefaultUnit;
                int lastContent = LastNonWhitespaceBefore(text, layout.ArrayClose, layout.ArrayOpen);
                if (lastContent == layout.ArrayOpen)
                {
                    string itemIndent = keyIndent + unit;
                    return text.Substring(0, layout.ArrayOpen + 1) +
                        nl + itemIndent + Entry(itemIndent, unit, nl) + nl + keyIndent +
                        text.Substring(layout.ArrayClose);
                }

                int firstContent = FirstNonWhitespaceAfter(text, layout.ArrayOpen + 1, layout.ArrayClose);
                string existingItemIndent =
                    text.IndexOf('\n', layout.ArrayOpen, firstContent - layout.ArrayOpen) >= 0
                        ? LineIndent(text, firstContent)
                        : keyIndent + unit;
                return text.Substring(0, lastContent + 1) +
                    "," + nl + existingItemIndent + Entry(existingItemIndent, unit, nl) +
                    text.Substring(lastContent + 1);
            }

            string rootKeyIndent = layout.FirstKeyOffset >= 0
                ? LineIndent(text, layout.FirstKeyOffset)
                : string.Empty;
            string rootUnit = rootKeyIndent.Length != 0 ? rootKeyIndent : DefaultUnit;
            string indent = rootKeyIndent.Length != 0 ? rootKeyIndent : DefaultUnit;
            string block = "\"" + VersionDefinesKey + "\": [" + nl +
                indent + rootUnit + Entry(indent + rootUnit, rootUnit, nl) + nl +
                indent + "]";
            int lastRootContent = LastNonWhitespaceBefore(text, layout.RootClose, layout.RootOpen);
            if (lastRootContent == layout.RootOpen)
            {
                return text.Substring(0, layout.RootOpen + 1) +
                    nl + indent + block + nl +
                    text.Substring(layout.RootClose);
            }

            return text.Substring(0, lastRootContent + 1) +
                "," + nl + indent + block +
                text.Substring(lastRootContent + 1);
        }

        private static string Entry(string indent, string unit, string nl)
        {
            string inner = indent + unit;
            return "{" + nl +
                inner + "\"name\": \"" + PackageName + "\"," + nl +
                inner + "\"expression\": \"" + Expression + "\"," + nl +
                inner + "\"define\": \"" + Define + "\"" + nl +
                indent + "}";
        }

        private static string LineIndent(string text, int offset)
        {
            int lineStart = offset;
            while (lineStart > 0 && text[lineStart - 1] != '\n')
            {
                lineStart--;
            }

            int end = lineStart;
            while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
            {
                end++;
            }

            return text.Substring(lineStart, end - lineStart);
        }

        private static int LastNonWhitespaceBefore(string text, int exclusiveEnd, int lowerBound)
        {
            int index = exclusiveEnd - 1;
            while (index > lowerBound && IsWhitespace(text[index]))
            {
                index--;
            }

            return index;
        }

        private static int FirstNonWhitespaceAfter(string text, int start, int limit)
        {
            int index = start;
            while (index < limit && IsWhitespace(text[index]))
            {
                index++;
            }

            return index;
        }

        private static bool IsWhitespace(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }

        /// <summary>
        /// Locates the offsets needed to splice the text. The grammar was already validated by
        /// <see cref="StrictJsonSpecParser"/>, so this scan only tracks strings and nesting.
        /// </summary>
        private static Layout Scan(string text)
        {
            var layout = new Layout();
            int depth = 0;
            bool expectKey = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    int start = i;
                    i++;
                    while (i < text.Length && text[i] != '"')
                    {
                        if (text[i] == '\\')
                        {
                            i++;
                        }

                        i++;
                    }

                    if (depth == 1 && expectKey)
                    {
                        expectKey = false;
                        if (layout.FirstKeyOffset < 0)
                        {
                            layout.FirstKeyOffset = start;
                        }

                        if (i <= text.Length &&
                            string.Equals(
                                text.Substring(start + 1, Math.Min(i, text.Length) - start - 1),
                                VersionDefinesKey,
                                StringComparison.Ordinal))
                        {
                            layout.KeyOffset = start;
                            int j = i + 1;
                            while (j < text.Length && IsWhitespace(text[j]))
                            {
                                j++;
                            }

                            if (j < text.Length && text[j] == ':')
                            {
                                j++;
                                while (j < text.Length && IsWhitespace(text[j]))
                                {
                                    j++;
                                }

                                if (j < text.Length && text[j] == '[')
                                {
                                    layout.ArrayOpen = j;
                                }
                            }
                        }
                    }

                    continue;
                }

                if (c == '{' || c == '[')
                {
                    depth++;
                    if (c == '{' && depth == 1)
                    {
                        layout.RootOpen = i;
                        expectKey = true;
                    }

                    continue;
                }

                if (c == '}' || c == ']')
                {
                    if (c == ']' && depth == 2 && layout.ArrayOpen >= 0 &&
                        layout.ArrayClose < 0 && i > layout.ArrayOpen)
                    {
                        layout.ArrayClose = i;
                    }

                    if (c == '}' && depth == 1)
                    {
                        layout.RootClose = i;
                    }

                    depth--;
                    continue;
                }

                if (c == ',' && depth == 1)
                {
                    expectKey = true;
                }
            }

            return layout;
        }

        private sealed class Layout
        {
            internal int RootOpen = -1;
            internal int RootClose = -1;
            internal int FirstKeyOffset = -1;
            internal int KeyOffset = -1;
            internal int ArrayOpen = -1;
            internal int ArrayClose = -1;
        }
    }
}
