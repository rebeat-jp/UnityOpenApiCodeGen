#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.UI;


namespace Rhycol.OpenApiCodeGen.Presenter
{
    internal class MenuPresenter : IGenerationPresenter
    {
        IGenerationView? _generationView;
        readonly GenerationService _generateService;
        GenerateApiClientDto _generateApiClientDto = new();
        CancellationTokenSource? _generationCancellation;
        int _viewGeneration;
        int _settingsRevision;
        int _providerRevision;

        public MenuPresenter()
        {
            _generateService = new GenerationService();
        }

        internal MenuPresenter(GenerationService generateService)
        {
            _generateService = generateService ?? throw new ArgumentNullException(nameof(generateService));
        }

        public void Bind(IGenerationView generationView)
        {
            Unbind();

            _generationView = generationView;
            _viewGeneration++;

            _generationView.GenerateRequested += OnGenerateRequested;
            _generationView.GenerateSettingChanged += OnGenerateSettingChanged;
            _generationView.CancelRequested += OnCancelRequested;
            ProjectSettingChangeNotification.Saved += OnProjectSettingSaved;

            _ = LoadConfigAsync();
        }

        public void Unbind()
        {
            ProjectSettingChangeNotification.Saved -= OnProjectSettingSaved;
            CancellationTokenSource? cancellation = _generationCancellation;
            _generationCancellation = null;
            _viewGeneration++;
            _settingsRevision++;
            _providerRevision++;

            if (_generationView == null)
            {
                cancellation?.Cancel();
                return;
            }

            _generationView.GenerateRequested -= OnGenerateRequested;
            _generationView.GenerateSettingChanged -= OnGenerateSettingChanged;
            _generationView.CancelRequested -= OnCancelRequested;
            _generationView = null;
            // Invalidate the view before synchronous cancellation callbacks run.
            // The operation owns disposal until its token consumers have returned.
            cancellation?.Cancel();
        }

        async Task LoadConfigAsync()
        {
            IGenerationView? view = _generationView;
            int generation = _viewGeneration;
            int revision = ++_settingsRevision;
            int providerRevision = _providerRevision;
            view?.SetInputEnabled(false);

            try
            {
                GenerateApiClientDto settings = await _generateService.GetDefaultGenerateApiClientDtoAsync();
                if (IsCurrentView(view, generation) && revision == _settingsRevision)
                {
                    // A newer provider refresh owns the selection, but must not discard
                    // the initial form values while the user has not edited them.
                    if (providerRevision != _providerRevision)
                        settings = new GenerateApiClientDto(_generateApiClientDto.GenerateProvider,
                            settings.ApiDocumentFilePathOrUrl, settings.ApiClientOutputFolderPath);
                    _generateApiClientDto = settings;
                    view?.SetFormValue(settings);
                }
            }
            catch (ApplicationServiceException e)
            {
                if (IsCurrentView(view, generation) && revision == _settingsRevision)
                    view?.SetGenerateStatus(new FailedProgressStatus(BuildFailureLog(e)));
            }
            finally
            {
                if (IsCurrentView(view, generation) && _generationCancellation == null)
                    view?.SetInputEnabled(true);
            }
        }

        bool IsCurrentView(IGenerationView? view, int generation)
        {
            return generation == _viewGeneration && ReferenceEquals(view, _generationView);
        }

        void OnGenerateSettingChanged(GenerateApiClientDto generateApiClientDto)
        {
            _settingsRevision++;
            _generateApiClientDto = generateApiClientDto;
        }

        void OnGenerateRequested(GenerateApiClientDto generateApiClientDto)
        {
            if (_generationCancellation != null) return;
            _generateApiClientDto = generateApiClientDto;
            _ = GenerateAsync();
        }

        void OnProjectSettingSaved()
        {
            _ = RefreshGenerateProviderForViewAsync();
        }

        void OnCancelRequested()
        {
            _generationCancellation?.Cancel();
        }

