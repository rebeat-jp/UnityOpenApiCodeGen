#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.UI;

internal sealed class GenerationServiceProviderTests
{
    [Test]
    public async Task GenerateReloadsSavedProviderForEveryExecution()
    {
        var projectSettingRepository = new MutableProjectSettingRepository(
            new ProjectSetting(generateProvider: GenerateProvider.SourceGenerator));
        var dockerProvider = new RecordingProvider(GenerateProvider.OpenApi);
        var sourceGeneratorProvider = new RecordingProvider(GenerateProvider.SourceGenerator);
        var registry = CreateRegistry(dockerProvider, sourceGeneratorProvider);
        var service = new GenerationService(projectSettingRepository, registry);
        var dto = new GenerateApiClientDto(
            generateProvider: GenerateProvider.OpenApi,
            apiDocumentFilePathOrUrl: "spec/openapi.json",
            apiClientOutputFolderPath: "GeneratedClient");

        await service.GenerateApiClientAsync(dto);
        projectSettingRepository.Value =
            new ProjectSetting(generateProvider: GenerateProvider.OpenApi);
        await service.GenerateApiClientAsync(dto);

        Assert.That(projectSettingRepository.ReadCount, Is.EqualTo(2));
        Assert.That(sourceGeneratorProvider.GenerateAsyncCallCount, Is.EqualTo(1));
        Assert.That(dockerProvider.GenerateAsyncCallCount, Is.EqualTo(1));
        Assert.That(sourceGeneratorProvider.LastRequest!.ApiDocumentFilePathOrUrl, Is.EqualTo("spec/openapi.json"));
        Assert.That(
            sourceGeneratorProvider.LastRequest.OutputFolderPath,
            Is.EqualTo(Path.GetFullPath("GeneratedClient")));
    }

    [Test]
    public void UnknownSavedProviderDoesNotFallbackToDocker()
    {
        var projectSettingRepository = new MutableProjectSettingRepository(
            new ProjectSetting(generateProvider: (GenerateProvider)99));
        var dockerProvider = new RecordingProvider(GenerateProvider.OpenApi);
        var service = new GenerationService(
            projectSettingRepository,
            CreateRegistry(dockerProvider));

        ApplicationServiceException exception = Assert.ThrowsAsync<ApplicationServiceException>(
            async () => await service.GenerateApiClientAsync(CreateDto()))!;

        Assert.That(exception.Message, Does.Contain("Unknown generation provider value: 99"));
        Assert.That(dockerProvider.GenerateAsyncCallCount, Is.Zero);
    }

    [Test]
    public void UnregisteredSavedProviderDoesNotFallbackToDocker()
    {
        var projectSettingRepository = new MutableProjectSettingRepository(
            new ProjectSetting(generateProvider: GenerateProvider.SourceGenerator));
        var dockerProvider = new RecordingProvider(GenerateProvider.OpenApi);
        var service = new GenerationService(
            projectSettingRepository,
            CreateRegistry(dockerProvider));

        ApplicationServiceException exception = Assert.ThrowsAsync<ApplicationServiceException>(
            async () => await service.GenerateApiClientAsync(CreateDto()))!;

        Assert.That(exception.Message, Does.Contain("SourceGenerator"));
        Assert.That(exception.Message, Does.Contain("is not registered"));
        Assert.That(dockerProvider.GenerateAsyncCallCount, Is.Zero);
    }

    [Test]
    public void UnavailableSavedProviderReturnsItsReasonWithoutGenerating()
    {
        const string reason = "Analyzer metadata is missing.";
        var projectSettingRepository = new MutableProjectSettingRepository(
            new ProjectSetting(generateProvider: GenerateProvider.SourceGenerator));
        var unavailableProvider = new RecordingProvider(
            GenerateProvider.SourceGenerator,
            GenerationProviderAvailability.Unavailable(reason));
        var service = new GenerationService(
            projectSettingRepository,
            CreateRegistry(unavailableProvider));

        ApplicationServiceException exception = Assert.ThrowsAsync<ApplicationServiceException>(
            async () => await service.GenerateApiClientAsync(CreateDto()))!;

        Assert.That(exception.Message, Does.Contain(reason));
        Assert.That(unavailableProvider.GenerateAsyncCallCount, Is.Zero);
    }

    [Test]
    public void FailedProviderResultIsReportedWithoutFallback()
    {
        const string failureMessage = "Provider generation failed.";
        var projectSettingRepository = new MutableProjectSettingRepository(
            new ProjectSetting(generateProvider: GenerateProvider.SourceGenerator));
        var dockerProvider = new RecordingProvider(GenerateProvider.OpenApi);
        var failedProvider = new RecordingProvider(
            GenerateProvider.SourceGenerator,
            result: GenerationResult.Failure(failureMessage));
        var service = new GenerationService(
            projectSettingRepository,
            CreateRegistry(dockerProvider, failedProvider));

        ApplicationServiceException exception = Assert.ThrowsAsync<ApplicationServiceException>(
            async () => await service.GenerateApiClientAsync(CreateDto()))!;

        Assert.That(exception.Message, Does.Contain(failedProvider.Descriptor.DisplayName));
        Assert.That(exception.Message, Does.Contain(failureMessage));
        Assert.That(failedProvider.GenerateAsyncCallCount, Is.EqualTo(1));
        Assert.That(dockerProvider.GenerateAsyncCallCount, Is.Zero);
    }

    static GenerateApiClientDto CreateDto()
    {
        return new GenerateApiClientDto(
            generateProvider: GenerateProvider.OpenApi,
            apiDocumentFilePathOrUrl: "openapi.json",
            apiClientOutputFolderPath: "GeneratedClient");
    }

    static GenerationProviderRegistry CreateRegistry(params IGenerationProvider[] providers)
    {
        var registry = new GenerationProviderRegistry();
        foreach (IGenerationProvider provider in providers)
        {
            Assert.That(
                registry.TryRegister(provider, out string failureReason),
                Is.True,
                failureReason);
        }

        return registry;
    }

    sealed class RecordingProvider : IGenerationProvider
    {
        readonly GenerationResult _result;

        public GenerationProviderDescriptor Descriptor { get; }
        public int GenerateAsyncCallCount { get; private set; }
        public GenerationRequest? LastRequest { get; private set; }

        public RecordingProvider(
            GenerateProvider provider,
            GenerationProviderAvailability? availability = null,
            GenerationResult? result = null)
        {
            Descriptor = new GenerationProviderDescriptor(
                provider,
                $"Test {provider}",
                availability ?? GenerationProviderAvailability.Available());
            _result = result ?? GenerationResult.Success();
        }

        public GenerationResult Generate(GenerationRequest request)
        {
            LastRequest = request;
            return _result;
        }

        public Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            GenerateAsyncCallCount++;
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    sealed class MutableProjectSettingRepository : IAsyncRepository<ProjectSetting>
    {
        public ProjectSetting? Value { get; set; }
        public int ReadCount { get; private set; }

        public MutableProjectSettingRepository(ProjectSetting? value)
        {
            Value = value;
        }

        public Task<ProjectSetting?> ReadAsync()
        {
            ReadCount++;
            return Task.FromResult(Value);
        }

        public Task SaveAsync(ProjectSetting value)
        {
            Value = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync()
        {
            Value = null;
            return Task.CompletedTask;
        }
    }
}
