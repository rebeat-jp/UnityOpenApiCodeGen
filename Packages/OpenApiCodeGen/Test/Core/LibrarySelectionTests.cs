#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.Lib;
using Rhycol.OpenApiCodeGen.UI;
using UnityEngine;

internal sealed class LibrarySelectionTests
{
    static GenerationLibraryState Installed(string name, string version = "1.0.0")
    {
        return new GenerationLibraryState(name, GenerationLibraryStatus.Installed, version);
    }

    static GenerationLibraryState Missing(string name, string guidance = "install it")
    {
        return new GenerationLibraryState(name, GenerationLibraryStatus.Missing, null, null, guidance);
    }

    static GenerationLibraryReport Report(
        GenerationLibraryState? newtonsoft = null,
        GenerationLibraryState? systemTextJson = null,
        GenerationLibraryState? httpClient = null,
        GenerationLibraryState? unityWebRequest = null)
    {
        return new GenerationLibraryReport(
            newtonsoft ?? Installed("Newtonsoft.Json", "13.0.0"),
            systemTextJson ?? Missing("System.Text.Json"),
            httpClient ?? Installed("System.Net.Http"),
            unityWebRequest ?? Installed("UnityWebRequest"));
    }

    [Test]
    public void ContractEnumsKeepTheirPersistedValues()
    {
        Assert.That((int)GenerationHttpTransport.HttpClient, Is.EqualTo(0));
        Assert.That((int)GenerationHttpTransport.UnityWebRequest, Is.EqualTo(1));
        Assert.That((int)GenerationJsonSerializer.NewtonsoftJson, Is.EqualTo(0));
        Assert.That((int)GenerationJsonSerializer.SystemTextJson, Is.EqualTo(1));
    }

    [Test]
    public void RequestDefaultsToTheOriginalCombinationAndKeepsTheConstructors()
    {
        var request = new GenerationRequest("spec.json", "Assets/Generated");
        Assert.That(request.HttpTransport, Is.EqualTo(GenerationHttpTransport.HttpClient));
        Assert.That(request.JsonSerializer, Is.EqualTo(GenerationJsonSerializer.NewtonsoftJson));
        var fourArguments = new GenerationRequest("spec.json", "Assets/Generated", "Api", "Example");
        Assert.That(fourArguments.HttpTransport, Is.EqualTo(GenerationHttpTransport.HttpClient));
    }

