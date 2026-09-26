using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Phase0
{
    [Generator(LanguageNames.CSharp)]
    public sealed class Phase0IncrementalGenerator : IIncrementalGenerator
    {
        public const string AnalyzerName = "Rhycol.OpenApiCodeGen.SourceGenerator.Phase0";
        public const string AdditionalFileSuffix = "." + AnalyzerName + ".additionalfile";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(static output =>
            {
                output.AddSource(
                    "Phase0Fixed.g.cs",
                    SourceText.From(
                        "namespace Rhycol.OpenApiCodeGen.SourceGenerator.Phase0 { internal static class Phase0Fixed { internal const string Marker = \"Phase0\"; } }",
                        Encoding.UTF8));
            });

            IncrementalValuesProvider<AdditionalInput> matchingInputs = context.AdditionalTextsProvider
                .Where(static text => IsMatchingAdditionalFile(text.Path))
                .Select(static (text, cancellationToken) => AdditionalInput.From(text, cancellationToken))
                .WithTrackingName("Phase0ParseAdditionalFile");

            IncrementalValueProvider<ImmutableArray<AdditionalInput>> collectedInputs = matchingInputs
                .Collect()
                .WithTrackingName("Phase0CollectAdditionalFiles");

            context.RegisterSourceOutput(collectedInputs, static (output, inputs) =>
            {
                output.AddSource(
                    "Phase0Additional.g.cs",
                    SourceText.From(RenderAdditionalValues(inputs), Encoding.UTF8));
            });
        }

        private static bool IsMatchingAdditionalFile(string path)
        {
            return Path.GetFileName(path).EndsWith(AdditionalFileSuffix, StringComparison.Ordinal);
        }

        private static string RenderAdditionalValues(ImmutableArray<AdditionalInput> inputs)
        {
            var source = new StringBuilder();
            source.Append("namespace Rhycol.OpenApiCodeGen.SourceGenerator.Phase0 { internal static class Phase0Additional {");

            foreach (AdditionalInput input in inputs.OrderBy(static input => input.Name, StringComparer.Ordinal))
            {
                source.Append(" internal const string ")
                    .Append(ToIdentifier(input.Name))
                    .Append(" = \"")
                    .Append(EscapeString(input.Content))
                    .Append("\";");
            }

            source.Append(" } }");
            return source.ToString();
        }

        private static string ToIdentifier(string value)
        {
            var identifier = new StringBuilder(value.Length + 1);
            if (value.Length == 0 || !IsIdentifierStart(value[0]))
            {
                identifier.Append('_');
            }

            foreach (char character in value)
            {
                identifier.Append(IsIdentifierPart(character) ? character : '_');
            }

            return identifier.ToString();
        }

        private static bool IsIdentifierStart(char value)
        {
            return value == '_' || char.IsLetter(value);
        }

        private static bool IsIdentifierPart(char value)
        {
            return value == '_' || char.IsLetterOrDigit(value);
        }

        private static string EscapeString(string value)
        {
            var escaped = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '"':
                        escaped.Append("\\\"");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(character))
                        {
                            escaped.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            escaped.Append(character);
                        }

                        break;
                }
            }

            return escaped.ToString();
        }

        private readonly struct AdditionalInput : IEquatable<AdditionalInput>
        {
            private AdditionalInput(string name, string content)
            {
                Name = name;
                Content = content;
            }

            internal string Name { get; }

            internal string Content { get; }

            internal static AdditionalInput From(
                AdditionalText text,
                System.Threading.CancellationToken cancellationToken)
            {
                string fileName = Path.GetFileName(text.Path);
                string name = fileName.Substring(0, fileName.Length - AdditionalFileSuffix.Length);
                string content = (text.GetText(cancellationToken)?.ToString() ?? string.Empty).Trim();
                return new AdditionalInput(name, content);
            }

            public bool Equals(AdditionalInput other)
            {
                return string.Equals(Name, other.Name, StringComparison.Ordinal)
                    && string.Equals(Content, other.Content, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is AdditionalInput other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (StringComparer.Ordinal.GetHashCode(Name) * 397)
                        ^ StringComparer.Ordinal.GetHashCode(Content);
                }
            }
        }
    }
}
