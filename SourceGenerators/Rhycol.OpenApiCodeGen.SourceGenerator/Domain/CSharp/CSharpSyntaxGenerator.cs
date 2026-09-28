using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// C# ファイルのビルダー。
    /// Builder for a C# file domain model.
    /// </summary>
    internal sealed class CSharpSyntaxGenerator
    {
        private readonly string _fileName;
        private readonly string _namespace;
        private readonly List<string> _usings = new();
        private readonly List<CSharpTypeDeclaration> _types = new();

        public CSharpSyntaxGenerator(string fileName, string @namespace)
        {
            _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            _namespace = @namespace ?? throw new ArgumentNullException(nameof(@namespace));
        }

        /// <summary>
        /// using を追加する。
        /// Adds a using directive.
        /// </summary>
        public CSharpSyntaxGenerator AddUsing(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _usings.Add(name);
            }

            return this;
        }

        /// <summary>
        /// using をまとめて追加する。
        /// Adds multiple using directives.
        /// </summary>
        public CSharpSyntaxGenerator AddUsings(params string[] names)
        {
            if (names is null)
            {
                return this;
            }

            foreach (var name in names)
            {
                AddUsing(name);
            }

            return this;
        }

        /// <summary>
        /// 条件付きで using を追加する。
        /// Adds a using directive if condition is true.
        /// </summary>
        public CSharpSyntaxGenerator AddUsingIf(bool condition, string name)
        {
            if (condition)
            {
                AddUsing(name);
            }

            return this;
        }

        /// <summary>
        /// 既存の型宣言を追加する。
        /// Adds an existing type declaration.
        /// </summary>
        public CSharpSyntaxGenerator AddType(CSharpTypeDeclaration declaration)
        {
            if (declaration is null)
            {
                throw new ArgumentNullException(nameof(declaration));
            }

            _types.Add(declaration);
            return this;
        }

        /// <summary>
        /// クラスを追加する。
        /// Adds a class declaration.
        /// </summary>
        public CSharpSyntaxGenerator AddClass(string name, Action<CSharpClassBuilder> configure)
        {
            var builder = new CSharpClassBuilder(name);
            configure?.Invoke(builder);
            _types.Add(builder.Build());
            return this;
        }

        /// <summary>
        /// 構造体を追加する。
        /// Adds a struct declaration.
        /// </summary>
        public CSharpSyntaxGenerator AddStruct(string name, Action<CSharpStructBuilder> configure)
        {
            var builder = new CSharpStructBuilder(name);
            configure?.Invoke(builder);
            _types.Add(builder.Build());
            return this;
        }

        /// <summary>
        /// インターフェースを追加する。
        /// Adds an interface declaration.
        /// </summary>
        public CSharpSyntaxGenerator AddInterface(string name, Action<CSharpInterfaceBuilder> configure)
        {
            var builder = new CSharpInterfaceBuilder(name);
            configure?.Invoke(builder);
            _types.Add(builder.Build());
            return this;
        }

        /// <summary>
        /// enum を追加する。
        /// Adds an enum declaration.
        /// </summary>
        public CSharpSyntaxGenerator AddEnum(string name, Action<CSharpEnumBuilder> configure)
        {
            var builder = new CSharpEnumBuilder(name);
            configure?.Invoke(builder);
            _types.Add(builder.Build());
            return this;
        }

        /// <summary>
        /// CSharpFile を生成する。
        /// Builds CSharpFile.
        /// </summary>
        public CSharpFile Build()
        {
            return new CSharpFile(
                _fileName,
                _namespace,
                _usings.Distinct(StringComparer.Ordinal).ToList(),
                _types.ToList());
        }
    }
}
