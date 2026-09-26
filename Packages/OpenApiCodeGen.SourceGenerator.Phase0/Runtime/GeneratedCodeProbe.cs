namespace Rhycol.OpenApiCodeGen.SourceGenerator.Phase0
{
    /// <summary>
    /// Exposes the internal Phase 0 generated values to the Unity test assembly.
    /// </summary>
    public static class GeneratedCodeProbe
    {
        public static string Fixed => Phase0Fixed.Marker;

        public static string Root => Phase0Additional.Root;

        public static string Scoped => Phase0Additional.Scoped;
    }
}
