#nullable enable

using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen.Editor.Generation;

internal sealed class GenerationProviderRegistryTests
{
    [Test]
    public void RegisterAndResolveReturnsProvider()
    {
        var registry = new GenerationProviderRegistry();
        var provider = new StubGenerationProvider(GenerateProvider.OpenApi);

        bool registered = registry.TryRegister(provider, out string failureReason);
        GenerationProviderResolution resolution = registry.Resolve(GenerateProvider.OpenApi);

        Assert.That(registered, Is.True, failureReason);
        Assert.That(failureReason, Is.Empty);
        Assert.That(resolution.IsResolved, Is.True);
        Assert.That(resolution.Provider, Is.SameAs(provider));
        Assert.That(resolution.FailureReason, Is.Empty);
    }

    [Test]
    public void DuplicateRegistrationKeepsFirstProvider()
    {
        var registry = new GenerationProviderRegistry();
        var first = new StubGenerationProvider(GenerateProvider.OpenApi);
        var duplicate = new StubGenerationProvider(GenerateProvider.OpenApi);

        Assert.That(registry.TryRegister(first, out _), Is.True);
        bool registered = registry.TryRegister(duplicate, out string failureReason);

        Assert.That(registered, Is.False);
        Assert.That(failureReason, Does.Contain("already registered"));
        Assert.That(registry.Resolve(GenerateProvider.OpenApi).Provider, Is.SameAs(first));
    }

    [Test]
    public void UnknownProviderCannotBeRegisteredOrResolved()
    {
        var registry = new GenerationProviderRegistry();
        var provider = new StubGenerationProvider((GenerateProvider)99);

        bool registered = registry.TryRegister(provider, out string registrationFailure);
        GenerationProviderResolution resolution = registry.Resolve((GenerateProvider)99);

        Assert.That(registered, Is.False);
        Assert.That(registrationFailure, Does.Contain("Unknown generation provider value: 99"));
        Assert.That(resolution.IsResolved, Is.False);
        Assert.That(resolution.FailureReason, Does.Contain("Unknown generation provider value: 99"));
    }

    [Test]
    public void KnownButUnregisteredProviderReturnsExplicitFailure()
    {
        var registry = new GenerationProviderRegistry();

        GenerationProviderResolution resolution = registry.Resolve(GenerateProvider.SourceGenerator);

        Assert.That(resolution.IsResolved, Is.False);
        Assert.That(resolution.FailureReason, Does.Contain("is not registered"));
    }

    [Test]
    public void RegisteredUnavailableProviderRemainsResolvable()
    {
        var registry = new GenerationProviderRegistry();
        var provider = new StubGenerationProvider(
            GenerateProvider.SourceGenerator,
            GenerationProviderAvailability.Unavailable("Analyzer is missing."));

        Assert.That(registry.TryRegister(provider, out _), Is.True);

        GenerationProviderResolution resolution = registry.Resolve(GenerateProvider.SourceGenerator);
        Assert.That(resolution.IsResolved, Is.True);
        Assert.That(resolution.Provider!.Descriptor.Availability.IsAvailable, Is.False);
        Assert.That(resolution.Provider.Descriptor.Availability.Reason, Is.EqualTo("Analyzer is missing."));
    }

    sealed class StubGenerationProvider : IGenerationProvider
    {
        public GenerationProviderDescriptor Descriptor { get; }

        public StubGenerationProvider(
            GenerateProvider provider,
            GenerationProviderAvailability? availability = null)
        {
            Descriptor = new GenerationProviderDescriptor(
                provider,
                provider.ToString(),
                availability ?? GenerationProviderAvailability.Available());
        }

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