        async Task RefreshGenerateProviderForViewAsync()
        {
            IGenerationView? view = _generationView;
            int generation = _viewGeneration;
            int revision = ++_providerRevision;
            try
            {
                GenerateApiClientDto savedSetting = await _generateService.GetDefaultGenerateApiClientDtoAsync();
                if (IsCurrentView(view, generation) && revision == _providerRevision)
                {
                    _generateApiClientDto = new GenerateApiClientDto(savedSetting.GenerateProvider,
                        _generateApiClientDto.ApiDocumentFilePathOrUrl, _generateApiClientDto.ApiClientOutputFolderPath);
                    view?.SetGenerateProvider(savedSetting.GenerateProvider);
                }
            }
            catch (ApplicationServiceException e)
            {
                if (IsCurrentView(view, generation) && revision == _providerRevision && _generationCancellation == null)
                    view?.SetGenerateStatus(new FailedProgressStatus(BuildFailureLog(e)));
            }
        }

        async Task GenerateAsync()
        {
            if (_generationCancellation != null)
            {
                return;
            }

            _generationCancellation = new CancellationTokenSource();
            CancellationTokenSource operationCancellation = _generationCancellation;
            int operationViewGeneration = _viewGeneration;
            IGenerationView? operationView = _generationView;
            CancellationToken cancellationToken = operationCancellation.Token;
            GenerateApiClientDto input = _generateApiClientDto;
            _settingsRevision++;
            int providerRevision = ++_providerRevision;
            bool completed = false;
            bool IsCurrentOperation() => IsCurrentView(operationView, operationViewGeneration) &&
                ReferenceEquals(_generationCancellation, operationCancellation);
            void SetOperationStatus(IProgressStatus status)
            {
                if (IsCurrentOperation()) operationView?.SetGenerateStatus(status);
            }
            operationView?.SetInputEnabled(false);
            operationView?.SetCancelEnabled(true);
            try
            {
                GenerateApiClientDto savedSetting = await _generateService.GetDefaultGenerateApiClientDtoAsync();
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentOperation()) return;
                var requestDto = new GenerateApiClientDto(savedSetting.GenerateProvider,
                    input.ApiDocumentFilePathOrUrl, input.ApiClientOutputFolderPath);
                if (providerRevision == _providerRevision)
                {
                    _generateApiClientDto = new GenerateApiClientDto(savedSetting.GenerateProvider,
                        _generateApiClientDto.ApiDocumentFilePathOrUrl, _generateApiClientDto.ApiClientOutputFolderPath);
                    operationView?.SetGenerateProvider(savedSetting.GenerateProvider);
                }

                operationView?.SetDocumentFilePathComment("");
                operationView?.SetOutputPathComment("");

                var canGenerate = true;

                if (string.IsNullOrEmpty(requestDto.ApiDocumentFilePathOrUrl))
                {
                    canGenerate = false;
                    operationView?.SetDocumentFilePathComment("Api document file path or url is Empty");
                }

                if (string.IsNullOrEmpty(requestDto.ApiClientOutputFolderPath))
                {
                    canGenerate = false;
                    operationView?.SetOutputPathComment("Api Client file output Folder is Empty");
                }

                if (!canGenerate)
                {
                    return;
                }

                SetOperationStatus(new PendingProgressStatus(0.2));
                var progress = new Progress<GenerationProgress>(reported =>
                {
                    if (!completed && IsCurrentOperation())
                    {
                        operationView?.SetGenerateStatus(new PendingProgressStatus(reported.Value, reported.Stage));
                    }
                });
                GenerationResult result = await _generateService.GenerateApiClientAsync(
                    requestDto, cancellationToken, progress);

                completed = true;
                SetOperationStatus(result.Warnings.Count == 0
                    ? new SucceedProgressStatus(result.Message)
                    : new WarningProgressStatus(string.Join(Environment.NewLine, result.Warnings)));
            }
            catch (OperationCanceledException)
            {
                completed = true;
                SetOperationStatus(new CanceledProgressStatus());
            }
            catch (ApplicationServiceException e)
            {
                completed = true;
                SetOperationStatus(new FailedProgressStatus(BuildFailureLog(e)));
            }
            finally
            {
                completed = true;
                bool restoreView = IsCurrentOperation();
                if (ReferenceEquals(_generationCancellation, operationCancellation))
                {
                    _generationCancellation = null;
                }
                operationCancellation.Dispose();
                if (restoreView)
                {
                    operationView?.SetCancelEnabled(false);
                    operationView?.SetInputEnabled(true);
                }
            }
        }

        static string BuildFailureLog(Exception exception)
        {
            var builder = new StringBuilder();
            Exception? currentException = exception;

            while (currentException != null)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine(currentException.Message);
                currentException = currentException.InnerException;
            }

            return builder.ToString().TrimEnd();
        }
    }
}
