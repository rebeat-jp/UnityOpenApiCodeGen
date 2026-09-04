using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Verification
{
    /// <summary>
    /// Resolves the generated client without making the pre-generation project uncompilable.
    /// </summary>
    public static class GeneratedClientProbe
    {
        private const string GeneratedClientAssemblyQualifiedName =
            "Rhycol.OpenApiCodeGen.Generated.Api, Unity.OpenApiCodeGen.SourceGenerator.Verification";

        public static Type ClientType => Type.GetType(
            GeneratedClientAssemblyQualifiedName,
            throwOnError: false);
    }
}
