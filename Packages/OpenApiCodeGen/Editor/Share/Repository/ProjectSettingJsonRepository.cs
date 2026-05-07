#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Core;


namespace Rhycol.OpenApiCodeGen.Lib
{
    internal class ProjectSettingJsonRepository : IAsyncRepository<ProjectSetting>
    {
        readonly JsonFileStore<ProjectSettingJson> _jsonFileStore;
        const string FILE_NAME = "projectSettings.json";
        public ProjectSettingJsonRepository()
        {

            var directory = Path.GetDirectoryName(ApplicationConstant.PROJECT_FOLDER_PATH);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _jsonFileStore = new(
                Path.Combine(ApplicationConstant.PROJECT_FOLDER_PATH, FILE_NAME)
            );
        }

        public async Task<ProjectSetting?> ReadAsync()
        {
            var json = await _jsonFileStore.ReadAsync();
            return json?.ToDomain();
        }

        public async Task SaveAsync(ProjectSetting value)
        {
            await _jsonFileStore.SaveAsync(ProjectSettingJson.FromDomain(value));
        }

        public async Task DeleteAsync()
        {
            await Task.Run(() => _jsonFileStore.Delete());
        }

        [Serializable]
        private class ProjectSettingJson
        {
            public GenerateProvider GenerateProvider = GenerateProvider.OpenApi;
            public string ApiDocumentFilePathOrUrl = "";
            public string ApiClientOutputFolderPath = "";

            public static ProjectSettingJson FromDomain(ProjectSetting value)
            {
                return new ProjectSettingJson
                {
                    GenerateProvider = value.GenerateProvider,
                    ApiDocumentFilePathOrUrl = value.ApiDocumentFilePathOrUrl,
                    ApiClientOutputFolderPath = value.ApiClientOutputFolderPath,
                };
            }

            public ProjectSetting ToDomain()
            {
                return new ProjectSetting(
                    generateProvider: GenerateProvider,
                    apiDocumentFilePathOrUrl: ApiDocumentFilePathOrUrl,
                    apiClientOutputFolderPath: ApiClientOutputFolderPath
                );
            }
        }

    }
}
