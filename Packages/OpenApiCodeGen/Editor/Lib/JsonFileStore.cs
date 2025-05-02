#nullable enable

using System.IO;
using System.Threading.Tasks;

using Newtonsoft.Json;

namespace ReBeat.OpenApiCodeGen.Lib
{
    public class JsonFileStore<T> where T : class
    {
        readonly string _savePath;
        public JsonFileStore(string savePath)
        {
            _savePath = savePath;
        }

        public T? Read()
        {
            if (!File.Exists(_savePath))
            {
                return null;
            }
            var jsonContent = File.ReadAllText(_savePath);
            return JsonConvert.DeserializeObject<T>(jsonContent);
        }

        async public Task<T?> ReadAsync()
        {
            if (!File.Exists(_savePath))
            {
                return null;
            }

            var jsonContent = await File.ReadAllTextAsync(_savePath);
            return JsonConvert.DeserializeObject<T>(jsonContent);
        }

        public T Save(T value)
        {
            var jsonContent = JsonConvert.SerializeObject(value);

            if (File.Exists(_savePath))
            {
                File.WriteAllText(_savePath, jsonContent);
                return value;
            }
            if (!Directory.Exists(Path.GetDirectoryName(_savePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_savePath));
            }
            using var jsonFile = File.CreateText(_savePath);
            jsonFile.Write(jsonContent);
            jsonFile.Flush();
            return value;
        }
        async public Task<T> SaveAsync(T value)
        {
            var jsonContent = JsonConvert.SerializeObject(value);

            if (File.Exists(_savePath))
            {
                await File.WriteAllTextAsync(_savePath, jsonContent);
                return value;
            }
            using var jsonFile = File.CreateText(_savePath);
            await jsonFile.WriteAsync(jsonContent);
            await jsonFile.FlushAsync();
            return value;
        }
        public string? Delete()
        {
            if (!File.Exists(_savePath))
            {
                return null;
            }
            File.Delete(_savePath);

            return _savePath;
        }

    }
}