    [Test]
    public void ReportReadyOnlyWhenTheCombinationLibrariesAreInstalled()
    {
        GenerationLibraryReport report = Report();
        Assert.That(
            report.IsReady(GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson),
            Is.True);
        Assert.That(
            report.IsReady(GenerationHttpTransport.HttpClient, GenerationJsonSerializer.SystemTextJson),
            Is.False);
        IReadOnlyList<string> problems = report.GetProblems(
            GenerationHttpTransport.HttpClient,
            GenerationJsonSerializer.SystemTextJson);
        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("System.Text.Json").And.Contain("install it"));
    }

    [Test]
    public void UnityWebRequestNeedsTheHttpTypesToo()
    {
        GenerationLibraryReport report = Report(httpClient: Missing("System.Net.Http", "enable http"));
        Assert.That(
            report.IsReady(GenerationHttpTransport.UnityWebRequest, GenerationJsonSerializer.NewtonsoftJson),
            Is.False);
        Assert.That(
            report.GetProblems(GenerationHttpTransport.UnityWebRequest, GenerationJsonSerializer.NewtonsoftJson),
            Has.Count.EqualTo(1));
    }

    [Test]
    public void UnresolvedReportBlocksEveryCombinationWithItsReason()
    {
        GenerationLibraryReport report = GenerationLibraryReport.Unresolved("no asmdef");
        IReadOnlyList<string> problems = report.GetProblems(
            GenerationHttpTransport.HttpClient,
            GenerationJsonSerializer.NewtonsoftJson);
        Assert.That(problems, Is.EqualTo(new[] { "no asmdef" }));
        Assert.That(report.ResolutionFailure, Is.EqualTo("no asmdef"));
    }

    [Test]
    public void StateDescriptionsNameTheStatus()
    {
        Assert.That(Installed("A", "2.0").Describe(), Is.EqualTo("A 2.0 (installed)"));
        Assert.That(Missing("A").Describe(), Is.EqualTo("A (not installed)"));
        Assert.That(
            new GenerationLibraryState("A", GenerationLibraryStatus.TooOld, "7.0", "8.0").Describe(),
            Does.Contain("7.0").And.Contain("8.0"));
        Assert.That(
            new GenerationLibraryState("A", GenerationLibraryStatus.NotReferenced).Describe(),
            Does.Contain("not referenced"));
    }

    [Test]
    public void ProjectSettingFromAnOldFileKeepsTheOriginalCombination()
    {
        ProjectSetting setting = ReadProjectSetting(
            "{\"GenerateProvider\":1,\"ApiDocumentFilePathOrUrl\":\"a.json\",\"ApiClientOutputFolderPath\":\"Assets/G\"}");
        Assert.That(setting.GenerateProvider, Is.EqualTo(GenerateProvider.SourceGenerator));
        Assert.That(setting.HttpTransport, Is.EqualTo(GenerationHttpTransport.HttpClient));
        Assert.That(setting.JsonSerializer, Is.EqualTo(GenerationJsonSerializer.NewtonsoftJson));
    }

    [Test]
    public void ProjectSettingReadsTheNewFields()
    {
        ProjectSetting setting = ReadProjectSetting(
            "{\"GenerateProvider\":1,\"HttpTransport\":1,\"JsonSerializer\":1}");
        Assert.That(setting.HttpTransport, Is.EqualTo(GenerationHttpTransport.UnityWebRequest));
        Assert.That(setting.JsonSerializer, Is.EqualTo(GenerationJsonSerializer.SystemTextJson));
    }

    [Test]
    public void ProjectSettingJsonRoundTripsTheSelection()
    {
        Type jsonType = GetProjectSettingJsonType();
        MethodInfo fromDomain = jsonType.GetMethod("FromDomain", BindingFlags.Public | BindingFlags.Static)!;
        object json = fromDomain.Invoke(
            null,
            new object[]
            {
                new ProjectSetting(
                    GenerateProvider.SourceGenerator,
                    "a.json",
                    "Assets/G",
                    httpTransport: GenerationHttpTransport.UnityWebRequest,
                    jsonSerializer: GenerationJsonSerializer.SystemTextJson),
            })!;
        string text = JsonUtility.ToJson(json);
        Assert.That(text, Does.Contain("\"HttpTransport\":1").And.Contain("\"JsonSerializer\":1"));
        ProjectSetting back = ReadProjectSetting(text);
        Assert.That(back.HttpTransport, Is.EqualTo(GenerationHttpTransport.UnityWebRequest));
        Assert.That(back.JsonSerializer, Is.EqualTo(GenerationJsonSerializer.SystemTextJson));
    }

    [Test]
    public void SettingDtosCarryTheSelection()
    {
        var dto = new ProjectSettingDisplayDto(
            GenerateProvider.SourceGenerator,
            "a.json",
            "Assets/G",
            httpTransport: GenerationHttpTransport.UnityWebRequest,
            jsonSerializer: GenerationJsonSerializer.SystemTextJson);
        Assert.That(dto.HttpTransport, Is.EqualTo(GenerationHttpTransport.UnityWebRequest));
        Assert.That(new ProjectSettingDisplayDto().HttpTransport, Is.EqualTo(GenerationHttpTransport.HttpClient));
        Assert.That(new GenerateApiClientDto().JsonSerializer, Is.EqualTo(GenerationJsonSerializer.NewtonsoftJson));
    }

    [Test]
    public void ReadinessIsNotApplicableForProvidersWithoutAnInspector()
    {
        var registry = new GenerationProviderRegistry();
        Assert.That(registry.TryRegister(new PlainProvider(), out string reason), Is.True, reason);
        GenerationLibraryAssessment assessment = GenerationLibraryAssessment.Assess(
            registry,
            GenerateProvider.SourceGenerator,
            "Assets/G",
            GenerationHttpTransport.HttpClient,
            GenerationJsonSerializer.NewtonsoftJson);
        Assert.That(assessment.IsApplicable, Is.False);
        Assert.That(SourceGeneratorLibraryPresentation.BuildReadiness(assessment, false).IsApplicable, Is.False);
        Assert.That(SourceGeneratorLibraryPresentation.BuildSettings(assessment, false).IsVisible, Is.False);
    }

    [Test]
    public void ReadinessReadyBlockedAndNotChecked()
    {
        var inspector = new InspectingProvider(Report());
        GenerationProviderRegistry registry = RegistryWith(inspector);

        GenerationLibraryReadinessDto ready = SourceGeneratorLibraryPresentation.BuildReadiness(
            Assess(registry, "Assets/G", GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson),
            false);
        Assert.That(ready.IsApplicable, Is.True);
        Assert.That(ready.IsBlocking, Is.False);
        Assert.That(ready.Summary, Is.EqualTo("Libraries: HttpClient + Newtonsoft.Json (ready)"));

        GenerationLibraryReadinessDto blocked = SourceGeneratorLibraryPresentation.BuildReadiness(
            Assess(registry, "Assets/G", GenerationHttpTransport.HttpClient, GenerationJsonSerializer.SystemTextJson),
            false);
        Assert.That(blocked.IsBlocking, Is.True);
        Assert.That(blocked.Summary, Does.Contain("System.Text.Json").And.Contain("not ready"));
        Assert.That(blocked.Problems, Has.Count.EqualTo(1));
        Assert.That(blocked.BuildBlockingMessage(), Does.Contain("install it"));

        GenerationLibraryReadinessDto unchecked_ = SourceGeneratorLibraryPresentation.BuildReadiness(
            Assess(registry, "", GenerationHttpTransport.HttpClient, GenerationJsonSerializer.SystemTextJson),
            false);
        Assert.That(unchecked_.IsBlocking, Is.False);
        Assert.That(unchecked_.Summary, Does.Contain("set the output folder"));
    }

    [Test]
    public void UnresolvedOutputFolderIsShownButDoesNotBlock()
    {
        GenerationProviderRegistry registry = RegistryWith(
            new InspectingProvider(GenerationLibraryReport.Unresolved("no asmdef")));
        GenerationLibraryReadinessDto dto = SourceGeneratorLibraryPresentation.BuildReadiness(
            Assess(registry, "Assets/G", GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson),
            false);
        Assert.That(dto.IsApplicable, Is.True);
        Assert.That(dto.IsBlocking, Is.False);
        Assert.That(dto.Summary, Does.Contain("no asmdef"));
    }

    [Test]
    public void ReadinessWarnsAboutHttpClientOnWebGl()
    {
        GenerationProviderRegistry registry = RegistryWith(new InspectingProvider(Report()));
        GenerationLibraryReadinessDto http = SourceGeneratorLibraryPresentation.BuildReadiness(
            Assess(registry, "Assets/G", GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson),
            true);
        Assert.That(http.Warnings, Has.Count.EqualTo(1));
        Assert.That(http.IsBlocking, Is.False);

        GenerationLibraryReadinessDto uwr = SourceGeneratorLibraryPresentation.BuildReadiness(
            Assess(registry, "Assets/G", GenerationHttpTransport.UnityWebRequest, GenerationJsonSerializer.NewtonsoftJson),
            true);
        Assert.That(uwr.Warnings, Is.Empty);
    }

    [Test]
    public void SettingsDisableChoicesWhoseLibraryIsNotReadyAndOfferTheAction()
    {
        GenerationLibraryReport report = Report(
            unityWebRequest: Missing("UnityWebRequest", "enable module"));
        GenerationProviderRegistry registry = RegistryWith(new InspectingProvider(report));
        SourceGeneratorLibrariesDto dto = SourceGeneratorLibraryPresentation.BuildSettings(
            Assess(registry, "Assets/G", GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson),
            false);

        Assert.That(dto.IsVisible, Is.True);
        LibraryChoiceDto http = dto.HttpChoices[(int)GenerationHttpTransport.HttpClient];
        Assert.That(http.IsSelectable, Is.True);
        LibraryChoiceDto uwr = dto.HttpChoices[(int)GenerationHttpTransport.UnityWebRequest];
        Assert.That(uwr.IsSelectable, Is.False);
        Assert.That(uwr.Action, Is.EqualTo(LibraryInstallAction.EnableUnityWebRequestModule));
        Assert.That(uwr.Guidance, Is.EqualTo("enable module"));
        LibraryChoiceDto newtonsoft = dto.JsonChoices[(int)GenerationJsonSerializer.NewtonsoftJson];
        Assert.That(newtonsoft.IsInstalled, Is.True);
        Assert.That(newtonsoft.Action, Is.EqualTo(LibraryInstallAction.None));
        LibraryChoiceDto stj = dto.JsonChoices[(int)GenerationJsonSerializer.SystemTextJson];
        Assert.That(stj.IsSelectable, Is.False);
        Assert.That(stj.Action, Is.EqualTo(LibraryInstallAction.ShowSystemTextJsonSteps));
    }

    [Test]
    public void SettingsOfferTheNewtonsoftInstallOnlyWhenItIsMissing()
    {
        GenerationLibraryReport report = Report(newtonsoft: Missing("Newtonsoft.Json"));
        SourceGeneratorLibrariesDto dto = SourceGeneratorLibraryPresentation.BuildSettings(
            Assess(
                RegistryWith(new InspectingProvider(report)),
                "Assets/G",
                GenerationHttpTransport.HttpClient,
                GenerationJsonSerializer.NewtonsoftJson),
            false);
        LibraryChoiceDto choice = dto.JsonChoices[(int)GenerationJsonSerializer.NewtonsoftJson];
        Assert.That(choice.Action, Is.EqualTo(LibraryInstallAction.AddNewtonsoftJsonPackage));
        Assert.That(LibraryInstaller.GetPackageSpecifier(choice.Action), Is.EqualTo("com.unity.nuget.newtonsoft-json@3.2.2"));
        Assert.That(
            LibraryInstaller.GetPackageSpecifier(LibraryInstallAction.EnableUnityWebRequestModule),
            Is.EqualTo("com.unity.modules.unitywebrequest"));
        Assert.That(LibraryInstaller.GetPackageSpecifier(LibraryInstallAction.ShowSystemTextJsonSteps), Is.Null);
    }

    [Test]
    public void NotReferencedLibraryOffersGuidanceButNoInstallButton()
    {
        GenerationLibraryReport report = Report(
            newtonsoft: new GenerationLibraryState(
                "Newtonsoft.Json",
                GenerationLibraryStatus.NotReferenced,
                null,
                null,
                "reference it"));
        SourceGeneratorLibrariesDto dto = SourceGeneratorLibraryPresentation.BuildSettings(
            Assess(
                RegistryWith(new InspectingProvider(report)),
                "Assets/G",
                GenerationHttpTransport.HttpClient,
                GenerationJsonSerializer.NewtonsoftJson),
            false);
        LibraryChoiceDto choice = dto.JsonChoices[(int)GenerationJsonSerializer.NewtonsoftJson];
        Assert.That(choice.Action, Is.EqualTo(LibraryInstallAction.None));
        Assert.That(choice.Guidance, Is.EqualTo("reference it"));
        Assert.That(choice.IsSelectable, Is.False);
    }

    [Test]
    public void SettingsShowWebGlNoticeAndWarning()
    {
        GenerationProviderRegistry registry = RegistryWith(new InspectingProvider(Report()));
        SourceGeneratorLibrariesDto http = SourceGeneratorLibraryPresentation.BuildSettings(
            Assess(registry, "Assets/G", GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson),
            true);
        Assert.That(http.WebGlNotice, Does.Contain("UnityWebRequest is recommended"));
        Assert.That(http.WebGlWarning, Does.Contain("OACG014"));

        SourceGeneratorLibrariesDto uwr = SourceGeneratorLibraryPresentation.BuildSettings(
            Assess(registry, "Assets/G", GenerationHttpTransport.UnityWebRequest, GenerationJsonSerializer.NewtonsoftJson),
            true);
        Assert.That(uwr.WebGlNotice, Is.Not.Empty);
        Assert.That(uwr.WebGlWarning, Is.Empty);

        SourceGeneratorLibrariesDto standalone = SourceGeneratorLibraryPresentation.BuildSettings(
            Assess(registry, "Assets/G", GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson),
            false);
        Assert.That(standalone.WebGlNotice, Is.Empty);
        Assert.That(standalone.WebGlWarning, Is.Empty);
    }

    [Test]
    public void SettingsWithoutAnOutputFolderLeaveEveryChoiceSelectable()
    {
        SourceGeneratorLibrariesDto dto = SourceGeneratorLibraryPresentation.BuildSettings(
            Assess(
                RegistryWith(new InspectingProvider(Report())),
                "",
                GenerationHttpTransport.HttpClient,
                GenerationJsonSerializer.NewtonsoftJson),
            false);
        Assert.That(dto.IsVisible, Is.True);
        Assert.That(dto.Message, Does.Contain("Output Folder"));
        foreach (LibraryChoiceDto choice in dto.HttpChoices) Assert.That(choice.IsSelectable, Is.True);
        foreach (LibraryChoiceDto choice in dto.JsonChoices) Assert.That(choice.IsSelectable, Is.True);
    }

    [Test]
    public void SystemTextJsonStepsNameTheRegistryPackageAndEncodingsWeb()
    {
        string steps = SourceGeneratorLibraryPresentation.BuildSystemTextJsonSteps();
        Assert.That(steps, Does.Contain("https://unitynuget-registry.openupm.com"));
        Assert.That(steps, Does.Contain("org.nuget.system.text.json"));
        Assert.That(steps, Does.Contain("org.nuget"));
        Assert.That(steps, Does.Contain("System.Text.Encodings.Web"));
    }

    [Test]
    public void GenerationServiceSendsTheSavedSelectionInTheRequest()
    {
        var recording = new InspectingProvider(Report());
        GenerationProviderRegistry registry = RegistryWith(recording);
        var service = new GenerationService(
            new StubRepository<ProjectSetting>(new ProjectSetting(
                generateProvider: GenerateProvider.SourceGenerator,
                httpTransport: GenerationHttpTransport.UnityWebRequest,
                jsonSerializer: GenerationJsonSerializer.SystemTextJson)),
            new StubRepository<GenerationCSharpSetting>(new GenerationCSharpSetting()),
            registry);

        service.GenerateApiClientAsync(
            new GenerateApiClientDto(GenerateProvider.SourceGenerator, "spec.json", "Assets/G")).GetAwaiter().GetResult();

        Assert.That(recording.LastRequest, Is.Not.Null);
        Assert.That(recording.LastRequest!.HttpTransport, Is.EqualTo(GenerationHttpTransport.UnityWebRequest));
        Assert.That(recording.LastRequest.JsonSerializer, Is.EqualTo(GenerationJsonSerializer.SystemTextJson));
    }

    [Test]
    public void GenerationServiceReportsReadinessThroughTheRegistry()
    {
        GenerationProviderRegistry registry = RegistryWith(new InspectingProvider(Report()));
        var service = new GenerationService(
            new StubRepository<ProjectSetting>(null),
            new StubRepository<GenerationCSharpSetting>(new GenerationCSharpSetting()),
            registry);
        GenerationLibraryReadinessDto dto = service.GetLibraryReadiness(
            GenerateProvider.SourceGenerator,
            "Assets/G",
            GenerationHttpTransport.HttpClient,
            GenerationJsonSerializer.SystemTextJson);
        Assert.That(dto.IsBlocking, Is.True);
        Assert.That(
            service.GetLibraryReadiness(
                GenerateProvider.OpenApi,
                "Assets/G",
                GenerationHttpTransport.HttpClient,
                GenerationJsonSerializer.NewtonsoftJson).IsApplicable,
            Is.False);
    }

    static GenerationLibraryAssessment Assess(
        GenerationProviderRegistry registry,
        string folder,
        GenerationHttpTransport transport,
        GenerationJsonSerializer serializer)
    {
        return GenerationLibraryAssessment.Assess(
            registry,
            GenerateProvider.SourceGenerator,
            folder,
            transport,
            serializer);
    }

    static GenerationProviderRegistry RegistryWith(IGenerationProvider provider)
    {
        var registry = new GenerationProviderRegistry();
        Assert.That(registry.TryRegister(provider, out string reason), Is.True, reason);
        return registry;
    }

    static Type GetProjectSettingJsonType()
    {
        Type? type = typeof(ProjectSettingJsonRepository).GetNestedType(
            "ProjectSettingJson",
            BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);
        return type!;
    }

    static ProjectSetting ReadProjectSetting(string json)
    {
        Type type = GetProjectSettingJsonType();
        object value = JsonUtility.FromJson(json, type);
        return (ProjectSetting)type.GetMethod("ToDomain")!.Invoke(value, null)!;
    }

    sealed class PlainProvider : IGenerationProvider
    {
        public GenerationProviderDescriptor Descriptor { get; } =
            new GenerationProviderDescriptor(
                GenerateProvider.SourceGenerator,
                "Plain",
                GenerationProviderAvailability.Available());

        public GenerationResult Generate(GenerationRequest request) => GenerationResult.Success();

        public Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(GenerationResult.Success());
    }

    sealed class InspectingProvider : IGenerationProvider, IGenerationLibraryInspector
    {
        readonly GenerationLibraryReport report;

        public InspectingProvider(GenerationLibraryReport report)
        {
            this.report = report;
        }

        public GenerationProviderDescriptor Descriptor { get; } =
            new GenerationProviderDescriptor(
                GenerateProvider.SourceGenerator,
                "Inspecting",
                GenerationProviderAvailability.Available());

        public GenerationRequest? LastRequest { get; private set; }

        public GenerationLibraryReport Inspect(string outputFolderPath) => report;

        public GenerationResult Generate(GenerationRequest request)
        {
            LastRequest = request;
            return GenerationResult.Success();
        }

        public Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(GenerationResult.Success());
        }
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
