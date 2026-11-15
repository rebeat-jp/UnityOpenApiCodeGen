using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>A server URL template variable (servers[].variables).</summary>
    internal sealed class OpenApiSemanticServerVariable
    {
        internal OpenApiSemanticServerVariable(
            string name,
            string defaultValue,
            IReadOnlyList<string> enumValues)
        {
            Name = name;
            DefaultValue = defaultValue;
            EnumValues = enumValues ?? Array.Empty<string>();
        }

        internal string Name { get; }

        internal string DefaultValue { get; }

        /// <summary>Allowed values. Empty when the variable is not restricted.</summary>
        internal IReadOnlyList<string> EnumValues { get; }
    }

    /// <summary>A Server Object: a URL template, its variables and the URL with defaults substituted.</summary>
    internal sealed class OpenApiSemanticServer
    {
        internal OpenApiSemanticServer(
            string urlTemplate,
            string defaultUrl,
            IReadOnlyList<OpenApiSemanticServerVariable> variables)
        {
            UrlTemplate = urlTemplate;
            DefaultUrl = defaultUrl;
            Variables = variables ?? Array.Empty<OpenApiSemanticServerVariable>();
        }

        /// <summary>The url value as written, including {variable} placeholders.</summary>
        internal string UrlTemplate { get; }

        /// <summary>The url with every variable replaced by its default.</summary>
        internal string DefaultUrl { get; }

        internal IReadOnlyList<OpenApiSemanticServerVariable> Variables { get; }
    }
}
