using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Creates the nodes of a normalized (OpenAPI 3.0) tree synthesized from a Swagger 2.0 document.
    /// Every node has a logical path in the new tree (the pointer a reference or an identity uses) and
    /// remembers the Swagger node it came from: its line, column and logical path are the original ones, so
    /// diagnostics point at the Swagger document the user wrote.
    /// </summary>
    internal static class Swagger2NodeFactory
    {
        internal static string EscapeToken(string token)
        {
            return token.Replace("~", "~0").Replace("/", "~1");
        }

        internal static string Child(string parentPath, string name)
        {
            return parentPath + "/" + EscapeToken(name);
        }

        internal static string Index(string parentPath, int index)
        {
            return parentPath + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static SpecNode Object(SpecNode origin, string path, IReadOnlyList<SpecProperty> properties)
        {
            return WithOrigin(SpecNode.CreateObject(origin.Line, origin.Column, path, properties), origin);
        }

        internal static SpecNode Array(SpecNode origin, string path, IReadOnlyList<SpecNode> items)
        {
            return WithOrigin(SpecNode.CreateArray(origin.Line, origin.Column, path, items), origin);
        }

        internal static SpecNode String(SpecNode origin, string path, string value)
        {
            return WithOrigin(SpecNode.CreateString(origin.Line, origin.Column, path, value), origin);
        }

        internal static SpecNode Boolean(SpecNode origin, string path, bool value)
        {
            return WithOrigin(SpecNode.CreateBoolean(origin.Line, origin.Column, path, value), origin);
        }

        /// <summary>A property whose key position is taken from the node it is synthesized from.</summary>
        internal static SpecProperty Property(string name, SpecNode value, SpecNode keyOrigin)
        {
            return new SpecProperty(name, keyOrigin.Line, keyOrigin.Column, value);
        }

        internal static SpecProperty Property(SpecProperty source, SpecNode value)
        {
            return new SpecProperty(source.Name, source.Line, source.Column, value);
        }

        /// <summary>Copies a subtree as written, giving it logical paths below <paramref name="path"/>.</summary>
        internal static SpecNode Clone(SpecNode source, string path)
        {
            switch (source.ValueKind)
            {
                case SpecValueKind.Object:
                {
                    var properties = new List<SpecProperty>();
                    foreach (SpecProperty property in source.EnumerateObject())
                    {
                        properties.Add(Property(property, Clone(property.Value, Child(path, property.Name))));
                    }

                    return Object(source, path, properties);
                }

                case SpecValueKind.Array:
                {
                    var items = new List<SpecNode>();
                    int index = 0;
                    foreach (SpecNode item in source.EnumerateArray())
                    {
                        items.Add(Clone(item, Index(path, index++)));
                    }

                    return Array(source, path, items);
                }

                case SpecValueKind.String:
                    return String(source, path, source.GetString() ?? string.Empty);
                case SpecValueKind.Number:
                    return WithOrigin(
                        SpecNode.CreateNumber(source.Line, source.Column, path, source.IsInteger, source.GetRawJson()),
                        source);
                case SpecValueKind.True:
                    return Boolean(source, path, true);
                case SpecValueKind.False:
                    return Boolean(source, path, false);
                case SpecValueKind.Null:
                    return WithOrigin(SpecNode.CreateNull(source.Line, source.Column, path), source);
                default:
                    throw new InvalidOperationException("Unknown node kind.");
            }
        }

        private static SpecNode WithOrigin(SpecNode node, SpecNode origin)
        {
            node.OriginLogicalPath = origin.DiagnosticLogicalPath;
            return node;
        }
    }
}
