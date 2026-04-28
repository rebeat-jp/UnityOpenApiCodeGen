#nullable enable

using System.IO;
using System.Threading.Tasks;

using ReBeat.OpenApiCodeGen.Core;


namespace ReBeat.OpenApiCodeGen.Lib
{
    internal class OpenApiCsharpSettingJsonRepository : IAsyncRepository<OpenApiCsharpOption>
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

        public async Task DeleteAsync()
        {
            await Task.Run(() => _jsonFileStore.Delete());
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