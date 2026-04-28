#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.UI;


namespace ReBeat.OpenApiCodeGen.Presenter
{
    internal class MenuPresenter : IGenerationPresenter
    {
        IGenerationView? _generationView;
        readonly GenerationService _generateService;
        GenerateApiClientDto _generateApiClientDto = new();

        public MenuPresenter()
        {
            _generateService = new GenerationService();
        }

        public void Bind(IGenerationView generationView)
        {
            Unbind();

            _generationView = generationView;

            _generationView.GenerateRequested += OnGenerateRequested;
            _generationView.GenerateSettingChanged += OnGenerateSettingChanged;

            _ = LoadConfigAsync();
        }

        public void Unbind()
        {
            if (_generationView == null)
            {
                return;
            }

            _generationView.GenerateRequested -= OnGenerateRequested;
            _generationView.GenerateSettingChanged -= OnGenerateSettingChanged;
            _generationView = null;
        }

        async Task LoadConfigAsync()
        {
            _generationView?.SetInputEnabled(false);

            try
            {
                _generateApiClientDto = await _generateService.GetDefaultGenerateApiClientDtoAsync();
                _generationView?.SetFormValue(_generateApiClientDto);
            }
            catch (ApplicationServiceException e)
            {
                SetProgressStatus(new FailedProgressStatus(BuildFailureLog(e)));
            }
            finally
            {
                _generationView?.SetInputEnabled(true);
            }
        }

        void SetProgressStatus(IProgressStatus status)
        {
            _generationView?.SetGenerateStatus(status);
        }

        void OnGenerateSettingChanged(GenerateApiClientDto generateApiClientDto)
        {
            _generateApiClientDto = generateApiClientDto;
        }

        void OnGenerateRequested(GenerateApiClientDto generateApiClientDto)
        {
            _generateApiClientDto = generateApiClientDto;
            _ = GenerateAsync();
        }

        async Task GenerateAsync()
        {
            _generationView?.SetDocumentFilePathComment("");
            _generationView?.SetOutputPathComment("");

            var canGenerate = true;

            if (string.IsNullOrEmpty(_generateApiClientDto.ApiDocumentFilePathOrUrl))
            {
                canGenerate = false;
                _generationView?.SetDocumentFilePathComment("Api document file path or url is Empty");
            }

            if (string.IsNullOrEmpty(_generateApiClientDto.ApiClientOutputFolderPath))
            {
                canGenerate = false;
                _generationView?.SetOutputPathComment("Api Client file output Folder is Empty");
            }

            if (!canGenerate)
            {
                return;
            }

            _generationView?.SetInputEnabled(false);
            SetProgressStatus(new PendingProgressStatus(20));

            try
            {
                await _generateService.GenerateApiClientAsync(
                    _generateApiClientDto);

                SetProgressStatus(new SucceedProgressStatus());
            }
            catch (ApplicationServiceException e)
            {
                SetProgressStatus(new FailedProgressStatus(BuildFailureLog(e)));
            }
            finally
            {
                _generationView?.SetInputEnabled(true);
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
