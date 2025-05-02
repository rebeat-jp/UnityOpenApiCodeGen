#nullable enable

using System;
using System.Linq;
using System.Threading;

using Cysharp.Threading.Tasks;


using UnityEditor;

using UnityEngine;

namespace ReBeat.OpenApiCodeGen.Lib
{
    public class ScriptableObjectRepository<T> where T : class, IScriptable<T>, new()
    {
        public T? Read()
        {
            var instance = new T();
            var targetScriptableObjectType = instance.GetScriptableType();
            var assetGuid = AssetDatabase.FindAssets("t:" + targetScriptableObjectType.Name).FirstOrDefault();
            var assetFilePath = AssetDatabase.GUIDToAssetPath(assetGuid);
            if (string.IsNullOrEmpty(assetFilePath))
            {
                return null;
            }

            var scriptableObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetFilePath);
            var test = instance.FromScriptable(scriptableObject);

            return test;
        }

        public T Save(T value, string savePath = "Assets/OpenApiCodeGen")
        {
            var oldScriptableObjectGuid = AssetDatabase.FindAssets($"t:{typeof(T).Name}").FirstOrDefault();
            var scriptableObject = value.ToScriptable();

            if (oldScriptableObjectGuid is not null)
            {
                var oldScriptableObjectPath = AssetDatabase.GUIDToAssetPath(oldScriptableObjectGuid);
                AssetDatabase.DeleteAsset(oldScriptableObjectPath);
            }

            AssetDatabase.CreateAsset(scriptableObject, savePath);
            return value;
        }

        async public UniTask<T?> ReadAsync(CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(Read, cancellationToken: ct);
        }


    }

}
