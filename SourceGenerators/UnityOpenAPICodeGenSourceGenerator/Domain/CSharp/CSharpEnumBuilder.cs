using System.Collections.Generic;
using System.Linq;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    internal sealed class CSharpEnumBuilder : CSharpTypeBuilderBase<CSharpEnumDeclaration>
    {
        private readonly List<CSharpEnumMember> _members = new();

        public CSharpEnumBuilder(string name)
            : base(name)
        {
        }

        /// <summary>
        /// enum メンバーを追加する。
        /// Adds an enum member.
        /// </summary>
        public CSharpEnumBuilder AddMember(string name, string? value = null)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _members.Add(new CSharpEnumMember(name, value));
            }

            return this;
        }

        /// <summary>
        /// enum 宣言を構築する。
        /// Builds the enum declaration.
        /// </summary>
        public override CSharpEnumDeclaration Build()
        {
            return new CSharpEnumDeclaration(
                Name,
                Modifiers.ToList(),
                Attributes.ToList(),
                BaseTypes.ToList(),
                _members.ToList(),
                Comments.ToList());
        }
    }
}
