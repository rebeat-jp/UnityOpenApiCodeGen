using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Verification
{
    /// <summary>
    /// Makes compilation fail if the production analyzer does not generate the expected client.
    /// </summary>
    public static class GeneratedClientProbe
    {
        public static Type ClientType => typeof(Rhycol.OpenApiCodeGen.Generated.Api);
    }
}
