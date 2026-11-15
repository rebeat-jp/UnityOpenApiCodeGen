using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// 型付きの JSON リテラル種別。enum 値、default、const で使う。
    /// Kind of a typed JSON literal used by enum values, default and const.
    /// </summary>
    internal enum OpenApiSemanticLiteralKind
    {
        Null,
        String,
        Integer,
        Number,
        Boolean
    }

    /// <summary>
    /// wire 上の表現を保持した型付きリテラル。RawText は文字列なら値そのもの、
    /// それ以外は JSON のトークン文字列（例: 1, 1.5, true）。
    /// A typed literal that keeps its wire representation. RawText is the string value
    /// for strings and the JSON token text otherwise (for example 1, 1.5, true).
    /// </summary>
    internal readonly struct OpenApiSemanticLiteral : IEquatable<OpenApiSemanticLiteral>
    {
        internal OpenApiSemanticLiteral(OpenApiSemanticLiteralKind kind, string rawText)
        {
            Kind = kind;
            RawText = rawText ?? string.Empty;
        }

        internal OpenApiSemanticLiteralKind Kind { get; }

        internal string RawText { get; }

        internal static OpenApiSemanticLiteral Null =>
            new OpenApiSemanticLiteral(OpenApiSemanticLiteralKind.Null, string.Empty);

        internal static OpenApiSemanticLiteral FromString(string value)
        {
            return new OpenApiSemanticLiteral(OpenApiSemanticLiteralKind.String, value);
        }

        public bool Equals(OpenApiSemanticLiteral other)
        {
            return Kind == other.Kind &&
                   string.Equals(RawText, other.RawText, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is OpenApiSemanticLiteral other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(RawText);
            }
        }
    }

    /// <summary>
    /// schema に付随する注釈。wire 契約には影響しない。
    /// Annotations attached to a schema. They never change the wire contract.
    /// </summary>
    /// <remarks>
    /// Default / Const: null means "not specified", whereas OpenApiSemanticLiteral.Null means an
    /// explicit JSON null. Numeric literals keep their original text in RawText (not normalized).
    /// Equality is by value.
    /// </remarks>
    internal sealed class OpenApiSemanticAnnotations : IEquatable<OpenApiSemanticAnnotations>
    {
        internal static readonly OpenApiSemanticAnnotations Empty =
            new OpenApiSemanticAnnotations(string.Empty, false, false, false, null, null);

        internal OpenApiSemanticAnnotations(
            string description,
            bool deprecated,
            bool readOnly,
            bool writeOnly,
            OpenApiSemanticLiteral? defaultValue,
            OpenApiSemanticLiteral? constValue)
        {
            Description = description ?? string.Empty;
            Deprecated = deprecated;
            ReadOnly = readOnly;
            WriteOnly = writeOnly;
            Default = defaultValue;
            Const = constValue;
        }

        internal string Description { get; }

        internal bool Deprecated { get; }

        internal bool ReadOnly { get; }

        internal bool WriteOnly { get; }

        internal OpenApiSemanticLiteral? Default { get; }

        internal OpenApiSemanticLiteral? Const { get; }

        public bool Equals(OpenApiSemanticAnnotations? other)
        {
            return other is not null &&
                   string.Equals(Description, other.Description, StringComparison.Ordinal) &&
                   Deprecated == other.Deprecated &&
                   ReadOnly == other.ReadOnly &&
                   WriteOnly == other.WriteOnly &&
                   Nullable.Equals(Default, other.Default) &&
                   Nullable.Equals(Const, other.Const);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as OpenApiSemanticAnnotations);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = StringComparer.Ordinal.GetHashCode(Description);
                hashCode = (hashCode * 397) ^ Deprecated.GetHashCode();
                hashCode = (hashCode * 397) ^ ReadOnly.GetHashCode();
                hashCode = (hashCode * 397) ^ WriteOnly.GetHashCode();
                hashCode = (hashCode * 397) ^ Default.GetHashCode();
                hashCode = (hashCode * 397) ^ Const.GetHashCode();
                return hashCode;
            }
        }
    }

    /// <summary>
    /// 解析中に収集した「無視した OpenAPI 要素」の Warning（OACG108 の元データ）。
    /// A warning about an ignored OpenAPI element collected during parsing (source of OACG108).
    /// </summary>
    internal sealed class OpenApiSemanticWarning : IEquatable<OpenApiSemanticWarning>
    {
        internal OpenApiSemanticWarning(OpenApiSourceLocation location, string message)
        {
            Location = location;
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        internal OpenApiSourceLocation Location { get; }

        internal string Message { get; }

        public bool Equals(OpenApiSemanticWarning? other)
        {
            return other is not null &&
                   Location.Equals(other.Location) &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as OpenApiSemanticWarning);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Location.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(Message);
            }
        }
    }

    /// <summary>
    /// Warning を挿入順で蓄積する。重複排除は (位置, メッセージ) の完全一致のみ。
    /// Accumulates warnings in insertion order. De-duplication is by exact match of
    /// (location, message) only.
    /// </summary>
    internal sealed class OpenApiWarningCollector
    {
        private readonly List<OpenApiSemanticWarning> _warnings = new List<OpenApiSemanticWarning>();
        private readonly HashSet<OpenApiSemanticWarning> _seen = new HashSet<OpenApiSemanticWarning>();

        internal int Count => _warnings.Count;

        internal void Add(OpenApiSourceLocation location, string message)
        {
            var warning = new OpenApiSemanticWarning(location, message);
            if (_seen.Add(warning))
            {
                _warnings.Add(warning);
            }
        }

        internal void Add(SpecNode node, string message)
        {
            Add(OpenApiSourceLocation.FromNode(node), message);
        }

        internal IReadOnlyList<OpenApiSemanticWarning> ToList()
        {
            return _warnings.ToArray();
        }
    }
}
