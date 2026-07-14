using System;
using System.Collections.Generic;
using System.Linq;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    internal sealed class CSharpStructBuilder : CSharpTypeBuilderBase<CSharpStructDeclaration>
    {
        private readonly List<string> _members = new();
        private readonly List<CSharpMethodDeclaration> _methods = new();

        public CSharpStructBuilder(string name)
            : base(name)
        {
        }

        /// <summary>
        /// 生のメンバー文字列を追加する。
        /// Adds raw member source.
        /// </summary>
        public CSharpStructBuilder AddMember(string memberSource)
        {
            if (!string.IsNullOrWhiteSpace(memberSource))
            {
                _members.Add(memberSource);
            }

            return this;
        }

        /// <summary>
        /// メソッドを追加する。
        /// Adds a method declaration.
        /// </summary>
        public CSharpStructBuilder AddMethod(string name, Action<CSharpMethodBuilder> configure)
        {
            var builder = new CSharpMethodBuilder(name);
            configure?.Invoke(builder);
            _methods.Add(builder.Build());
            return this;
        }

        /// <summary>
        /// 構造体宣言を構築する。
        /// Builds the struct declaration.
        /// </summary>
        public override CSharpStructDeclaration Build()
        {
            return new CSharpStructDeclaration(
                Name,
                Modifiers.ToList(),
                Attributes.ToList(),
                GenericParameters.ToList(),
                BaseTypes.ToList(),
                _members.ToList(),
                _methods.ToList(),
                Comments.ToList());
        }
    }
}
