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
        QueuedContext? testContext;

        [SetUp]
        public void SetUpContext() { testContext = new QueuedContext(); }

        [TearDown]
        public void RestoreContext() { testContext?.Dispose(); }

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

        [Test]
        public void GeneratePropagatesProviderSuccessMessageToSucceedProgressStatus()
        {
            const string successMessage = "Source Generator cache and definition were published.";
            var repository = new MutableProjectSettingRepository(
                new ProjectSetting(GenerateProvider.SourceGenerator));
            var provider = new RecordingProvider(
                GenerateProvider.SourceGenerator,
                GenerationResult.Success(successMessage));
            var presenter = new MenuPresenter(
                new GenerationService(repository, CreateRegistry(provider)));
            var view = new RecordingGenerationView();

            try
            {
                presenter.Bind(view);
                AwaitWithTimeout(view.FormSet.Task);
                view.RaiseGenerateRequested(
                    new GenerateApiClientDto(
                        GenerateProvider.SourceGenerator,
                        "draft/openapi.json",
                        "draft/output"));

                AwaitWithTimeout(view.GenerationSucceeded.Task);

                Assert.That(view.LastSuccessMessage, Is.EqualTo(successMessage));
            }
            finally
            {
                presenter.Unbind();
            }
        }

        [Test]
        public void GenerateDisplaysWarningsAsSeparateWarningStatus()
        {
            const string successMessage = "Source Generator cache and definition were published.";
            const string warningMessage = "The URL query was not persisted.";
            var repository = new MutableProjectSettingRepository(
                new ProjectSetting(GenerateProvider.SourceGenerator));
            var provider = new RecordingProvider(
                GenerateProvider.SourceGenerator,
                GenerationResult.Success(successMessage, new[] { warningMessage }));
            var presenter = new MenuPresenter(
                new GenerationService(repository, CreateRegistry(provider)));
            var view = new RecordingGenerationView();

            try
            {
                presenter.Bind(view);
                AwaitWithTimeout(view.FormSet.Task);
                view.RaiseGenerateRequested(
                    new GenerateApiClientDto(
                        GenerateProvider.SourceGenerator,
                        "draft/openapi.json",
                        "draft/output"));

                AwaitWithTimeout(view.GenerationFinished.Task);

                Assert.That(view.LastWarningMessage, Is.EqualTo(warningMessage));
                Assert.That(view.LastSuccessMessage, Is.Null);
            }
            finally
            {
                presenter.Unbind();
            }
        }

        [Test]
        public void GenerateIgnoresASecondRequestWhileTheFirstGenerationIsInFlight()
        {
            var repository = new MutableProjectSettingRepository(
                new ProjectSetting(GenerateProvider.SourceGenerator));
            var provider = new BlockingProvider(GenerateProvider.SourceGenerator);
            var presenter = new MenuPresenter(
                new GenerationService(repository, CreateRegistry(provider)));
            var view = new RecordingGenerationView();
            GenerateApiClientDto request = new GenerateApiClientDto(
                GenerateProvider.SourceGenerator,
                "draft/openapi.json",
                "draft/output");

            try
            {
                presenter.Bind(view);
                AwaitWithTimeout(view.FormSet.Task);
                view.RaiseGenerateRequested(request);
                AwaitWithTimeout(provider.Started.Task);

                view.RaiseGenerateRequested(request);

                Assert.That(provider.GenerateAsyncCallCount, Is.EqualTo(1));
                provider.Completion.TrySetResult(GenerationResult.Success("completed"));
                AwaitWithTimeout(view.GenerationFinished.Task);
            }
            finally
            {
                presenter.Unbind();
            }
        }

        [Test]
        public void CancelRequestCancelsTheInFlightProviderAndRestoresTheView()
        {
            var repository = new MutableProjectSettingRepository(
                new ProjectSetting(GenerateProvider.SourceGenerator));
            var provider = new BlockingProvider(GenerateProvider.SourceGenerator);
            var presenter = new MenuPresenter(
                new GenerationService(repository, CreateRegistry(provider)));
            var view = new RecordingGenerationView();
            GenerateApiClientDto request = new GenerateApiClientDto(
                GenerateProvider.SourceGenerator,
                "draft/openapi.json",
                "draft/output");

            try
            {
                presenter.Bind(view);
                AwaitWithTimeout(view.FormSet.Task);
                view.RaiseGenerateRequested(request);
                AwaitWithTimeout(provider.Started.Task);

                view.RaiseCancelRequested();
                AwaitWithTimeout(view.GenerationFinished.Task);

                Assert.That(provider.LastCancellationToken.IsCancellationRequested, Is.True);
                Assert.That(view.LastCanceledMessage, Is.EqualTo("Generating was canceled."));
                Assert.That(view.LastInputEnabled, Is.True);
                Assert.That(view.LastCancelEnabled, Is.False);
            }
            finally
            {
                presenter.Unbind();
            }
        }

        [Test]
        public void UnbindSuppressesAProviderCompletionThatArrivesAfterTheViewIsDetached()
        {
            var repository = new MutableProjectSettingRepository(
                new ProjectSetting(GenerateProvider.SourceGenerator));
            var provider = new BlockingProvider(GenerateProvider.SourceGenerator);
            var presenter = new MenuPresenter(
                new GenerationService(repository, CreateRegistry(provider)));
            var view = new RecordingGenerationView();
            GenerateApiClientDto request = new GenerateApiClientDto(
                GenerateProvider.SourceGenerator,
                "draft/openapi.json",
                "draft/output");

            presenter.Bind(view);
            AwaitWithTimeout(view.FormSet.Task);
            view.RaiseGenerateRequested(request);
            AwaitWithTimeout(provider.Started.Task);
            int statusCountBeforeUnbind = view.StatusCount;

            presenter.Unbind();
            provider.Completion.TrySetResult(GenerationResult.Success("late completion"));
            testContext!.Drain();
            Assert.That(provider.Completion.Task.IsCompleted, Is.True);
            Assert.That(view.StatusCount, Is.EqualTo(statusCountBeforeUnbind));
        }

        [TestCase("success")]
        [TestCase("failure")]
        [TestCase("cancel")]
        public void RebindIgnoresThePreviousOperationsTerminalStatus(string outcome)
        {
            using (var context = new QueuedContext())
            {
                var provider = new DeferredProvider();
                var presenter = new MenuPresenter(new GenerationService(
                    new MutableProjectSettingRepository(new ProjectSetting(GenerateProvider.SourceGenerator)),
                    CreateRegistry(provider)));
                var first = new RecordingGenerationView();
                var second = new RecordingGenerationView();
                try
                {
                    presenter.Bind(first);
                    Task operation = StartOperation(presenter, first);
                    presenter.Unbind();
                    presenter.Bind(second);
                    int count = second.StatusCount;
                    if (outcome == "cancel") provider.Completion.SetCanceled();
                    else provider.Completion.SetResult(outcome == "failure"
                        ? GenerationResult.Failure("old failure") : GenerationResult.Success("old success"));
                    context.PumpUntil(() => operation.IsCompleted);
                    operation.GetAwaiter().GetResult();
                    Assert.That(second.StatusCount, Is.EqualTo(count));
                    Assert.That(second.LastInputEnabled, Is.True);
                    Assert.That(second.LastCancelEnabled, Is.Not.True);
                }
                finally { presenter.Unbind(); }
            }
        }

        [Test]
        public void QueuedProgressCannotOverwriteCompletionOrTheNextOperation()
        {
            using (var context = new QueuedContext())
            {
                var provider = new DeferredProvider();
                var presenter = new MenuPresenter(new GenerationService(
                    new MutableProjectSettingRepository(new ProjectSetting(GenerateProvider.SourceGenerator)),
                    CreateRegistry(provider)));
                var view = new RecordingGenerationView();
                try
                {
                    presenter.Bind(view);
                    Task first = StartOperation(presenter, view);
                    // Progress<T> posts even when the report originates on the UI thread.
                    ReportProgress(provider.LastRequest!, "old progress");
                    provider.Completion.SetResult(GenerationResult.Success("completed"));
                    Assert.That(first.IsCompleted, Is.True);
                    int count = view.StatusCount;
                    context.Drain();
                    Assert.That(view.StatusCount, Is.EqualTo(count));
                    GenerationRequest oldRequest = provider.LastRequest;
                    provider.Completion = new TaskCompletionSource<GenerationResult>();
                    Task second = StartOperation(presenter, view);
                    count = view.StatusCount;
                    ReportProgress(oldRequest, "late old progress");
                    context.Drain();
                    Assert.That(view.StatusCount, Is.EqualTo(count));
                    provider.Completion.SetResult(GenerationResult.Success());
                    context.PumpUntil(() => second.IsCompleted);
                }
                finally { presenter.Unbind(); }
            }
        }

        [Test]
        public void RebindIgnoresThePreviousPendingSettingsLoad()
        {
            using (var context = new QueuedContext())
            {
                var repository = new DeferredRepository();
                var presenter = new MenuPresenter(new GenerationService(repository, new GenerationProviderRegistry()));
                var first = new RecordingGenerationView();
                var second = new RecordingGenerationView();
                try
                {
                    presenter.Bind(first);
                    presenter.Bind(second);
                    repository.Reads[0].SetResult(new ProjectSetting(GenerateProvider.OpenApi, "old", "old"));
                    context.Drain();
                    Assert.That(second.SetFormValueCallCount, Is.Zero);
                    Assert.That(second.LastInputEnabled, Is.False);
                    repository.Reads[1].SetResult(new ProjectSetting(GenerateProvider.SourceGenerator, "new", "new"));
                    context.PumpUntil(() => second.FormSet.Task.IsCompleted);
                    Assert.That(second.FormSet.Task.Result.ApiDocumentFilePathOrUrl, Is.EqualTo("new"));
                }
                finally { presenter.Unbind(); }
            }
        }

        [Test]
        public void SavedProviderRefreshKeepsTheNewestResponse()
        {
            using (var context = new QueuedContext())
            {
                var repository = new DeferredRepository();
                var presenter = new MenuPresenter(new GenerationService(repository, new GenerationProviderRegistry()));
                var view = new RecordingGenerationView();
                try
                {
                    presenter.Bind(view);
                    repository.Reads[0].SetResult(new ProjectSetting(GenerateProvider.OpenApi));
                    ProjectSettingChangeNotification.PublishSaved();
                    ProjectSettingChangeNotification.PublishSaved();
                    repository.Reads[2].SetResult(new ProjectSetting(GenerateProvider.SourceGenerator));
                    repository.Reads[1].SetResult(new ProjectSetting(GenerateProvider.OpenApi));
                    context.Drain();
                    Assert.That(view.LastDisplayedProvider, Is.EqualTo(GenerateProvider.SourceGenerator));
                }
                finally { presenter.Unbind(); }
            }
        }

        [Test]
        public void SavedProviderRefreshSurvivesDraftEditingDuringItsRead()
        {
            var repository = new DeferredRepository();
            var presenter = new MenuPresenter(new GenerationService(repository, new GenerationProviderRegistry()));
            var view = new RecordingGenerationView();
            try
            {
                presenter.Bind(view);
                repository.Reads[0].SetResult(new ProjectSetting(GenerateProvider.OpenApi, "saved", "saved-output"));
                ProjectSettingChangeNotification.PublishSaved();
                view.RaiseGenerateSettingChanged(new GenerateApiClientDto(
                    GenerateProvider.OpenApi, "edited", "edited-output"));
                repository.Reads[1].SetResult(new ProjectSetting(GenerateProvider.SourceGenerator));
                testContext!.Drain();
                GenerateApiClientDto dto = CurrentDto(presenter);
                Assert.That(view.LastDisplayedProvider, Is.EqualTo(GenerateProvider.SourceGenerator));
                Assert.That(dto.GenerateProvider, Is.EqualTo(GenerateProvider.SourceGenerator));
                Assert.That(dto.ApiDocumentFilePathOrUrl, Is.EqualTo("edited"));
                Assert.That(dto.ApiClientOutputFolderPath, Is.EqualTo("edited-output"));
                Assert.That(view.SetFormValueCallCount, Is.EqualTo(1));
            }
            finally { presenter.Unbind(); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InitialLoadAndProviderRefreshPreserveBothFormAndNewestProvider(bool refreshFirst)
        {
            var repository = new DeferredRepository();
            var presenter = new MenuPresenter(new GenerationService(repository, new GenerationProviderRegistry()));
            var view = new RecordingGenerationView();
            try
            {
                presenter.Bind(view);
                ProjectSettingChangeNotification.PublishSaved();
                var form = new ProjectSetting(GenerateProvider.OpenApi, "initial", "initial-output");
                var provider = new ProjectSetting(GenerateProvider.SourceGenerator);
                if (refreshFirst)
                {
                    repository.Reads[1].SetResult(provider);
                    repository.Reads[0].SetResult(form);
                }
                else
                {
                    repository.Reads[0].SetResult(form);
                    repository.Reads[1].SetResult(provider);
                }
                testContext!.Drain();
                GenerateApiClientDto dto = CurrentDto(presenter);
                Assert.That(view.LastDisplayedProvider, Is.EqualTo(GenerateProvider.SourceGenerator));
                Assert.That(dto.GenerateProvider, Is.EqualTo(GenerateProvider.SourceGenerator));
                Assert.That(dto.ApiDocumentFilePathOrUrl, Is.EqualTo("initial"));
                Assert.That(dto.ApiClientOutputFolderPath, Is.EqualTo("initial-output"));
                Assert.That(view.SetFormValueCallCount, Is.EqualTo(1));
                Assert.That(view.LastInputEnabled, Is.True);
            }
            finally { presenter.Unbind(); }
        }

        static GenerateApiClientDto CurrentDto(MenuPresenter presenter) =>
            (GenerateApiClientDto)typeof(MenuPresenter).GetField("_generateApiClientDto",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(presenter)!;

        static void ReportProgress(GenerationRequest request, string stage)
        {
            typeof(GenerationRequest).GetMethod("ReportProgress",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(request, new object[] { 0.5, stage });
        }

        static Task StartOperation(MenuPresenter presenter, RecordingGenerationView view)
        {
            view.RaiseGenerateSettingChanged(new GenerateApiClientDto(
                GenerateProvider.SourceGenerator, "draft/openapi.json", "draft/output"));
            // Retain the actual presenter task so assertions wait for its continuation,
            // not merely for the provider's task to complete.
            return (Task)typeof(MenuPresenter).GetMethod("GenerateAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(presenter, null)!;
        }

        sealed class DeferredProvider : IGenerationProvider
        {
            public GenerationProviderDescriptor Descriptor { get; } = new(
                GenerateProvider.SourceGenerator, "Deferred", GenerationProviderAvailability.Available());
            public TaskCompletionSource<GenerationResult> Completion { get; set; } = new();
            public GenerationRequest? LastRequest { get; private set; }
            public GenerationResult Generate(GenerationRequest request) => throw new NotSupportedException();
            public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken = default)
            {
                LastRequest = request;
                return Completion.Task;
            }
        }

        sealed class DeferredRepository : IAsyncRepository<ProjectSetting>
        {
            public System.Collections.Generic.List<TaskCompletionSource<ProjectSetting?>> Reads { get; } = new();
            public Task<ProjectSetting?> ReadAsync()
            {
                var read = new TaskCompletionSource<ProjectSetting?>();
                Reads.Add(read);
                return read.Task;
            }
            public Task SaveAsync(ProjectSetting value) => Task.CompletedTask;
            public Task DeleteAsync() => Task.CompletedTask;
        }

        sealed class QueuedContext : SynchronizationContext, IDisposable
        {
            readonly SynchronizationContext? previous = Current;
            readonly System.Collections.Concurrent.ConcurrentQueue<Action> callbacks = new();
            public QueuedContext() { SetSynchronizationContext(this); }
            public override void Post(SendOrPostCallback callback, object? state) => callbacks.Enqueue(() => callback(state));
            public void Drain() { while (callbacks.TryDequeue(out Action callback)) callback(); }
            public void PumpUntil(Func<bool> completed)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (!completed() && timer.Elapsed < TimeSpan.FromSeconds(5)) { Drain(); Thread.Yield(); }
                Assert.That(completed(), Is.True, "Timed out waiting for presenter continuation.");
                Drain();
            }
            public void Dispose() { SetSynchronizationContext(previous); }
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
            if (SynchronizationContext.Current is QueuedContext context)
                context.PumpUntil(() => task.IsCompleted);
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
            public event Action? CancelRequested;

            public TaskCompletionSource<GenerateApiClientDto> FormSet { get; } = new();
            public TaskCompletionSource<GenerateProvider> ProviderSet { get; } = new();
            public TaskCompletionSource<bool> GenerationSucceeded { get; } = new();
            public TaskCompletionSource<bool> GenerationFinished { get; } = new();

            public int SetFormValueCallCount { get; private set; }
            public GenerateProvider? LastDisplayedProvider { get; private set; }
            public string? LastSuccessMessage { get; private set; }
            public string? LastWarningMessage { get; private set; }
            public string? LastCanceledMessage { get; private set; }
            public bool? LastInputEnabled { get; private set; }
            public bool? LastCancelEnabled { get; private set; }
            public int StatusCount { get; private set; }

            public void RaiseGenerateRequested(GenerateApiClientDto dto)
            {
                GenerateRequested?.Invoke(dto);
            }

            public void RaiseGenerateSettingChanged(GenerateApiClientDto dto)
            {
                GenerateSettingChanged?.Invoke(dto);
            }

            public void RaiseCancelRequested()
            {
                CancelRequested?.Invoke();
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
                StatusCount++;
                if (generateStatus is SucceedProgressStatus)
                {
                    LastSuccessMessage = ((SucceedProgressStatus)generateStatus).Message;
                    GenerationSucceeded.TrySetResult(true);
                }

                if (generateStatus is WarningProgressStatus)
                {
                    LastWarningMessage = ((WarningProgressStatus)generateStatus).Message;
                }

                if (generateStatus is CanceledProgressStatus)
                {
                    LastCanceledMessage = ((CanceledProgressStatus)generateStatus).Message;
                }

                if (!(generateStatus is PendingProgressStatus))
                {
                    GenerationFinished.TrySetResult(true);
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
                LastInputEnabled = isEnabled;
            }

            public void SetCancelEnabled(bool isEnabled)
            {
                LastCancelEnabled = isEnabled;
            }
        }

        sealed class BlockingProvider : IGenerationProvider
        {
            public GenerationProviderDescriptor Descriptor { get; }
            public int GenerateAsyncCallCount { get; private set; }
            public CancellationToken LastCancellationToken { get; private set; }
            public TaskCompletionSource<bool> Started { get; } = new();
            public TaskCompletionSource<GenerationResult> Completion { get; } = new();

            public BlockingProvider(GenerateProvider provider)
            {
                Descriptor = new GenerationProviderDescriptor(
                    provider,
                    $"Blocking {provider}",
                    GenerationProviderAvailability.Available());
            }

            public GenerationResult Generate(GenerationRequest request)
            {
                return GenerateAsync(request).GetAwaiter().GetResult();
            }

            public async Task<GenerationResult> GenerateAsync(
                GenerationRequest request,
                CancellationToken cancellationToken = default)
            {
                GenerateAsyncCallCount++;
                LastCancellationToken = cancellationToken;
                Started.TrySetResult(true);
                using (cancellationToken.Register(() => Completion.TrySetCanceled()))
                {
                    return await Completion.Task.ConfigureAwait(false);
                }
            }
        }

        sealed class RecordingProvider : IGenerationProvider
        {
            readonly GenerationResult _result;

            public GenerationProviderDescriptor Descriptor { get; }
            public int GenerateAsyncCallCount { get; private set; }
            public GenerationRequest? LastRequest { get; private set; }

            public RecordingProvider(
                GenerateProvider provider,
                GenerationResult? result = null)
            {
                Descriptor = new GenerationProviderDescriptor(
                    provider,
                    $"Test {provider}",
                    GenerationProviderAvailability.Available());
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
