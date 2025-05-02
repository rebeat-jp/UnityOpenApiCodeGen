#nullable enable

using System.IO;
using System.Threading.Tasks;

using ReBeat.OpenApiCodeGen.Lib;

namespace ReBeat.OpenApiCodeGen.Core
{
    internal class OpenApiCsharpSettingJsonRepository : IRepository<OpenApiCsharpOption>
    {
        readonly JsonFileStore<OpenApiCsharpOption> _jsonFileStore;

        public OpenApiCsharpSettingJsonRepository()
        {
            _jsonFileStore = new(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Assets",
                    "OpenApiCodeGen",
                    "openapi.json")
            );
        }

        public OpenApiCsharpOption? Read()
        {
            return _jsonFileStore.Read();
        }

        public void Save(OpenApiCsharpOption value)
        {
            _jsonFileStore.Save(value);
        }

        public void Delete()
        {
            _jsonFileStore.Delete();
        }

        public async Task<OpenApiCsharpOption?> ReadAsync()
        {
            return await _jsonFileStore.ReadAsync();
        }

        public async Task SaveAsync(OpenApiCsharpOption value)
        {
            await _jsonFileStore.SaveAsync(value);
        }
    }
}