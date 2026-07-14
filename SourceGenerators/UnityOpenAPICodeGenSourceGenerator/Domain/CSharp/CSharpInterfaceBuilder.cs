using System.Collections.Generic;
using System.Linq;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    internal sealed class CSharpInterfaceBuilder : CSharpTypeBuilderBase<CSharpInterfaceDeclaration>
    {
        private readonly List<string> _members = new();

        public CSharpInterfaceBuilder(string name)
            : base(name)
        {
        }

        /// <summary>
        /// 生のメンバー文字列を追加する。
        /// Adds raw member source.
        /// </summary>
        public CSharpInterfaceBuilder AddMember(string memberSource)
        {
            if (!string.IsNullOrWhiteSpace(memberSource))
            {
                _members.Add(memberSource);
            }

            return this;
        }

        /// <summary>
        /// インターフェース宣言を構築する。
        /// Builds the interface declaration.
        /// </summary>
        public override CSharpInterfaceDeclaration Build()
        {
            return new CSharpInterfaceDeclaration(
                Name,
                Modifiers.ToList(),
                Attributes.ToList(),
                GenericParameters.ToList(),
                BaseTypes.ToList(),
                _members.ToList(),
                Comments.ToList());
        }
    }
}
