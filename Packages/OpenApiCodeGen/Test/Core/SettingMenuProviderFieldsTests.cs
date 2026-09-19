#nullable enable
using System.Reflection;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
#if !UNITY_2022_1_OR_NEWER
using EnumField = UnityEditor.UIElements.EnumField;
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
                var provider = root.Q<EnumField>("GenerateProviderField");
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

        static void BindField(SettingMenu window, string name, object value)
        {
            typeof(SettingMenu).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, value);
        }
    }
}
