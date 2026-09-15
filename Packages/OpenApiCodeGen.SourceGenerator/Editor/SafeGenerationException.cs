#nullable enable

using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    // Only use for messages assembled from fixed text and explicitly redacted data.
    // Do not attach framework exceptions, which can retain paths or credentials.
    internal class SafeGenerationException : InvalidOperationException
    {
        internal SafeGenerationException(string message) : base(message) { }
    }
}
