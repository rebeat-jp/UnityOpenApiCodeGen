#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Core;


namespace Rhycol.OpenApiCodeGen.Lib
{
    internal class UserSettingJsonRepository : IAsyncRepository<UserSetting>
    {
        readonly JsonFileStore<UserSettingJson> _jsonFileStore;
        const string FILE_NAME = "userSettings.json";

        public UserSettingJsonRepository()
        {

            var directory = Path.GetDirectoryName(ApplicationConstant.USER_FOLDER_PATH);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _jsonFileStore = new(
                Path.Combine(ApplicationConstant.USER_FOLDER_PATH, FILE_NAME)
            );
        }

        public async Task<UserSetting?> ReadAsync()
        {
            var json = await _jsonFileStore.ReadAsync();
            return json?.ToDomain();
        }

        public async Task SaveAsync(UserSetting value)
        {
            await _jsonFileStore.SaveAsync(UserSettingJson.FromDomain(value));
        }

        public async Task DeleteAsync()
        {
            await Task.Run(() => _jsonFileStore.Delete());
        }

        [Serializable]
        private class UserSettingJson
        {
            public string DockerPath = "";

            public static UserSettingJson FromDomain(UserSetting value)
            {
                return new UserSettingJson
                {
                    DockerPath = value.DockerPath,
                };
            }

            public UserSetting ToDomain()
            {
                return new UserSetting(
                    dockerPath: DockerPath
                );
            }
        }

    }
}
