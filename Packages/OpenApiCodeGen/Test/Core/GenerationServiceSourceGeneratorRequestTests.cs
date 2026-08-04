#nullable enable

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.UI;

internal sealed class GenerationServiceSourceGeneratorRequestTests
{
    [Test]
    public void GenerateMapsExistingCSharpApiNameAndPackageNameToProviderNeutralRequest()
    {
        var projectSettingRepository = new StubRepository<ProjectSetting>(
            new ProjectSetting(generateProvider: GenerateProvider.SourceGenerator));
        var cSharpSettingRepository = new StubRepository<GenerationCSharpSetting>(
            new GenerationCSharpSetting
            {
                ApiName = "PetStoreApi",
                PackageName = "Example.Generated.PetStore",
            });
        var provider = new RecordingProvider();
        var registry = new GenerationProviderRegistry();
        Assert.That(registry.TryRegister(provider, out string failureReason), Is.True, failureReason);
        var service = new GenerationService(
            projectSettingRepository,
            cSharpSettingRepository,
            registry);
        var dto = new GenerateApiClientDto(
            GenerateProvider.SourceGenerator,
            "Assets/Specs/petstore.json",
            "Assets/Generated/PetStore");

        service.GenerateApiClientAsync(dto).GetAwaiter().GetResult();

        Assert.That(provider.LastRequest, Is.Not.Null);
        Assert.That(provider.LastRequest!.ApiName, Is.EqualTo("PetStoreApi"));
        Assert.That(
            provider.LastRequest.GeneratedNamespace,
            Is.EqualTo("Example.Generated.PetStore"));
    }

    sealed class RecordingProvider : IGenerationProvider
    {
        public GenerationProviderDescriptor Descriptor { get; } =
            new GenerationProviderDescriptor(
                GenerateProvider.SourceGenerator,
                "Source Generator test provider",
                GenerationProviderAvailability.Available());

        public GenerationRequest? LastRequest { get; private set; }

        public GenerationResult Generate(GenerationRequest request)
        {
            LastRequest = request;
            return GenerationResult.Success();
        }

        public Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(GenerationResult.Success());
        }
    }

    sealed class StubRepository<T> : IAsyncRepository<T> where T : class
    {
        readonly T? value;

        internal StubRepository(T? value)
        {
            this.value = value;
        }

        public Task<T?> ReadAsync()
        {
            return Task.FromResult(value);
        }

        public Task SaveAsync(T newValue)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync()
        {
            return Task.CompletedTask;
        }
    }
}
