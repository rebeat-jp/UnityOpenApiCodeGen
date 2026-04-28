using System;
using System.IO;

namespace ReBeat.OpenApiCodeGen
{
    internal static class ApplicationConstant
    {
        public static readonly string CacheFolderPath = Path.Combine(
            Path.GetTempPath(),
            "ReBeat",
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
            "ReBeat",
            "OpenApiCodeGen"
        );
#elif UNITY_EDITOR_OSX
        public static readonly string USER_FOLDER_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "ReBeat",
            "OpenApiCodeGen"
        );
#elif UNITY_EDITOR_LINUX
        public static readonly string USER_FOLDER_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config",
                "ReBeat",
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