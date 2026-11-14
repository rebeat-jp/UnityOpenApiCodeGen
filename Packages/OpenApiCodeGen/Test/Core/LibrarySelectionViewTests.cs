#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.Presenter;
using Rhycol.OpenApiCodeGen.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

internal sealed class LibrarySelectionViewTests
{
    const string SettingsUxml =
        "Packages/jp.rhycol.openapicodegen/Editor/UI/SettingMenu/View/SettingMenu.uxml";
    const string GeneratorUxml =
        "Packages/jp.rhycol.openapicodegen/Editor/UI/GenerateMenu/View/MenuWindow.uxml";

    static GenerationLibraryState Installed(string name) =>
        new GenerationLibraryState(name, GenerationLibraryStatus.Installed, "1.0");

    static GenerationLibraryState Missing(string name, string guidance) =>
        new GenerationLibraryState(name, GenerationLibraryStatus.Missing, null, null, guidance);

    static GenerationLibraryReport MissingSystemTextJsonAndUnityWebRequest() =>
        new GenerationLibraryReport(
            Installed("Newtonsoft.Json"),
            Missing("System.Text.Json", "see the steps"),
            Installed("System.Net.Http"),
            Missing("UnityWebRequest", "enable the module"));

    static SourceGeneratorLibrariesDto BuildSettings(
        GenerationHttpTransport transport,
        GenerationJsonSerializer serializer,
        bool webGl)
    {
        var registry = new GenerationProviderRegistry();
        registry.TryRegister(new InspectingProvider(MissingSystemTextJsonAndUnityWebRequest()), out _);
        return SourceGeneratorLibraryPresentation.BuildSettings(
            GenerationLibraryAssessment.Assess(
                registry,
                GenerateProvider.SourceGenerator,
                "Assets/G",
                transport,
                serializer),
            webGl);
    }

