#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.Lib;
using Rhycol.OpenApiCodeGen.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
#if !UNITY_2022_1_OR_NEWER
using PopupField = UnityEditor.UIElements.PopupField<Rhycol.OpenApiCodeGen.Editor.Generation.GenerateProvider>;
#else
using PopupField = UnityEngine.UIElements.PopupField<Rhycol.OpenApiCodeGen.Editor.Generation.GenerateProvider>;
#endif

namespace Rhycol.OpenApiCodeGen.Test.Core
{
    internal sealed class SettingMenuProviderFieldsTests
    {
        [Test]
        public void ProviderSwitchKeepsSharedFieldsEditableAndPreservesValues()
        {
            var window = ScriptableObject.CreateInstance<SettingMenu>();
            try
            {
                var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Packages/jp.rhycol.openapicodegen/Editor/UI/SettingMenu/View/SettingMenu.uxml");
                Assert.That(tree, Is.Not.Null);
                tree.CloneTree(window.rootVisualElement);
                var root = window.rootVisualElement;
                SettingMenu.InitializeEnumFields(root);
                var provider = root.Q<PopupField>("GenerateProviderField");
                Assert.That(provider, Is.Not.Null);
                var foldout = root.Q<Foldout>("CSharpGenerationSettingsFoldout");
                var apiName = root.Q<TextField>("ApiNameField");
                var packageName = root.Q<TextField>("PackageNameField");
                var docker = root.Q<TextField>("DockerPathField");
                BindField(window, "_generateProviderField", provider);
                BindField(window, "_cSharpGenerationSettingsFoldout", foldout);
                BindField(window, "_apiNameField", apiName);
                BindField(window, "_packageNameField", packageName);
                BindField(window, "_dockerPathField", docker);
                apiName.value = "OrdersApi";
                packageName.value = "Example.Orders";

                window.SetProjectSettingValue(new ProjectSettingDisplayDto(
                    GenerateProvider.SourceGenerator, "spec.json", "Assets/Generated"));
                Assert.That(foldout.enabledInHierarchy, Is.True);
                foreach (VisualElement field in foldout.Children())
                    Assert.That(field.enabledInHierarchy, Is.EqualTo(field == apiName || field == packageName), field.name);
                Assert.That(docker.enabledInHierarchy, Is.False);

                window.SetInputEnabled(false);
                Assert.That(apiName.enabledInHierarchy, Is.False);
                Assert.That(packageName.enabledInHierarchy, Is.False);
                window.SetInputEnabled(true);
                Assert.That(apiName.enabledInHierarchy, Is.True);
                Assert.That(packageName.enabledInHierarchy, Is.True);
                Assert.That(docker.enabledInHierarchy, Is.False);

                window.SetProjectSettingValue(new ProjectSettingDisplayDto(
                    GenerateProvider.OpenApi, "spec.json", "Assets/Generated"));
                foreach (VisualElement field in foldout.Children())
                    Assert.That(field.enabledInHierarchy, Is.True, field.name);
                Assert.That(docker.enabledInHierarchy, Is.True);
                var settings = window.GetGenerationCSharpSetting();
                Assert.That(settings.ApiName, Is.EqualTo("OrdersApi"));
                Assert.That(settings.PackageName, Is.EqualTo("Example.Orders"));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SettingsShowsBetaInChoicesAndSelectionWithoutChangingProviderIdentity()
        {
            var window = ScriptableObject.CreateInstance<SettingMenu>();
            try
            {
                var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Packages/jp.rhycol.openapicodegen/Editor/UI/SettingMenu/View/SettingMenu.uxml");
                Assert.That(tree, Is.Not.Null);
                tree.CloneTree(window.rootVisualElement);
                var root = window.rootVisualElement;
                SettingMenu.InitializeEnumFields(root);
                var provider = root.Q<PopupField>("GenerateProviderField");
                var notice = root.Q<VisualElement>("SourceGeneratorBetaNotice");
                var displayName = root.Q<Label>("GenerateProviderDisplayNameLabel");
                BindField(window, "_generateProviderField", provider);
                BindField(window, "_generateProviderDisplayNameLabel", displayName);
                BindField(window, "_sourceGeneratorBetaNotice", notice);
                Assert.That(provider.formatListItemCallback(GenerateProvider.SourceGenerator),
                    Is.EqualTo("Source Generator (Beta)"));
                Assert.That(provider.formatSelectedValueCallback(GenerateProvider.SourceGenerator),
                    Is.EqualTo("Source Generator (Beta)"));
                Assert.That(provider.choices, Does.Contain(GenerateProvider.SourceGenerator));
                Assert.That(root.Q<Button>("SourceGeneratorSupportMatrix").text, Is.EqualTo("Open Support Matrix"));
                Assert.That(notice.Q<Label>().text, Does.Contain("generation is in beta"));

                window.SetProjectSettingValue(new ProjectSettingDisplayDto(
                    GenerateProvider.SourceGenerator, "spec.json", "Assets/Generated"));
                Assert.That(displayName.text, Is.EqualTo("Source Generator (Beta)"));
                Assert.That(notice.ClassListContains("source-generator-beta-hidden"), Is.False);
                Assert.That((int)window.GetCurrentProjectSettingValue().GenerateProvider, Is.EqualTo(1));
                window.SetInputEnabled(false);
                Assert.That(provider.enabledSelf, Is.False);
                Assert.That(notice.ClassListContains("source-generator-beta-hidden"), Is.False);

                window.SetProjectSettingValue(new ProjectSettingDisplayDto(
                    GenerateProvider.OpenApi, "spec.json", "Assets/Generated"));
                Assert.That(notice.ClassListContains("source-generator-beta-hidden"), Is.True);
                Assert.That((int)window.GetCurrentProjectSettingValue().GenerateProvider, Is.EqualTo(0));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void GeneratorShowsBetaForSourceGeneratorAndHidesItForDocker()
        {
            var window = ScriptableObject.CreateInstance<MenuWindow>();
            try
            {
                var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Packages/jp.rhycol.openapicodegen/Editor/UI/GenerateMenu/View/MenuWindow.uxml");
                Assert.That(tree, Is.Not.Null);
                tree.CloneTree(window.rootVisualElement);
                var root = window.rootVisualElement;
                var provider = root.Q<TextField>("GenerateProvider");
                var notice = root.Q<VisualElement>("SourceGeneratorBetaNotice");
                BindField(window, "_generateProviderField", provider);
                BindField(window, "_sourceGeneratorBetaNotice", notice);
                window.SetGenerateProvider(GenerateProvider.SourceGenerator);
                Assert.That(provider.value, Is.EqualTo("Source Generator (Beta)"));
                Assert.That(notice.ClassListContains("source-generator-beta-hidden"), Is.False);
                Assert.That(notice.Q<Label>().text, Does.Contain("generation is in beta"));
                Assert.That(root.Q<Button>("SourceGeneratorSupportMatrix"), Is.Not.Null);
                window.SetGenerateProvider(GenerateProvider.OpenApi);
                Assert.That(provider.value, Does.Not.Contain("Beta"));
                Assert.That(notice.ClassListContains("source-generator-beta-hidden"), Is.True);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator ProjectSettingsPersistenceUsesUnityAssetsDirectoryFromExternalWorkingDirectory()
        {
            string settingsPath = Path.Combine(
                Application.dataPath,
                "OpenApiCodeGen",
                "projectSettings.json");
            string? launchDirectory =
                Environment.GetEnvironmentVariable("SOURCE_GENERATOR_VERIFY_LAUNCH_DIRECTORY");
            string? launchDirectorySettingsPath = string.IsNullOrEmpty(launchDirectory)
                ? null
                : Path.Combine(
                    Path.GetFullPath(launchDirectory),
                    "Assets",
                    "OpenApiCodeGen",
                    "projectSettings.json");
            bool hadExistingSettings = File.Exists(settingsPath);
            byte[] existingSettings = hadExistingSettings
                ? File.ReadAllBytes(settingsPath)
                : Array.Empty<byte>();
            bool hadLaunchDirectorySettings =
                launchDirectorySettingsPath != null && File.Exists(launchDirectorySettingsPath);
            byte[] existingLaunchDirectorySettings = hadLaunchDirectorySettings
                ? File.ReadAllBytes(launchDirectorySettingsPath!)
                : Array.Empty<byte>();

            if (launchDirectorySettingsPath != null)
            {
                Assert.That(Path.IsPathRooted(launchDirectory!), Is.True,
                    "The Unity regression gate must supply an absolute launch directory.");
            }
            try
            {
                var repository = new ProjectSettingJsonRepository();
                Task saveTask = repository.SaveAsync(new ProjectSetting(
                    GenerateProvider.SourceGenerator,
                    "Assets/Specs/openapi.json",
                    "Assets/Generated"));
                while (!saveTask.IsCompleted)
                {
                    yield return null;
                }
                saveTask.GetAwaiter().GetResult();

                Assert.That(ApplicationConstant.PROJECT_FOLDER_PATH,
                    Is.EqualTo(Path.Combine(Application.dataPath, "OpenApiCodeGen")));
                Assert.That(File.Exists(settingsPath), Is.True,
                    "Project settings were not saved below Application.dataPath.");
                if (launchDirectorySettingsPath != null &&
                    launchDirectorySettingsPath != settingsPath)
                {
                    Assert.That(File.Exists(launchDirectorySettingsPath), Is.False,
                        "Project settings leaked into the Unity launch directory.");
                }
            }
            finally
            {
                RestoreFile(settingsPath, hadExistingSettings, existingSettings);
                if (launchDirectorySettingsPath != null)
                {
                    RestoreFile(
                        launchDirectorySettingsPath,
                        hadLaunchDirectorySettings,
                        existingLaunchDirectorySettings);
                }
            }
        }

        static void RestoreFile(string path, bool existed, byte[] content)
        {
            if (existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, content);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        static void BindField(object window, string name, object value)
        {
            window.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, value);
        }
    }
}
