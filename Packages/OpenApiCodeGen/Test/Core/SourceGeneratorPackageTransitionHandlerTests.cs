#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;

using UnityEditor;
using UnityEditor.Build;

using UnityEngine;
using UnityEngine.TestTools;

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
    public void OnlyRemovalWithoutReplacementPurgesAllTargets()
    {
        string packageName = SourceGeneratorPackageTransitionHandler.PackageName;
        string[] none = Array.Empty<string>();

        Assert.That(
            SourceGeneratorPackageTransitionHandler.IsSourceGeneratorPackageRemoval(
                none, new[] { packageName }, none),
            Is.True);
        Assert.That(
            SourceGeneratorPackageTransitionHandler.IsSourceGeneratorPackageRemoval(
                new[] { packageName }, new[] { packageName }, none),
            Is.False);
        Assert.That(
            SourceGeneratorPackageTransitionHandler.IsSourceGeneratorPackageRemoval(
                none, new[] { packageName }, new[] { packageName }),
            Is.False);
        Assert.That(
            SourceGeneratorPackageTransitionHandler.IsSourceGeneratorPackageRemoval(
                none, none, none),
            Is.False);
    }

    [Test]
    public void KnownTargetsIncludeServerAndOtherPlatformsWithoutUnknownOrDuplicates()
    {
        IReadOnlyList<NamedBuildTarget> targets =
            SourceGeneratorPackageTransitionHandler.GetKnownBuildTargets();

        Assert.That(targets, Does.Contain(NamedBuildTarget.Standalone));
        Assert.That(targets, Does.Contain(NamedBuildTarget.Server));
        Assert.That(targets, Does.Contain(NamedBuildTarget.Android));
        Assert.That(targets.Contains(NamedBuildTarget.Unknown), Is.False);
        Assert.That(targets.Distinct().Count(), Is.EqualTo(targets.Count));
    }

    [Test]
    public void KnownTargetsIncludeValidGroupsWithoutPublicStaticMembers()
    {
        IReadOnlyList<NamedBuildTarget> targets =
            SourceGeneratorPackageTransitionHandler.GetKnownBuildTargets();
        int validatedGroups = 0;
        foreach (string groupName in new[]
        {
            "GameCoreScarlett", "GameCoreXboxOne", "Lumin", "Kepler"
        })
        {
            if (!Enum.TryParse(groupName, out BuildTargetGroup group))
            {
                continue;
            }

            NamedBuildTarget target;
            try
            {
                target = NamedBuildTarget.FromBuildTargetGroup(group);
            }
            catch (ArgumentException)
            {
                continue;
            }
            catch (NotSupportedException)
            {
                continue;
            }

            if (target == NamedBuildTarget.Unknown
                || string.IsNullOrEmpty(target.TargetName))
            {
                continue;
            }

            Assert.That(targets.Contains(target), Is.True,
                $"{groupName} was omitted from known targets.");
            validatedGroups++;
        }

        Assert.That(validatedGroups, Is.GreaterThan(0));
    }

    [Test]
    public void RemovalPurgesEveryTargetAndPreservesUnrelatedDefines()
    {
        var registry = new GenerationProviderRegistry();
        Assert.That(registry.TryRegister(new StubGenerationProvider(), out _), Is.True);
        var store = new FakeDefineStore("ACTIVE");
        store.SetInitial(NamedBuildTarget.Server,
            SourceGeneratorDefineSynchronizer.DefineSymbol,
            "SERVER",
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        store.SetInitial(NamedBuildTarget.Android,
            "ANDROID",
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        var handler = new SourceGeneratorPackageTransitionHandler(registry, store);

        bool changed = handler.DisableSourceGeneratorForTargets(new[]
        {
            NamedBuildTarget.Standalone,
            NamedBuildTarget.Server,
            NamedBuildTarget.Android,
            NamedBuildTarget.Server
        });

        Assert.That(changed, Is.True);
        Assert.That(store.Defines, Is.EqualTo(new[] { "ACTIVE" }));
        Assert.That(store.Get(NamedBuildTarget.Server), Is.EqualTo(new[] { "SERVER" }));
        Assert.That(store.Get(NamedBuildTarget.Android), Is.EqualTo(new[] { "ANDROID" }));
        Assert.That(store.SetCount, Is.EqualTo(2));
        Assert.That(registry.Resolve(GenerateProvider.SourceGenerator).IsResolved,
            Is.False);
    }

    [Test]
    public void SingleTargetTransitionLeavesOtherTargetDefinesIntact()
    {
        var store = new FakeDefineStore(
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        store.SetInitial(NamedBuildTarget.Server,
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        var handler = new SourceGeneratorPackageTransitionHandler(
            new GenerationProviderRegistry(), store);

        handler.DisableSourceGenerator(NamedBuildTarget.Standalone);

        Assert.That(store.Defines, Is.Empty);
        Assert.That(store.Get(NamedBuildTarget.Server),
            Is.EqualTo(new[] { SourceGeneratorDefineSynchronizer.DefineSymbol }));
        Assert.That(store.SetCount, Is.EqualTo(1));
    }

    [Test]
    public void UnreadableTargetDoesNotPreventLaterTargetsFromBeingCleared()
    {
        var store = new FakeDefineStore(
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        store.SetInitial(NamedBuildTarget.Server,
            SourceGeneratorDefineSynchronizer.DefineSymbol,
            "SERVER");
        store.SetInitial(NamedBuildTarget.iOS,
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        store.UnsupportedTargets.Add(NamedBuildTarget.iOS);
        var handler = new SourceGeneratorPackageTransitionHandler(
            new GenerationProviderRegistry(), store);
        LogAssert.Expect(
            LogType.Warning,
            "OpenApiCodeGen skipped unsupported build target "
            + $"{NamedBuildTarget.iOS.TargetName}: Unsupported build target.");

        bool changed = handler.DisableSourceGeneratorForTargets(new[]
        {
            NamedBuildTarget.iOS,
            NamedBuildTarget.Standalone,
            NamedBuildTarget.Server
        });

        Assert.That(changed, Is.True);
        Assert.That(store.Defines, Is.Empty);
        Assert.That(store.Get(NamedBuildTarget.Server), Is.EqualTo(new[] { "SERVER" }));
        Assert.That(store.SetCount, Is.EqualTo(2));
    }

    [Test]
    public void FailedWriteIsReportedInsteadOfBeingSkipped()
    {
        var store = new FakeDefineStore(
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        store.SetInitial(NamedBuildTarget.iOS,
            SourceGeneratorDefineSynchronizer.DefineSymbol);
        store.UnsupportedSetTargets.Add(NamedBuildTarget.iOS);
        var handler = new SourceGeneratorPackageTransitionHandler(
            new GenerationProviderRegistry(), store);

        Assert.Throws<ArgumentException>(() =>
            handler.DisableSourceGeneratorForTargets(new[]
            {
                NamedBuildTarget.iOS,
                NamedBuildTarget.Standalone
            }));
        Assert.That(store.Defines,
            Is.EqualTo(new[] { SourceGeneratorDefineSynchronizer.DefineSymbol }));
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
        readonly Dictionary<NamedBuildTarget, string[]> _defines =
            new Dictionary<NamedBuildTarget, string[]>();

        internal string[] Defines => Get(Target);
        internal int SetCount { get; private set; }
        internal HashSet<NamedBuildTarget> UnsupportedTargets { get; } =
            new HashSet<NamedBuildTarget>();
        internal HashSet<NamedBuildTarget> UnsupportedSetTargets { get; } =
            new HashSet<NamedBuildTarget>();

        internal FakeDefineStore(params string[] defines)
        {
            SetInitial(Target, defines);
        }

        internal void SetInitial(NamedBuildTarget buildTarget, params string[] defines)
        {
            _defines[buildTarget] = (string[])defines.Clone();
        }

        public string[] Get(NamedBuildTarget buildTarget)
        {
            if (UnsupportedTargets.Contains(buildTarget))
            {
                throw new ArgumentException("Unsupported build target.");
            }

            return _defines.TryGetValue(buildTarget, out string[] defines)
                ? (string[])defines.Clone()
                : Array.Empty<string>();
        }

        public void Set(NamedBuildTarget buildTarget, string[] defines)
        {
            if (UnsupportedSetTargets.Contains(buildTarget))
            {
                throw new ArgumentException("Unsupported build target.");
            }

            SetCount++;
            _defines[buildTarget] = (string[])defines.Clone();
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
