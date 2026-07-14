#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.Presenter;
using Rhycol.OpenApiCodeGen.UI;

namespace Rhycol.OpenApiCodeGen.Test.Core
{
    internal sealed class MenuPresenterProviderRefreshTests
    {
        [Test]
        public void SavedProjectSettingRefreshesOnlyProviderPresentation()
        {
            var repository = new MutableProjectSettingRepository(
                new ProjectSetting(
                    GenerateProvider.OpenApi,
                    "saved/openapi.json",
                    "saved/output"));
            var presenter = new MenuPresenter(
                new GenerationService(repository, new GenerationProviderRegistry()));
            var view = new RecordingGenerationView();

            try
            {
                presenter.Bind(view);
                AwaitWithTimeout(view.FormSet.Task);

                view.RaiseGenerateSettingChanged(
                    new GenerateApiClientDto(
                        GenerateProvider.OpenApi,
                        "draft/openapi.json",
                        "draft/output"));
                repository.Value = new ProjectSetting(
                    GenerateProvider.SourceGenerator,
                    "different/saved/openapi.json",
                    "different/saved/output");

                ProjectSettingChangeNotification.PublishSaved();

                GenerateProvider displayedProvider =
                    AwaitWithTimeout(view.ProviderSet.Task);
                Assert.That(displayedProvider, Is.EqualTo(GenerateProvider.SourceGenerator));
                Assert.That(view.SetFormValueCallCount, Is.EqualTo(1));
            }
            finally
            {
                presenter.Unbind();
            }
        }

        [Test]
        public void GenerateReloadsSavedProviderBeforeRoutingAndRefreshesPresentation()
        {
            var repository = new MutableProjectSettingRepository(
                new ProjectSetting(GenerateProvider.OpenApi));
            var dockerProvider = new RecordingProvider(GenerateProvider.OpenApi);
            var sourceGeneratorProvider =
                new RecordingProvider(GenerateProvider.SourceGenerator);
            var registry = CreateRegistry(dockerProvider, sourceGeneratorProvider);
            var presenter = new MenuPresenter(new GenerationService(repository, registry));
            var view = new RecordingGenerationView();

            try
            {
                presenter.Bind(view);
                AwaitWithTimeout(view.FormSet.Task);

                repository.Value = new ProjectSetting(GenerateProvider.SourceGenerator);
                view.RaiseGenerateRequested(
                    new GenerateApiClientDto(
                        GenerateProvider.OpenApi,
                        "draft/openapi.json",
                        "draft/output"));

                AwaitWithTimeout(view.GenerationSucceeded.Task);

                Assert.That(view.LastDisplayedProvider, Is.EqualTo(GenerateProvider.SourceGenerator));
                Assert.That(sourceGeneratorProvider.GenerateAsyncCallCount, Is.EqualTo(1));
                Assert.That(dockerProvider.GenerateAsyncCallCount, Is.Zero);
                Assert.That(
                    sourceGeneratorProvider.LastRequest?.ApiDocumentFilePathOrUrl,
                    Is.EqualTo("draft/openapi.json"));
                Assert.That(
                    sourceGeneratorProvider.LastRequest?.OutputFolderPath,
                    Is.EqualTo(System.IO.Path.GetFullPath("draft/output")));
            }
            finally
            {
                presenter.Unbind();
            }
        }

        static GenerationProviderRegistry CreateRegistry(
            params IGenerationProvider[] providers)
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

        static T AwaitWithTimeout<T>(Task<T> task)
        {
            Assert.That(
                task.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "Timed out waiting for presenter update.");
            return task.GetAwaiter().GetResult();
        }

        sealed class RecordingGenerationView : IGenerationView
        {
            public event Action<GenerateApiClientDto>? GenerateRequested;
            public event Action<GenerateApiClientDto>? GenerateSettingChanged;

            public TaskCompletionSource<GenerateApiClientDto> FormSet { get; } = new();
            public TaskCompletionSource<GenerateProvider> ProviderSet { get; } = new();
            public TaskCompletionSource<bool> GenerationSucceeded { get; } = new();

            public int SetFormValueCallCount { get; private set; }
            public GenerateProvider? LastDisplayedProvider { get; private set; }

            public void RaiseGenerateRequested(GenerateApiClientDto dto)
            {
                GenerateRequested?.Invoke(dto);
            }

            public void RaiseGenerateSettingChanged(GenerateApiClientDto dto)
            {
                GenerateSettingChanged?.Invoke(dto);
            }

            public void SetFormValue(GenerateApiClientDto generateMenuDto)
            {
                SetFormValueCallCount++;
                LastDisplayedProvider = generateMenuDto.GenerateProvider;
                FormSet.TrySetResult(generateMenuDto);
            }

            public void SetGenerateProvider(GenerateProvider generateProvider)
            {
                LastDisplayedProvider = generateProvider;
                ProviderSet.TrySetResult(generateProvider);
            }

            public void SetGenerateStatus(IProgressStatus generateStatus)
            {
                if (generateStatus is SucceedProgressStatus)
                {
                    GenerationSucceeded.TrySetResult(true);
                }
            }

            public void SetDocumentFilePathComment(string comment)
            {
            }

            public void SetOutputPathComment(string comment)
            {
            }

            public void SetInputEnabled(bool isEnabled)
            {
            }
        }

        sealed class RecordingProvider : IGenerationProvider
        {
            public GenerationProviderDescriptor Descriptor { get; }
            public int GenerateAsyncCallCount { get; private set; }
            public GenerationRequest? LastRequest { get; private set; }

            public RecordingProvider(GenerateProvider provider)
            {
                Descriptor = new GenerationProviderDescriptor(
                    provider,
                    $"Test {provider}",
                    GenerationProviderAvailability.Available());
            }

            public GenerationResult Generate(GenerationRequest request)
            {
                LastRequest = request;
                return GenerationResult.Success();
            }

            public Task<GenerationResult> GenerateAsync(
                GenerationRequest request,
                CancellationToken cancellationToken = default)
            {
                GenerateAsyncCallCount++;
                LastRequest = request;
                return Task.FromResult(GenerationResult.Success());
            }
        }

        sealed class MutableProjectSettingRepository : IAsyncRepository<ProjectSetting>
        {
            public ProjectSetting? Value { get; set; }

            public MutableProjectSettingRepository(ProjectSetting? value)
            {
                Value = value;
            }

            public Task<ProjectSetting?> ReadAsync()
            {
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
}
