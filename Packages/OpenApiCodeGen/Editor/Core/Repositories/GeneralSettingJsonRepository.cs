#nullable enable

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
            _jsonFileStore = new(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Assets",
                    "OpenApiCodeGen",
                    "general.json")
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