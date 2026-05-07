using System;
using System.IO;

namespace Rhycol.OpenApiCodeGen
{
    internal static class ApplicationConstant
    {
        public static readonly string CacheFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Rhycol",
            "OpenApiCodeGen"
        );
        public static readonly string PROJECT_FOLDER_PATH = Path.Combine(
            Directory.GetCurrentDirectory(),
            "Assets",
            "OpenApiCodeGen"
        );

#if UNITY_EDITOR_WIN
        public static readonly string USER_FOLDER_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData",
            "LocalLow",
            "Rhycol",
            "OpenApiCodeGen"
        );
#elif UNITY_EDITOR_OSX
        public static readonly string USER_FOLDER_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "Rhycol",
            "OpenApiCodeGen"
        );
#elif UNITY_EDITOR_LINUX
        public static readonly string USER_FOLDER_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config",
                "Rhycol",
                "OpenApiCodeGen"
            );
#else
        public static readonly string USER_FOLDER_PATH = Path.Combine(
            Directory.GetCurrentDirectory(),
            "Assets",
            "OpenApiCodeGen"
            );
#endif
    }
}