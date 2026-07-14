using System;
using System.Collections.Generic;
using System.Linq;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    internal abstract class CSharpTypeBuilderBase<TDeclaration>
        where TDeclaration : CSharpTypeDeclaration
    {
        protected readonly string Name;
        protected readonly List<string> Modifiers = new();
        protected readonly List<CSharpAttribute> Attributes = new();
        protected readonly List<string> GenericParameters = new();
        protected readonly List<string> BaseTypes = new();
        protected readonly List<string> Comments = new();

        protected CSharpTypeBuilderBase(string name)
        {
            Name = name;
        }

        /// <summary>
        /// 修飾子を追加する。
        /// Adds a modifier.
        /// </summary>
        public CSharpTypeBuilderBase<TDeclaration> AddModifier(string modifier)
        {
            if (!string.IsNullOrWhiteSpace(modifier))
            {
                Modifiers.Add(modifier);
            }

            return this;
        }

        /// <summary>
        /// 修飾子をまとめて追加する。
        /// Adds multiple modifiers.
        /// </summary>
        public CSharpTypeBuilderBase<TDeclaration> AddModifiers(params string[] modifiers)
        {
            if (modifiers is null)
            {
                return this;
            }

            foreach (var modifier in modifiers)
            {
                AddModifier(modifier);
            }

            return this;
        }

        /// <summary>
        /// 属性を追加する。
        /// Adds an attribute.
        /// </summary>
        public CSharpTypeBuilderBase<TDeclaration> AddAttribute(string name, params string[] arguments)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                var args = arguments ?? Array.Empty<string>();
                Attributes.Add(new CSharpAttribute(name, args.ToList()));
            }

            return this;
        }

        /// <summary>
        /// ジェネリック型パラメータを追加する。
        /// Adds a generic parameter.
        /// </summary>
        public CSharpTypeBuilderBase<TDeclaration> AddGenericParameter(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                GenericParameters.Add(name);
            }

            return this;
        }

        /// <summary>
        /// 継承/実装の型名を追加する。
        /// Adds a base type or interface.
        /// </summary>
        public CSharpTypeBuilderBase<TDeclaration> AddBaseType(string typeName)
        {
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                BaseTypes.Add(typeName);
            }

            return this;
        }

        /// <summary>
        /// 型宣言のドキュメントコメントを追加する。
        /// Adds XML doc comment lines.
        /// </summary>
        public CSharpTypeBuilderBase<TDeclaration> AddComment(string comment)
        {
            if (!string.IsNullOrWhiteSpace(comment))
            {
                Comments.Add(comment);
            }

            return this;
        }

        /// <summary>
        /// 型宣言を構築する。
        /// Builds the type declaration.
        /// </summary>
        public abstract TDeclaration Build();
    }
}
