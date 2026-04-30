#nullable enable

using System;

using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.UI
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
