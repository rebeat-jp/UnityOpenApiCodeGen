#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    /// <summary>
    /// Before 0.6.0 this package installed Newtonsoft.Json itself. Existing definitions still use it, so a
    /// project that updates and does not have it would only see OACG011 compile errors. This explains the cause
    /// once and offers to install the package. It never edits the manifest without the user's confirmation.
    /// </summary>
    [InitializeOnLoad]
    internal static class SourceGeneratorNewtonsoftNotice
    {
        internal const string NoticePreferencePrefix = "Rhycol.OpenApiCodeGen.SourceGenerator.NewtonsoftNotice.";

        static SourceGeneratorNewtonsoftNotice()
        {
            // Wait until Unity has finished loading assemblies and the asset database.
            EditorApplication.delayCall += CheckOnce;
        }

        /// <summary>
        /// Whether to show the notice: an existing definition uses Newtonsoft.Json, the project does not have
        /// it, and the notice was not shown already.
        /// </summary>
        internal static bool ShouldNotify(
            IEnumerable<string> definitionSources,
            bool newtonsoftJsonPresent,
            bool alreadyNotified)
        {
            if (newtonsoftJsonPresent || alreadyNotified)
            {
                return false;
            }

            foreach (string source in definitionSources)
            {
                if (UsesNewtonsoftJson(source))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Definitions written by this package declare the serializer only when it is not the default, so a
        /// definition without the SystemTextJson argument uses Newtonsoft.Json.
        /// </summary>
        internal static bool UsesNewtonsoftJson(string definitionSource)
        {
            return definitionSource.StartsWith(
                       OpenApiClientDefinitionWriter.OwnedFileHeader,
                       StringComparison.Ordinal) &&
                   definitionSource.IndexOf(
                       "OpenApiJsonSerializer.SystemTextJson",
                       StringComparison.Ordinal) < 0;
        }

        internal static string BuildMessage()
        {
            return "This project has Source Generator client definitions that use Newtonsoft.Json, "
                + "but Newtonsoft.Json is not installed. Since 0.6.0 the Source Generator package no longer "
                + "installs it automatically, so these clients fail to compile with OACG011. "
                + "Install the " + SourceGeneratorLibraryProbe.NewtonsoftJsonPackageId + " package "
                + SourceGeneratorLibraryProbe.NewtonsoftJsonPackageVersion
                + " (Window > Package Manager, or Project Settings > OpenAPI Code Generator), "
                + "or regenerate the clients with System.Text.Json.";
        }

        static void CheckOnce()
        {
            try
            {
                string preferenceKey = NoticePreferencePrefix + HashProject();
                var probe = SourceGeneratorLibraryProbe.CreateForCurrentProject();
                bool present = probe.IsLibraryPresentInProject(
                    SourceGeneratorLibraryProbe.NewtonsoftJsonAssemblyFileName);
                if (present)
                {
                    // Allow the notice again if the package is removed later.
                    EditorPrefs.DeleteKey(preferenceKey);
                    return;
                }

                bool alreadyNotified = EditorPrefs.GetBool(preferenceKey, false);
                if (!ShouldNotify(ReadDefinitionSources(), present, alreadyNotified))
                {
                    return;
                }

                string message = BuildMessage();
                Debug.LogWarning(message);
                if (Application.isBatchMode)
                {
                    // Batch runs cannot answer a dialog; the next interactive session shows it.
                    return;
                }

                EditorPrefs.SetBool(preferenceKey, true);
                if (EditorUtility.DisplayDialog(
                        "OpenApiCodeGen Source Generator",
                        message,
                        "Install Newtonsoft.Json",
                        "Not now"))
                {
                    InstallNewtonsoftJson();
                }
            }
            catch (Exception exception)
            {
                // A failed hint must never disturb Editor start-up.
                Debug.LogWarning("The Source Generator Newtonsoft.Json check failed: " + exception.Message);
            }
        }

        static IEnumerable<string> ReadDefinitionSources()
        {
            var paths = new List<string>(Directory.GetFiles(
                Application.dataPath,
                "*" + OpenApiClientDefinitionWriter.DefinitionFileSuffix,
                SearchOption.AllDirectories));
            // Embedded packages live under Packages/ and can hold definitions too.
            string packagesPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages");
            if (Directory.Exists(packagesPath))
            {
                paths.AddRange(Directory.GetFiles(
                    packagesPath,
                    "*" + OpenApiClientDefinitionWriter.DefinitionFileSuffix,
                    SearchOption.AllDirectories));
            }

            foreach (string path in paths)
            {
                string text;
                try
                {
                    text = File.ReadAllText(path, Encoding.UTF8);
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException)
                {
                    continue;
                }

                yield return text;
            }
        }

        static void InstallNewtonsoftJson()
        {
            AddRequest request = Client.Add(
                SourceGeneratorLibraryProbe.NewtonsoftJsonPackageId + "@"
                + SourceGeneratorLibraryProbe.NewtonsoftJsonPackageVersion);
            void Poll()
            {
                if (!request.IsCompleted)
                {
                    return;
                }

                EditorApplication.update -= Poll;
                if (request.Status == StatusCode.Success)
                {
                    Debug.Log("Installed " + request.Result.packageId + ".");
                }
                else
                {
                    Debug.LogError("Installing Newtonsoft.Json failed: " + request.Error?.message);
                }
            }

            EditorApplication.update += Poll;
        }

        static string HashProject()
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(Application.dataPath));
                var builder = new StringBuilder(16);
                for (int index = 0; index < 8; index++)
                {
                    builder.Append(hash[index].ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
