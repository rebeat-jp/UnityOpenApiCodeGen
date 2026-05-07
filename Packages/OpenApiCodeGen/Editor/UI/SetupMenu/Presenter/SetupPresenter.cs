#nullable enable

using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.UI;

namespace Rhycol.OpenApiCodeGen.Presenter
{
    internal class SetupPresenter : ISetupPresenter
    {
        ISetupView? _setupView;
        readonly SetupService _setupService;

        public SetupPresenter()
        {
            _setupService = new();
        }

        public void Bind(ISetupView setupView)
        {
            Unbind();

            _setupView = setupView;

            _setupView.SetupRequested += OnSetupRequested;
            _setupView.DockerCheckRequested += OnDockerCheckRequested;
        }

        public void Unbind()
        {
            if (_setupView == null)
            {
                return;
            }

            _setupView.SetupRequested -= OnSetupRequested;
            _setupView.DockerCheckRequested -= OnDockerCheckRequested;
            _setupView = null;
        }

        void SetProgressStatus(IProgressStatus setupStatus)
        {
            _setupView?.SetProgressStatus(setupStatus);
        }

        void OnDockerCheckRequested(CheckDockerInstalledDto checkDockerInstalledDto)
        {
            _ = CheckRunnableDockerPathAsync(checkDockerInstalledDto);
        }

        async Task CheckRunnableDockerPathAsync(CheckDockerInstalledDto checkDockerInstalledDto)
        {
            _setupView?.SetInputEnabled(false);

            try
            {
                var isRunnable = await _setupService.CheckDockerInstalledAsync(checkDockerInstalledDto);
                _setupView?.SetDockerCheckResult(isRunnable);
            }
            finally
            {
                _setupView?.SetInputEnabled(true);
            }
        }

        void OnSetupRequested(SetupDto setupDto)
        {
            _ = SetupAsync(setupDto);
        }

        async Task SetupAsync(SetupDto setupDto)
        {
            _setupView?.SetInputEnabled(false);
            SetProgressStatus(new PendingProgressStatus(0.25));

            try
            {
                await _setupService.SetupAsync(setupDto);
                SetProgressStatus(new SucceedProgressStatus());
            }
            catch
            {
                SetProgressStatus(new FailedProgressStatus());
            }
            finally
            {
                _setupView?.SetInputEnabled(true);
            }
        }
    }
}
