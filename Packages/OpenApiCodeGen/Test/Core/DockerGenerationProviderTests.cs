#nullable enable

using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;

internal sealed class DockerGenerationProviderTests
{
    [Test]
    public void DescriptorUsesPersistedOpenApiProviderValue()
    {
        var provider = new DockerGenerationProvider(
            new RecordingGenerator(),
            new StubRepository<GenerationCSharpSetting>(null),
            new StubRepository<UserSetting>(null));

        Assert.That((int)provider.Descriptor.Provider, Is.EqualTo(0));
        Assert.That(provider.Descriptor.Provider, Is.EqualTo(GenerateProvider.OpenApi));
        Assert.That(provider.Descriptor.DisplayName, Does.Contain("Docker"));
        Assert.That(provider.Descriptor.Availability.IsAvailable, Is.True);
    }

    [Test]
    public void GenerateDelegatesToExistingSynchronousGeneratorWithInternalSettings()
    {
        var cSharpSetting = new GenerationCSharpSetting { ApiName = "RegressionApi" };
        var userSetting = new UserSetting(dockerPath: "/custom/docker");
        var generator = new RecordingGenerator
        {
            SynchronousResponse = new ProcessResponse(ExitStatus.Success, "generated")
        };
        var provider = new DockerGenerationProvider(
            generator,
            new StubRepository<GenerationCSharpSetting>(cSharpSetting),
            new StubRepository<UserSetting>(userSetting));
        var request = new GenerationRequest("spec/openapi.json", "/tmp/generated-client");

        GenerationResult result = provider.Generate(request);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Message, Is.EqualTo("generated"));
        Assert.That(generator.SynchronousCallCount, Is.EqualTo(1));
        Assert.That(generator.AsynchronousCallCount, Is.Zero);
        Assert.That(generator.ProjectSetting!.GenerateProvider, Is.EqualTo(GenerateProvider.OpenApi));
        Assert.That(generator.ProjectSetting.ApiDocumentFilePathOrUrl, Is.EqualTo(request.ApiDocumentFilePathOrUrl));
        Assert.That(generator.ProjectSetting.ApiClientOutputFolderPath, Is.EqualTo(request.OutputFolderPath));
        Assert.That(generator.CSharpSetting, Is.SameAs(cSharpSetting));
        Assert.That(generator.UserSetting, Is.SameAs(userSetting));
    }

    [Test]
    public async Task GenerateAsyncDelegatesToExistingAsynchronousGeneratorAndMapsFailure()
    {
        var cSharpSetting = new GenerationCSharpSetting { ApiName = "AsyncRegressionApi" };
        var userSetting = new UserSetting(dockerPath: "docker-custom");
        var generator = new RecordingGenerator
        {
            AsynchronousResponse = new ProcessResponse(ExitStatus.Error, "docker failed")
        };
        var provider = new DockerGenerationProvider(
            generator,
            new StubRepository<GenerationCSharpSetting>(cSharpSetting),
            new StubRepository<UserSetting>(userSetting));
        var request = new GenerationRequest("https://example.test/openapi.json", "/tmp/async-client");
        using var cancellationSource = new CancellationTokenSource();

        GenerationResult result =
            await provider.GenerateAsync(request, cancellationSource.Token);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("docker failed"));
        Assert.That(generator.SynchronousCallCount, Is.Zero);
        Assert.That(generator.AsynchronousCallCount, Is.EqualTo(1));
        Assert.That(generator.ProjectSetting!.ApiDocumentFilePathOrUrl, Is.EqualTo(request.ApiDocumentFilePathOrUrl));
        Assert.That(generator.ProjectSetting.ApiClientOutputFolderPath, Is.EqualTo(request.OutputFolderPath));
        Assert.That(generator.CSharpSetting, Is.SameAs(cSharpSetting));
        Assert.That(generator.UserSetting, Is.SameAs(userSetting));
        Assert.That(generator.CancellationToken, Is.EqualTo(cancellationSource.Token));
    }

    sealed class RecordingGenerator : IGenerator
    {
        public ProcessResponse SynchronousResponse { get; set; } =
            new ProcessResponse(ExitStatus.Success, string.Empty);
        public ProcessResponse AsynchronousResponse { get; set; } =
            new ProcessResponse(ExitStatus.Success, string.Empty);
        public int SynchronousCallCount { get; private set; }
        public int AsynchronousCallCount { get; private set; }
        public ProjectSetting? ProjectSetting { get; private set; }
        public GenerationCSharpSetting? CSharpSetting { get; private set; }
        public UserSetting? UserSetting { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public ProcessResponse Generate(
            ProjectSetting projectSetting,
            GenerationCSharpSetting cSharpSetting,
            UserSetting userSetting)
        {
            SynchronousCallCount++;
            Capture(projectSetting, cSharpSetting, userSetting);
            return SynchronousResponse;
        }

        public Task<ProcessResponse> GenerateAsync(
            ProjectSetting projectSetting,
            GenerationCSharpSetting cSharpSetting,
            UserSetting userSetting,
            CancellationToken cancellationToken = default)
        {
            AsynchronousCallCount++;
            Capture(projectSetting, cSharpSetting, userSetting);
            CancellationToken = cancellationToken;
            return Task.FromResult(AsynchronousResponse);
        }

        void Capture(
            ProjectSetting projectSetting,
            GenerationCSharpSetting cSharpSetting,
            UserSetting userSetting)
        {
            ProjectSetting = projectSetting;
            CSharpSetting = cSharpSetting;
            UserSetting = userSetting;
        }
    }

    sealed class StubRepository<T> : IAsyncRepository<T> where T : class
    {
        readonly T? _value;

        public StubRepository(T? value)
        {
            _value = value;
        }

        public Task<T?> ReadAsync()
        {
            return Task.FromResult(_value);
        }

        public Task SaveAsync(T value)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync()
        {
            return Task.CompletedTask;
        }
    }
}
