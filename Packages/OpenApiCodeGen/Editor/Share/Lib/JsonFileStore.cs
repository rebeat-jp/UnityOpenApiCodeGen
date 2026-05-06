#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;


using ReBeat.OpenApiCodeGen.Core;

using UnityEngine;

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
            try
            {
                if (!File.Exists(_savePath))
                {
                    return null;
                }
                var jsonContent = File.ReadAllText(_savePath);
                return JsonUtility.FromJson<T>(jsonContent);
            }
            catch (Exception e)
            {
                throw new ExternalStorageException($"JSONファイルの読み込みに失敗しました。path: {_savePath}", e);
            }
        }

        async public Task<T?> ReadAsync()
        {
            try
            {
                if (!File.Exists(_savePath))
                {
                    return null;
                }

                var jsonContent = await File.ReadAllTextAsync(_savePath);
                return JsonUtility.FromJson<T>(jsonContent);
            }
            catch (Exception e)
            {
                throw new ExternalStorageException($"JSONファイルの読み込みに失敗しました。path: {_savePath}", e);
            }
        }

        public T Save(T value)
        {
            try
            {
                var jsonContent = JsonUtility.ToJson(value);

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
            catch (Exception e)
            {
                throw new ExternalStorageException($"JSONファイルの保存に失敗しました。path: {_savePath}", e);
            }
        }
        async public Task<T> SaveAsync(T value)
        {
            try
            {
                var jsonContent = JsonUtility.ToJson(value);

                var directory = Path.GetDirectoryName(_savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

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
            catch (Exception e)
            {
                throw new ExternalStorageException($"JSONファイルの保存に失敗しました。path: {_savePath}", e);
            }
        }
        public string? Delete()
        {
            try
            {
                if (!File.Exists(_savePath))
                {
                    return null;
                }
                File.Delete(_savePath);

                return _savePath;
            }
            catch (Exception e)
            {
                throw new ExternalStorageException($"JSONファイルの削除に失敗しました。path: {_savePath}", e);
            }
        }

    }
}
