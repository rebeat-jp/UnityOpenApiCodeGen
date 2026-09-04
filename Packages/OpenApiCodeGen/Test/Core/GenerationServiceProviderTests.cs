#nullable enable

using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen;
using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.Lib;
using Rhycol.OpenApiCodeGen.UI;

internal sealed class GenerationServiceProviderTests
{
    [Test]
    public void GenerateReloadsSavedProviderForEveryExecution()
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

        service.GenerateApiClientAsync(dto).GetAwaiter().GetResult();
        projectSettingRepository.Value =
            new ProjectSetting(generateProvider: GenerateProvider.OpenApi);
        service.GenerateApiClientAsync(dto).GetAwaiter().GetResult();

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

        ApplicationServiceException exception = Assert.Throws<ApplicationServiceException>(
            () => service.GenerateApiClientAsync(CreateDto()).GetAwaiter().GetResult())!;

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

        ApplicationServiceException exception = Assert.Throws<ApplicationServiceException>(
            () => service.GenerateApiClientAsync(CreateDto()).GetAwaiter().GetResult())!;

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

        ApplicationServiceException exception = Assert.Throws<ApplicationServiceException>(
            () => service.GenerateApiClientAsync(CreateDto()).GetAwaiter().GetResult())!;

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

        ApplicationServiceException exception = Assert.Throws<ApplicationServiceException>(
            () => service.GenerateApiClientAsync(CreateDto()).GetAwaiter().GetResult())!;

        Assert.That(exception.Message, Does.Contain(failedProvider.Descriptor.DisplayName));
        Assert.That(exception.Message, Does.Contain(failureMessage));
        Assert.That(failedProvider.GenerateAsyncCallCount, Is.EqualTo(1));
        Assert.That(dockerProvider.GenerateAsyncCallCount, Is.Zero);
    }

    [Test]
    public void SuccessfulProviderMessageIsReturnedUnchanged()
    {
        const string successMessage = "Source Generator cache and definition were published.";
        var projectSettingRepository = new MutableProjectSettingRepository(
            new ProjectSetting(generateProvider: GenerateProvider.SourceGenerator));
        var provider = new RecordingProvider(
            GenerateProvider.SourceGenerator,
            result: GenerationResult.Success(successMessage));
        var service = new GenerationService(
            projectSettingRepository,
            CreateRegistry(provider));

        GenerationResult result = service.GenerateApiClientAsync(CreateDto()).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Message, Is.EqualTo(successMessage));
    }

    [Test]
    public void ProjectSettingsDoNotPersistRemoteCredentialsQueryOrFragment()
    {
        string settingsPath = Path.Combine(
            ApplicationConstant.PROJECT_FOLDER_PATH,
            "projectSettings.json");
        bool hadExistingSettings = File.Exists(settingsPath);
        byte[] existingSettings = hadExistingSettings
            ? File.ReadAllBytes(settingsPath)
            : new byte[0];

        try
        {
            var repository = new ProjectSettingJsonRepository();
            ProjectSetting restored = Task.Run(async () =>
                {
                    await repository.SaveAsync(
                        new ProjectSetting(
                            GenerateProvider.SourceGenerator,
                            "http://user:password@127.0.0.1/openapi.json?token=secret#fragment",
                            "Assets/Generated"));
                    return await repository.ReadAsync();
                })
                .GetAwaiter()
                .GetResult()!;

            string persisted = File.ReadAllText(settingsPath);

            Assert.That(persisted, Does.Not.Contain("user"));
            Assert.That(persisted, Does.Not.Contain("password"));
            Assert.That(persisted, Does.Not.Contain("token"));
            Assert.That(persisted, Does.Not.Contain("secret"));
            Assert.That(persisted, Does.Not.Contain("fragment"));
            Assert.That(
                restored.ApiDocumentFilePathOrUrl,
                Is.EqualTo("http://127.0.0.1/openapi.json"));
        }
        finally
        {
            if (hadExistingSettings)
            {
                File.WriteAllBytes(settingsPath, existingSettings);
            }
            else if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
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
