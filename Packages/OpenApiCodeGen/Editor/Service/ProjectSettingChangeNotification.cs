#nullable enable

using System;

namespace Rhycol.OpenApiCodeGen.Core
{
    internal static class ProjectSettingChangeNotification
    {
        public static event Action? Saved;

        public static void PublishSaved()
        {
            Saved?.Invoke();
        }
    }
}
