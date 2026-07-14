#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;

using UnityEditor.Build;

internal sealed class SourceGeneratorPackageTransitionHandlerTests
{
    static readonly NamedBuildTarget Target = NamedBuildTarget.Standalone;

    [Test]
    public void DetectsAddRemoveAndBothSidesOfUpdate()
    {
        string packageName = SourceGeneratorPackageTransitionHandler.PackageName;
        string[] none = Array.Empty<string>();

        Assert.That(
            SourceGeneratorPackageTransitionHandler.ContainsSourceGeneratorPackage(
                new[] { packageName }, none, none, none),
            Is.True);
        Assert.That(
            SourceGeneratorPackageTransitionHandler.ContainsSourceGeneratorPackage(
                none, new[] { packageName }, none, none),
            Is.True);
        Assert.That(
            SourceGeneratorPackageTransitionHandler.ContainsSourceGeneratorPackage(
                none, none, new[] { packageName }, none),
            Is.True);
        Assert.That(
            SourceGeneratorPackageTransitionHandler.ContainsSourceGeneratorPackage(
                none, none, none, new[] { packageName }),
            Is.True);
    }

    [Test]
    public void IgnoresUnrelatedPackageTransition()
    {
        Assert.That(
            SourceGeneratorPackageTransitionHandler.ContainsSourceGeneratorPackage(
                new[] { "com.example.added" },
                new[] { "com.example.removed" },
                new[] { "com.example.old" },
                new[] { "com.example.new" }),
            Is.False);
    }

    [Test]
    public void DisableUnregistersProviderAndRemovesOnlySourceGeneratorDefines()
    {
        var registry = new GenerationProviderRegistry();
        Assert.That(
            registry.TryRegister(
                new StubGenerationProvider(),
                out string failureReason),
            Is.True,
            failureReason);
        var store = new FakeDefineStore(
            SourceGeneratorDefineSynchronizer.DefineSymbol,
            "EXISTING",
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        var handler = new SourceGeneratorPackageTransitionHandler(registry, store);

        bool changed = handler.DisableSourceGenerator(Target);

        Assert.That(changed, Is.True);
        Assert.That(store.SetCount, Is.EqualTo(1));
        Assert.That(store.Defines, Is.EqualTo(new[] { "EXISTING" }));
        Assert.That(
            registry.Resolve(GenerateProvider.SourceGenerator).IsResolved,
            Is.False);
    }

    [Test]
    public void DisableDoesNotRewriteUnchangedDefines()
    {
        var registry = new GenerationProviderRegistry();
        Assert.That(
            registry.TryRegister(new StubGenerationProvider(), out _),
            Is.True);
        var store = new FakeDefineStore("EXISTING");
        var handler = new SourceGeneratorPackageTransitionHandler(registry, store);

        bool changed = handler.DisableSourceGenerator(Target);

        Assert.That(changed, Is.False);
        Assert.That(store.SetCount, Is.Zero);
        Assert.That(
            registry.Resolve(GenerateProvider.SourceGenerator).IsResolved,
            Is.False);
    }

    sealed class FakeDefineStore : IScriptingDefineStore
    {
        internal string[] Defines { get; private set; }
        internal int SetCount { get; private set; }

        internal FakeDefineStore(params string[] defines)
        {
            Defines = defines;
        }

        public string[] Get(NamedBuildTarget buildTarget)
        {
            return (string[])Defines.Clone();
        }

        public void Set(NamedBuildTarget buildTarget, string[] defines)
        {
            SetCount++;
            Defines = (string[])defines.Clone();
        }
    }

    sealed class StubGenerationProvider : IGenerationProvider
    {
        public GenerationProviderDescriptor Descriptor { get; } =
            new GenerationProviderDescriptor(
                GenerateProvider.SourceGenerator,
                "Source Generator",
                GenerationProviderAvailability.Available());

        public GenerationResult Generate(GenerationRequest request)
        {
            return GenerationResult.Success();
        }

        public Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GenerationResult.Success());
        }
    }
}
