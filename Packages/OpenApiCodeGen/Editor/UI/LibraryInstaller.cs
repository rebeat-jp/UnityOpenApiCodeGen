#nullable enable

using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Rhycol.OpenApiCodeGen.UI
{
    /// <summary>
    /// Adds the Unity packages that the Source Generator libraries section offers. Only packages from the
    /// Unity registry or built-in modules are added; System.Text.Json is shown as manual steps.
    /// </summary>
    internal static class LibraryInstaller
    {
        internal static string? GetPackageSpecifier(LibraryInstallAction action)
        {
            switch (action)
            {
                case LibraryInstallAction.AddNewtonsoftJsonPackage:
                    return SourceGeneratorLibraryPresentation.NewtonsoftJsonPackageSpecifier;
                case LibraryInstallAction.EnableUnityWebRequestModule:
                    return SourceGeneratorLibraryPresentation.UnityWebRequestModuleSpecifier;
                default:
                    return null;
            }
        }

        /// <summary>Starts Package Manager and logs the result. Returns false when the action adds no package.</summary>
        internal static bool Run(LibraryInstallAction action)
        {
            string? specifier = GetPackageSpecifier(action);
            if (specifier == null)
            {
                return false;
            }

            AddRequest request = Client.Add(specifier);
            void Poll()
            {
                if (!request.IsCompleted)
                {
                    return;
                }

                EditorApplication.update -= Poll;
                if (request.Status == StatusCode.Success)
                {
                    Debug.Log($"Added {request.Result.packageId}. Unity will recompile the scripts.");
                }
                else
                {
                    Debug.LogError($"Adding '{specifier}' failed: {request.Error?.message}");
                }
            }

            EditorApplication.update += Poll;
            return true;
        }
    }
}
