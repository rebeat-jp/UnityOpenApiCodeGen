#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;

using ReBeat.OpenApiCodeGen.Lib;

namespace ReBeat.OpenApiCodeGen.Core
{
    public class GeneralSettingJsonRepository : IRepository<GeneralConfigSchema>
    {
        readonly JsonFileStore<GeneralConfigSchema> _jsonFileStore;

        public GeneralSettingJsonRepository()
        {
#if UNITY_EDITOR_WIN
            var savedPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData",
                "LocalLow",
                "ReBeat",
                "OpenApiCodeGen",
                "general.json"
            );
#elif UNITY_EDITOR_OSX
            var savedPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "ReBeat",
                "OpenApiCodeGen",
                "general.json"
            );
#elif UNITY_EDITOR_LINUX
            var savedPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config",
                "ReBeat",
                "OpenApiCodeGen",
                "general.json"
            );
#else
            var savedPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Assets",
                "OpenApiCodeGen",
                "general.json"
            );
#endif

            var directory = Path.GetDirectoryName(savedPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _jsonFileStore = new(
                savedPath
            );
        }

        public GeneralConfigSchema? Read()
        {
            return _jsonFileStore.Read();
        }

        public void Save(GeneralConfigSchema value)
        {
            _jsonFileStore.Save(value);
        }

        public void Delete()
        {
            _jsonFileStore.Delete();
        }

        public async Task<GeneralConfigSchema?> ReadAsync()
        {
            return await _jsonFileStore.ReadAsync();
        }

        public async Task SaveAsync(GeneralConfigSchema value)
        {
            await _jsonFileStore.SaveAsync(value);
        }
    }
}