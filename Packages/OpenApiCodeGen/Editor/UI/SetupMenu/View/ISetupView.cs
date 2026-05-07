#nullable enable

using System;

using Rhycol.OpenApiCodeGen.Core;

namespace Rhycol.OpenApiCodeGen.UI
{
    internal interface ISetupView
    {
        event Action<SetupDto>? SetupRequested;
        event Action<CheckDockerInstalledDto>? DockerCheckRequested;

        void SetProgressStatus(IProgressStatus setupStatus);
        void SetDockerCheckResult(bool isRunnable);
        void SetInputEnabled(bool isEnabled);
    }

}
