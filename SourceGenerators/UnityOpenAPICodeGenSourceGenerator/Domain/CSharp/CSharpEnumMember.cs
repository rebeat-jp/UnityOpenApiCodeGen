namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// enum メンバーのドメインモデル。
    /// Domain model for enum member.
    /// </summary>
    internal sealed class CSharpEnumMember
    {
        public string Name { get; }
        public string? Value { get; }

        public CSharpEnumMember(string name, string? value = null)
        {
            Name = name;
            Value = value;
        }
    }
}
