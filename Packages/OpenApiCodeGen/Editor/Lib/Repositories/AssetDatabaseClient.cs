#nullable enable

using System;
using System.Linq;

using UnityEditor;

using UnityEngine;

namespace ReBeat.OpenApiCodeGen.Lib
{
    class AssetDatabaseClient<T> where T : ScriptableObject
    {
        public T FindOneFirst()
        {
            var filePath = FindOneFirstPath();
            return AssetDatabase.LoadAssetAtPath<T>(filePath);
        }

        public string FindOneFirstPath()
        {
            var guid = AssetDatabase.FindAssets("t:" + typeof(T).Name).FirstOrDefault();
            if (string.IsNullOrEmpty(guid))
            {
                throw new InvalidOperationException();
            }
            var filePath = AssetDatabase.GUIDToAssetPath(guid);
            return filePath;
        }

        public T Save(T item, string path, bool isOverride = false)
        {
            if (!isOverride)
            {
                AssetDatabase.CreateAsset(item, path);
                return item;
            }

            var guid = AssetDatabase.FindAssets("t:" + typeof(T).Name).First();
            if (string.IsNullOrEmpty(guid))
            {
                AssetDatabase.CreateAsset(item, path);
                return item;
            }
            var filePath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(guid))
            {
                AssetDatabase.CreateAsset(item, path);
                return item;
            }

            var isDeletedOld = AssetDatabase.DeleteAsset(filePath);
            if (!isDeletedOld)
            {
                throw new InvalidOperationException("Failed old item");
            }
            AssetDatabase.CreateAsset(item, path);
            return item;

        }
    }
}