    [Test]
    public void SettingsSectionShowsStatesDisablesUnavailableChoicesAndOffersTheActions()
    {
        var window = ScriptableObject.CreateInstance<SettingMenu>();
        try
        {
            VisualElement root = CloneSettings(window);
            var foldout = root.Q<Foldout>("SourceGeneratorLibrariesFoldout");
            var http = root.Q<RadioButtonGroup>("HttpTransportGroup");
            var json = root.Q<RadioButtonGroup>("JsonSerializerGroup");
            var httpDetails = root.Q<VisualElement>("HttpTransportDetails");
            var jsonDetails = root.Q<VisualElement>("JsonSerializerDetails");
            Assert.That(foldout, Is.Not.Null);
            Assert.That(http, Is.Not.Null);
            Assert.That(json, Is.Not.Null);

            window.SetProjectSettingValue(new ProjectSettingDisplayDto(
                GenerateProvider.SourceGenerator, "spec.json", "Assets/G"));
            window.SetSourceGeneratorLibraries(
                BuildSettings(GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson, false));

            Assert.That(foldout.ClassListContains("source-generator-libraries-hidden"), Is.False);
            var httpButtons = http.Query<RadioButton>().ToList();
            Assert.That(httpButtons, Has.Count.EqualTo(2));
            Assert.That(httpButtons[0].enabledSelf, Is.True);
            Assert.That(httpButtons[1].enabledSelf, Is.False, "UnityWebRequest is not ready");
            var jsonButtons = json.Query<RadioButton>().ToList();
            Assert.That(jsonButtons[0].enabledSelf, Is.True);
            Assert.That(jsonButtons[1].enabledSelf, Is.False, "System.Text.Json is not ready");

            Assert.That(
                httpDetails.Query<Label>().ToList().ConvertAll(label => label.text),
                Has.Some.Contains("UnityWebRequest").And.Some.Contains("enable the module"));
            Assert.That(
                httpDetails.Query<Button>().ToList().ConvertAll(button => button.text),
                Is.EqualTo(new[] { "Enable the Unity Web Request module" }));
            Assert.That(
                jsonDetails.Query<Button>().ToList().ConvertAll(button => button.text),
                Is.EqualTo(new[] { "Show installation steps" }));

            // The manual steps stay hidden until requested, and never edit the manifest.
            var steps = jsonDetails.Q<TextField>();
            Assert.That(steps, Is.Not.Null);
            Assert.That(steps.style.display.value, Is.EqualTo(DisplayStyle.None));
            Invoke(window, "OnLibraryAction", LibraryInstallAction.ShowSystemTextJsonSteps);
            steps = root.Q<VisualElement>("JsonSerializerDetails").Q<TextField>();
            Assert.That(steps.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(steps.value, Does.Contain("org.nuget.system.text.json"));
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    [Test]
    public void SettingsSectionIsHiddenForDockerAndShowsTheSavedSelection()
    {
        var window = ScriptableObject.CreateInstance<SettingMenu>();
        try
        {
            VisualElement root = CloneSettings(window);
            var foldout = root.Q<Foldout>("SourceGeneratorLibrariesFoldout");

            window.SetSourceGeneratorLibraries(
                BuildSettings(GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson, false));
            window.SetProjectSettingValue(new ProjectSettingDisplayDto(
                GenerateProvider.OpenApi, "spec.json", "Assets/G"));
            Assert.That(foldout.ClassListContains("source-generator-libraries-hidden"), Is.True);

            window.SetSourceGeneratorLibraries(SourceGeneratorLibrariesDto.Hidden);
            window.SetProjectSettingValue(new ProjectSettingDisplayDto(
                GenerateProvider.SourceGenerator,
                "spec.json",
                "Assets/G",
                httpTransport: GenerationHttpTransport.UnityWebRequest,
                jsonSerializer: GenerationJsonSerializer.SystemTextJson));
            Assert.That(foldout.ClassListContains("source-generator-libraries-hidden"), Is.True);

            ProjectSettingDisplayDto current = window.GetCurrentProjectSettingValue();
            Assert.That(current.HttpTransport, Is.EqualTo(GenerationHttpTransport.UnityWebRequest));
            Assert.That(current.JsonSerializer, Is.EqualTo(GenerationJsonSerializer.SystemTextJson));
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    [Test]
    public void SettingsSectionShowsTheWebGlRecommendationAndWarning()
    {
        var window = ScriptableObject.CreateInstance<SettingMenu>();
        try
        {
            VisualElement root = CloneSettings(window);
            window.SetProjectSettingValue(new ProjectSettingDisplayDto(
                GenerateProvider.SourceGenerator, "spec.json", "Assets/G"));
            window.SetSourceGeneratorLibraries(
                BuildSettings(GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson, true));

            var boxes = root.Q<VisualElement>("LibrariesWebGlMessages").Query<HelpBox>().ToList();
            Assert.That(boxes, Has.Count.EqualTo(2));
            Assert.That(boxes[0].messageType, Is.EqualTo(HelpBoxMessageType.Info));
            Assert.That(boxes[0].text, Does.Contain("UnityWebRequest is recommended"));
            Assert.That(boxes[1].messageType, Is.EqualTo(HelpBoxMessageType.Warning));
            Assert.That(boxes[1].text, Does.Contain("OACG014"));

            window.SetSourceGeneratorLibraries(
                BuildSettings(GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson, false));
            Assert.That(root.Q<VisualElement>("LibrariesWebGlMessages").childCount, Is.EqualTo(0));
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    [Test]
    public void GeneratorDisablesGenerateWithTheReasonWhileTheLibrariesAreMissing()
    {
        var window = ScriptableObject.CreateInstance<MenuWindow>();
        try
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GeneratorUxml);
            Assert.That(tree, Is.Not.Null);
            tree.CloneTree(window.rootVisualElement);
            VisualElement root = window.rootVisualElement;
            var generate = root.Q<Button>("GenerateButton");
            BindField(window, "_generateButton", generate);
            BindField(window, "_libraryReadiness", root.Q<VisualElement>("LibraryReadiness"));
            BindField(window, "_librarySummaryLabel", root.Q<Label>("LibrarySummaryLabel"));
            BindField(window, "_libraryProblemsLabel", root.Q<Label>("LibraryProblemsLabel"));
            BindField(window, "_libraryWarningsLabel", root.Q<Label>("LibraryWarningsLabel"));
            BindField(window, "_openSettingsButton", root.Q<Button>("OpenSettingsButton"));

            window.SetLibraryReadiness(new GenerationLibraryReadinessDto(
                true,
                "Libraries: HttpClient + System.Text.Json (not ready)",
                true,
                new[] { "System.Text.Json (not installed): see the steps" },
                Array.Empty<string>()));
            Assert.That(generate.enabledSelf, Is.False);
            Assert.That(root.Q<VisualElement>("LibraryReadiness").ClassListContains("library-readiness-hidden"), Is.False);
            Assert.That(root.Q<Label>("LibrarySummaryLabel").text, Does.Contain("not ready"));
            Assert.That(root.Q<Label>("LibraryProblemsLabel").text, Does.Contain("see the steps"));
            Assert.That(root.Q<Button>("OpenSettingsButton").style.display.value, Is.EqualTo(DisplayStyle.Flex));

            // Re-enabling the form must not enable Generate while the libraries are missing.
            window.SetInputEnabled(true);
            Assert.That(generate.enabledSelf, Is.False);

            window.SetLibraryReadiness(new GenerationLibraryReadinessDto(
                true,
                "Libraries: HttpClient + Newtonsoft.Json (ready)",
                false,
                Array.Empty<string>(),
                Array.Empty<string>()));
            Assert.That(generate.enabledSelf, Is.True);
            window.SetInputEnabled(false);
            Assert.That(generate.enabledSelf, Is.False);
            window.SetInputEnabled(true);
            Assert.That(generate.enabledSelf, Is.True);

            window.SetLibraryReadiness(GenerationLibraryReadinessDto.NotApplicable);
            Assert.That(root.Q<VisualElement>("LibraryReadiness").ClassListContains("library-readiness-hidden"), Is.True);
            Assert.That(generate.enabledSelf, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    [Test]
    public void PresenterShowsTheReadinessAndRefusesToGenerateWhileBlocked()
    {
        var provider = new InspectingProvider(MissingSystemTextJsonAndUnityWebRequest());
        var registry = new GenerationProviderRegistry();
        Assert.That(registry.TryRegister(provider, out string reason), Is.True, reason);
        var service = new GenerationService(
            new StubRepository<ProjectSetting>(new ProjectSetting(
                GenerateProvider.SourceGenerator,
                "Assets/Specs/spec.json",
                "Assets/G",
                jsonSerializer: GenerationJsonSerializer.SystemTextJson)),
            new StubRepository<GenerationCSharpSetting>(new GenerationCSharpSetting()),
            registry);
        var presenter = new MenuPresenter(service);
        var view = new RecordingView();
        try
        {
            presenter.Bind(view);

            Assert.That(view.Readiness, Is.Not.Null);
            Assert.That(view.Readiness!.IsBlocking, Is.True);
            Assert.That(view.Readiness.Summary, Does.Contain("HttpClient + System.Text.Json"));

            view.RaiseGenerate(new GenerateApiClientDto(
                GenerateProvider.SourceGenerator, "Assets/Specs/spec.json", "Assets/G"));

            Assert.That(provider.GenerateCalls, Is.EqualTo(0), "a blocked combination must not reach the provider");
            Assert.That(view.Status, Is.InstanceOf<FailedProgressStatus>());
            Assert.That(((FailedProgressStatus)view.Status!).Reason, Does.Contain("see the steps"));

            // Changing the output folder re-evaluates the libraries.
            provider.Report = new GenerationLibraryReport(
                Installed("Newtonsoft.Json"),
                Installed("System.Text.Json"),
                Installed("System.Net.Http"),
                Installed("UnityWebRequest"));
            view.RaiseSettingChanged(new GenerateApiClientDto(
                GenerateProvider.SourceGenerator, "Assets/Specs/spec.json", "Assets/Other"));
            Assert.That(view.Readiness!.IsBlocking, Is.False);
            Assert.That(view.Readiness.Summary, Does.Contain("(ready)"));

            view.RaiseGenerate(new GenerateApiClientDto(
                GenerateProvider.SourceGenerator, "Assets/Specs/spec.json", "Assets/Other"));
            Assert.That(provider.GenerateCalls, Is.EqualTo(1));
        }
        finally
        {
            presenter.Unbind();
        }
    }

    [Test]
    public void GeneratorLineUpdatesWhenTheSelectionIsChangedInSettings()
    {
        var provider = new InspectingProvider(MissingSystemTextJsonAndUnityWebRequest());
        var registry = new GenerationProviderRegistry();
        Assert.That(registry.TryRegister(provider, out string reason), Is.True, reason);
        var repository = new MutableRepository<ProjectSetting>(
            new ProjectSetting(GenerateProvider.SourceGenerator, "Assets/Specs/spec.json", "Assets/G"));
        var service = new GenerationService(
            repository,
            new StubRepository<GenerationCSharpSetting>(new GenerationCSharpSetting()),
            registry);
        var presenter = new MenuPresenter(service);
        var view = new RecordingView();
        try
        {
            presenter.Bind(view);
            Assert.That(view.Readiness!.IsBlocking, Is.False);
            Assert.That(view.Readiness.Summary, Does.Contain("HttpClient + Newtonsoft.Json"));

            repository.Value = new ProjectSetting(
                GenerateProvider.SourceGenerator,
                "Assets/Specs/spec.json",
                "Assets/G",
                jsonSerializer: GenerationJsonSerializer.SystemTextJson);
            ProjectSettingChangeNotification.PublishSaved();

            Assert.That(view.Readiness!.IsBlocking, Is.True);
            Assert.That(view.Readiness.Summary, Does.Contain("System.Text.Json"));
        }
        finally
        {
            presenter.Unbind();
        }
    }

    [Test]
    public void PresenterHidesTheLineForProvidersWithoutLibraryChecks()
    {
        var registry = new GenerationProviderRegistry();
        var service = new GenerationService(
            new StubRepository<ProjectSetting>(new ProjectSetting(GenerateProvider.OpenApi, "spec.json", "Assets/G")),
            new StubRepository<GenerationCSharpSetting>(new GenerationCSharpSetting()),
            registry);
        var presenter = new MenuPresenter(service);
        var view = new RecordingView();
        try
        {
            presenter.Bind(view);
            Assert.That(view.Readiness, Is.Not.Null);
            Assert.That(view.Readiness!.IsApplicable, Is.False);
        }
        finally
        {
            presenter.Unbind();
        }
    }

    static VisualElement CloneSettings(SettingMenu window)
    {
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SettingsUxml);
        Assert.That(tree, Is.Not.Null);
        tree.CloneTree(window.rootVisualElement);
        VisualElement root = window.rootVisualElement;
        SettingMenu.InitializeEnumFields(root);
        BindField(window, "_sourceGeneratorLibrariesFoldout", root.Q<Foldout>("SourceGeneratorLibrariesFoldout"));
        BindField(window, "_librariesMessageLabel", root.Q<Label>("LibrariesMessageLabel"));
        BindField(window, "_librariesWebGlMessages", root.Q<VisualElement>("LibrariesWebGlMessages"));
        BindField(window, "_httpTransportGroup", root.Q<RadioButtonGroup>("HttpTransportGroup"));
        BindField(window, "_jsonSerializerGroup", root.Q<RadioButtonGroup>("JsonSerializerGroup"));
        BindField(window, "_httpTransportDetails", root.Q<VisualElement>("HttpTransportDetails"));
        BindField(window, "_jsonSerializerDetails", root.Q<VisualElement>("JsonSerializerDetails"));
        BindField(window, "_generateProviderField", root.Q<VisualElement>("GenerateProviderField"));
        return root;
    }

    static void BindField(object target, string fieldName, object? value)
    {
        FieldInfo? field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field!.SetValue(target, value);
    }

    static void Invoke(object target, string methodName, params object[] arguments)
    {
        MethodInfo? method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, methodName);
        method!.Invoke(target, arguments);
    }

    sealed class RecordingView : IGenerationView
    {
        public event Action<GenerateApiClientDto>? GenerateRequested;
        public event Action<GenerateApiClientDto>? GenerateSettingChanged;
        public event Action? CancelRequested;

        public GenerationLibraryReadinessDto? Readiness { get; private set; }
        public IProgressStatus? Status { get; private set; }

        public void RaiseGenerate(GenerateApiClientDto dto) => GenerateRequested?.Invoke(dto);

        public void RaiseSettingChanged(GenerateApiClientDto dto) => GenerateSettingChanged?.Invoke(dto);

        public void SetFormValue(GenerateApiClientDto generateMenuDto)
        {
        }

        public void SetGenerateProvider(GenerateProvider generateProvider)
        {
        }

        public void SetGenerateStatus(IProgressStatus generateStatus) => Status = generateStatus;

        public void SetDocumentFilePathComment(string comment)
        {
        }

        public void SetOutputPathComment(string comment)
        {
        }

        public void SetInputEnabled(bool isEnabled)
        {
        }

        public void SetLibraryReadiness(GenerationLibraryReadinessDto readiness) => Readiness = readiness;
    }

    sealed class InspectingProvider : IGenerationProvider, IGenerationLibraryInspector
    {
        public InspectingProvider(GenerationLibraryReport report)
        {
            Report = report;
        }

        public GenerationLibraryReport Report { get; set; }

        public int GenerateCalls { get; private set; }

        public GenerationProviderDescriptor Descriptor { get; } =
            new GenerationProviderDescriptor(
                GenerateProvider.SourceGenerator,
                "Inspecting",
                GenerationProviderAvailability.Available());

        public GenerationLibraryReport Inspect(string outputFolderPath) => Report;

        public GenerationResult Generate(GenerationRequest request)
        {
            GenerateCalls++;
            return GenerationResult.Success();
        }

        public Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            GenerateCalls++;
            return Task.FromResult(GenerationResult.Success());
        }
    }

    sealed class MutableRepository<T> : IAsyncRepository<T> where T : class
    {
        internal MutableRepository(T value)
        {
            Value = value;
        }

        internal T Value { get; set; }

        public Task<T?> ReadAsync() => Task.FromResult<T?>(Value);

        public Task SaveAsync(T newValue) => Task.CompletedTask;

        public Task DeleteAsync() => Task.CompletedTask;
    }

    sealed class StubRepository<T> : IAsyncRepository<T> where T : class
    {
        readonly T? value;

        internal StubRepository(T? value)
        {
            this.value = value;
        }

        public Task<T?> ReadAsync() => Task.FromResult(value);

        public Task SaveAsync(T newValue) => Task.CompletedTask;

        public Task DeleteAsync() => Task.CompletedTask;
    }
}
