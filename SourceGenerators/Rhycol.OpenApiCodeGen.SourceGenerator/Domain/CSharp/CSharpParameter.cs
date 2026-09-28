namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// メソッド引数のドメインモデル。
    /// Domain model for a method parameter.
    /// </summary>
    internal sealed class CSharpParameter
    {
        public string Name { get; }
        public string Type { get; }

        public CSharpParameter(string name, string type)
        {
            Name = name;
            Type = type;
        }
    }
}
