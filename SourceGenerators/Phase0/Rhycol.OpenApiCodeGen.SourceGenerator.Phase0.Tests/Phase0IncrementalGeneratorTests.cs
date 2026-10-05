using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.Tests
{
    public sealed class Phase0IncrementalGeneratorTests
    {
        [Fact]
        public void GeneratesFixedMarker()
        {
            GeneratorDriver driver = CreateDriver(Array.Empty<AdditionalText>());

            GeneratorDriverRunResult result = driver.RunGenerators(CreateCompilation()).GetRunResult();

            string source = GetGeneratedSource(result, "Phase0Fixed.g.cs");
            Assert.Contains("internal const string Marker = \"Phase0\"", source, StringComparison.Ordinal);
        }

        [Fact]
        public void AcceptsOnlyTheExactAnalyzerSuffix()
        {
            var root = new TestAdditionalText(
                "/Assets/Root.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.additionalfile",
                "root-value");
            var scoped = new TestAdditionalText(
                "/Assets/Scoped/Scoped.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.additionalfile",
                " scoped-value ");
            var ignored = new TestAdditionalText(
                "/Assets/Ignored.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.Other.additionalfile",
                "ignored-value");
            var wrongCase = new TestAdditionalText(
                "/Assets/Wrong.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.AdditionalFile",
                "wrong-case");

            GeneratorDriverRunResult result = CreateDriver(new AdditionalText[] { root, scoped, ignored, wrongCase })
                .RunGenerators(CreateCompilation())
                .GetRunResult();

            string source = GetGeneratedSource(result, "Phase0Additional.g.cs");
            Assert.Contains("Root = \"root-value\"", source, StringComparison.Ordinal);
            Assert.Contains("Scoped = \"scoped-value\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ignored-value", source, StringComparison.Ordinal);
            Assert.DoesNotContain("wrong-case", source, StringComparison.Ordinal);
        }

        [Fact]
        public void TracksCachedAndModifiedAdditionalInputs()
        {
            var root = new TestAdditionalText(
                "/Assets/Root.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.additionalfile",
                "root-value");
            var scoped = new TestAdditionalText(
                "/Assets/Scoped/Scoped.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.additionalfile",
                "scoped-value");
            Compilation compilation = CreateCompilation();
            GeneratorDriver driver = CreateDriver(new AdditionalText[] { root, scoped });

            driver = driver.RunGenerators(compilation);
            Assert.Equal(
                new[] { IncrementalStepRunReason.New, IncrementalStepRunReason.New },
                GetReasons(driver.GetRunResult(), "Phase0ParseAdditionalFile"));
            Assert.Equal(
                new[] { IncrementalStepRunReason.New },
                GetReasons(driver.GetRunResult(), "Phase0CollectAdditionalFiles"));

            driver = driver.RunGenerators(compilation);
            Assert.Equal(
                new[] { IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached },
                GetReasons(driver.GetRunResult(), "Phase0ParseAdditionalFile"));
            Assert.Equal(
                new[] { IncrementalStepRunReason.Cached },
                GetReasons(driver.GetRunResult(), "Phase0CollectAdditionalFiles"));

            var changedRoot = new TestAdditionalText(root.Path, "changed-root-value");
            driver = driver.ReplaceAdditionalText(root, changedRoot).RunGenerators(compilation);
            IReadOnlyList<IncrementalStepRunReason> reasons = GetReasons(
                driver.GetRunResult(),
                "Phase0ParseAdditionalFile");

            Assert.Contains(IncrementalStepRunReason.Modified, reasons);
            Assert.Contains(IncrementalStepRunReason.Cached, reasons);
            Assert.Equal(2, reasons.Count);
            Assert.Single(reasons, reason => reason == IncrementalStepRunReason.Modified);
            Assert.Single(reasons, reason => reason == IncrementalStepRunReason.Cached);
            Assert.Equal(
                new[] { IncrementalStepRunReason.Modified },
                GetReasons(driver.GetRunResult(), "Phase0CollectAdditionalFiles"));
            Assert.Contains(
                "Root = \"changed-root-value\"",
                GetGeneratedSource(driver.GetRunResult(), "Phase0Additional.g.cs"),
                StringComparison.Ordinal);
        }

        private static GeneratorDriver CreateDriver(IEnumerable<AdditionalText> additionalTexts)
        {
            return CSharpGeneratorDriver.Create(
                generators: new[] { new Phase0IncrementalGenerator().AsSourceGenerator() },
                additionalTexts: additionalTexts,
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9),
                driverOptions: new GeneratorDriverOptions(
                    disabledOutputs: IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true));
        }

        private static Compilation CreateCompilation()
        {
            return CSharpCompilation.Create(
                "Phase0Consumer",
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static string GetGeneratedSource(GeneratorDriverRunResult result, string hintName)
        {
            GeneratedSourceResult generated = result.Results.Single().GeneratedSources
                .Single(source => source.HintName == hintName);
            return generated.SourceText.ToString();
        }

        private static IReadOnlyList<IncrementalStepRunReason> GetReasons(
            GeneratorDriverRunResult result,
            string trackingName)
        {
            ImmutableArray<IncrementalGeneratorRunStep> steps = result.Results.Single().TrackedSteps[trackingName];
            return steps.SelectMany(static step => step.Outputs)
                .Select(static output => output.Reason)
                .ToArray();
        }

        private sealed class TestAdditionalText : AdditionalText
        {
            private readonly SourceText text;

            internal TestAdditionalText(string path, string content)
            {
                Path = path;
                text = SourceText.From(content);
            }

            public override string Path { get; }

            public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
            {
                return text;
            }
        }
    }
}
