using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class CSharpMethodBuilder
    {
        private readonly string _name;
        private string _returnType = "void";
        private readonly List<string> _modifiers = new();
        private readonly List<CSharpAttribute> _attributes = new();
        private readonly List<string> _genericParameters = new();
        private readonly List<string> _constraints = new();
        private readonly List<CSharpParameter> _parameters = new();
        private string? _body;

        public CSharpMethodBuilder(string name)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
        }

        /// <summary>
        /// 戻り値の型を設定する。
        /// Sets return type.
        /// </summary>
        public CSharpMethodBuilder WithReturnType(string returnType)
        {
            if (!string.IsNullOrWhiteSpace(returnType))
            {
                _returnType = returnType;
            }

            return this;
        }

        /// <summary>
        /// 修飾子を追加する。
        /// Adds a modifier.
        /// </summary>
        public CSharpMethodBuilder AddModifier(string modifier)
        {
            if (!string.IsNullOrWhiteSpace(modifier))
            {
                _modifiers.Add(modifier);
            }

            return this;
        }

        /// <summary>
        /// 修飾子をまとめて追加する。
        /// Adds multiple modifiers.
        /// </summary>
        public CSharpMethodBuilder AddModifiers(params string[] modifiers)
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
        public CSharpMethodBuilder AddAttribute(string name, params string[] arguments)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                var args = arguments ?? Array.Empty<string>();
                _attributes.Add(new CSharpAttribute(name, args.ToList()));
            }

            return this;
        }

        /// <summary>
        /// ジェネリック型パラメータを追加する。
        /// Adds a generic parameter.
        /// </summary>
        public CSharpMethodBuilder AddGenericParameter(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _genericParameters.Add(name);
            }

            return this;
        }

        /// <summary>
        /// 型制約句を追加する。
        /// Adds a type constraint clause.
        /// </summary>
        public CSharpMethodBuilder AddConstraint(string clause)
        {
            if (!string.IsNullOrWhiteSpace(clause))
            {
                _constraints.Add(clause);
            }

            return this;
        }

        /// <summary>
        /// 引数を追加する。
        /// Adds a parameter.
        /// </summary>
        public CSharpMethodBuilder AddParameter(string name, string type)
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(type))
            {
                _parameters.Add(new CSharpParameter(name, type));
            }

            return this;
        }

        /// <summary>
        /// メソッド本体を設定する。
        /// Sets method body.
        /// </summary>
        public CSharpMethodBuilder WithBody(string body)
        {
            _body = body;
            return this;
        }

        /// <summary>
        /// メソッド宣言を構築する。
        /// Builds the method declaration.
        /// </summary>
        public CSharpMethodDeclaration Build()
        {
            return new CSharpMethodDeclaration(
                _name,
                _returnType,
                _modifiers.ToList(),
                _attributes.ToList(),
                _genericParameters.ToList(),
                _constraints.ToList(),
                _parameters.ToList(),
                _body);
        }
    }
}
