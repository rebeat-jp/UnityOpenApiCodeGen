#nullable enable
using System;
using System.IO;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace ReBeat.OpenApiCodeGen.Lib
{
    class JsonRepository<T> where T : class
    {
        public T? Read(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }
            var jsonContent = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<T>(jsonContent)
            ?? throw new ArgumentException($"Cannot convert to {typeof(T).Name}");
        }

        async public Task<T?> ReadAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var jsonContent = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<T>(jsonContent)
                  ?? throw new ArgumentException($"Cannot convert to {typeof(T).Name}");

        }

        public T Save(string filePath, T value)
        {
            var jsonContent = JsonConvert.SerializeObject(value);

            if (File.Exists(filePath))
            {
                File.WriteAllText(filePath, jsonContent);
                return value;
            }
            using var jsonFile = File.CreateText(filePath);
            jsonFile.Write(jsonContent);
            jsonFile.Flush();
            return value;
        }
        async public Task<T> SaveAsync(string filePath, T value)
        {
            var jsonContent = JsonConvert.SerializeObject(value);

            if (File.Exists(filePath))
            {
                await File.WriteAllTextAsync(filePath, jsonContent);
                return value;
            }
            using var jsonFile = File.CreateText(filePath);
            await jsonFile.WriteAsync(jsonContent);
            await jsonFile.FlushAsync();
            return value;
        }
        public string? Delete(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }
            File.Delete(filePath);

            return filePath;
        }
    }
}