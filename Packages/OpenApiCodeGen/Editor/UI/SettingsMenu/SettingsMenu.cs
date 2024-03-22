#nullable enable

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Presenter;
using ReBeat.OpenApiCodeGen.UI;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

public class SettingsMenu : EditorWindow
{
    [SerializeField]
    private VisualTreeAsset? _visualTreeAsset = default;

    private readonly ISettingPresenter _presenter;

    // General
    EnumField? _generateProviderField;
    TextField? _javaPathField;
    Label? _javaTestResultLabel;
    TextField? _apiDocumentPathField;
    TextField? _apiClientOutputFolderPath;

    // OpenApi
    TextField? _openApiPackageNameField;
    EnumField? _openApiTargetFrameworkField;
    Toggle? _openApiIncludeOptionalAssemblyInfoField;
    Toggle? _openApiIncludeOptionalProjectFileField;
    Toggle? _openApiIncludeNullableReferenceFileField;
    EnumField? _openApiDependenceLibraryField;
    Toggle? _isValidatableField;

    public SettingsMenu()
    {
        _presenter = new SettingPresenter();
    }

    [MenuItem("Window/OpenAPI Code Generator/Settings")]
    public static void ShowExample()
    {
        SettingsMenu wnd = GetWindow<SettingsMenu>();
        wnd.titleContent = new GUIContent("SettingsMenu");
    }

    void OnEnable()
    {
        _presenter.LoadSetting();
    }

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

        if (_visualTreeAsset == null)
        {
            Debug.LogError("Unset Visual Tree Asset");
            return;
        }

        // Instantiate UXML
        VisualElement labelFromUXML = _visualTreeAsset.Instantiate();
        root.Add(labelFromUXML);

        _generateProviderField = root.Q<EnumField>("GeneratorField");
        _javaPathField = root.Q<TextField>("JavaPathField");
        _javaTestResultLabel = root.Q<Label>("JavaTestResultLabel");
        _apiDocumentPathField = root.Q<TextField>("ApiDocumentPathField");
        _apiClientOutputFolderPath = root.Q<TextField>("ApiClientFolderField");

        _openApiPackageNameField = root.Q<TextField>("OpenApiPackageName");
        _openApiTargetFrameworkField = root.Q<EnumField>("OpenApiGenerateTargetFramework");
        _openApiIncludeOptionalAssemblyInfoField = root.Q<Toggle>("OpenApiIncludeOptionalAssemblyInfo");
        _openApiIncludeOptionalProjectFileField = root.Q<Toggle>("OpenApiIncludeOptionalProjectFile");
        _openApiIncludeNullableReferenceFileField = root.Q<Toggle>("OpenApiIncludeNullableReferenceTypes");
        _openApiDependenceLibraryField = root.Q<EnumField>("OpenApiDependenceLibrary");
        _isValidatableField = root.Q<Toggle>("OpenApiIsValidatable");

        root.Q<Button>("TestButton").clicked += HandleOnClickTest;

        var (generalSettings, openApiSettings) = _presenter.LoadSetting();
        Debug.Log(GeneralConfigSchema.ConfigFilePath);
        var generalSettingsDto =
        new GeneralSettingsDto(generalSettings.GenerateProvider, generalSettings.JavaPath, generalSettings.ApiDocumentFilePathOrUrl, generalSettings.ApiClientOutputFolderPath);
        InjectGeneralSettings(generalSettingsDto);

        _generateProviderField.RegisterValueChangedCallback((e) => OnChangeGeneralSettings());
        _javaPathField.RegisterValueChangedCallback((e) => OnChangeGeneralSettings());
        _apiDocumentPathField.RegisterValueChangedCallback((e) => OnChangeGeneralSettings());
        _apiClientOutputFolderPath.RegisterValueChangedCallback((e) => OnChangeGeneralSettings());

        var openApiSettingsDto = new OpenApiSettingsDto(
            openApiSettings.PackageName,
            openApiSettings.TargetFramework,
            openApiSettings.IncludeNullableReferenceTypes,
            openApiSettings.IncludeOptionalAssemblyInfo,
            openApiSettings.IncludeOptionalProjectFile,
            openApiSettings.DependenceLibrary,
            openApiSettings.IsValidatable);
        InjectOpenApiSettings(openApiSettingsDto);

        _openApiPackageNameField.RegisterValueChangedCallback((e) => OnChangeOpenApiSettings());
        _openApiTargetFrameworkField.RegisterValueChangedCallback((e) => OnChangeOpenApiSettings());
        _openApiIncludeOptionalAssemblyInfoField.RegisterValueChangedCallback((e) => OnChangeOpenApiSettings());
        _openApiIncludeOptionalProjectFileField.RegisterValueChangedCallback((e) => OnChangeOpenApiSettings());
        _openApiIncludeNullableReferenceFileField.RegisterValueChangedCallback((e) => OnChangeOpenApiSettings());
        _openApiDependenceLibraryField.RegisterValueChangedCallback((e) => OnChangeOpenApiSettings());
        _isValidatableField.RegisterValueChangedCallback((e) => OnChangeOpenApiSettings());

    }

    void OnChangeGeneralSettings()
    {
        if (_generateProviderField is null || _javaPathField is null || _apiDocumentPathField is null || _apiClientOutputFolderPath is null)
        {
            Debug.LogError("Fatal Error: General Setting Fields is null");
            return;
        }

        var generateProvider = _generateProviderField.value;
        var javaPath = _javaPathField.value;

        var result = _presenter.SaveGeneral((GenerateProvider)generateProvider, javaPath, _apiDocumentPathField.value, _apiClientOutputFolderPath.value);

        var dto = new GeneralSettingsDto(result.GenerateProvider, result.JavaPath, result.ApiDocumentFilePathOrUrl, result.ApiClientOutputFolderPath);

        InjectGeneralSettings(dto);
    }

    void OnChangeOpenApiSettings()
    {
        if (_openApiPackageNameField is null
        || _openApiTargetFrameworkField is null
        || _openApiIncludeNullableReferenceFileField is null
        || _openApiIncludeOptionalAssemblyInfoField is null
        || _openApiIncludeOptionalProjectFileField is null
        || _openApiDependenceLibraryField is null
        || _isValidatableField is null)
        {
            Debug.LogError("Fatal Error: OpenAPI Setting Fields is null");
            return;
        }
        var packageName = _openApiPackageNameField.value;
        var targetFramework = ((OpenApiTargetFramework)_openApiTargetFrameworkField.value).ToConfigString();
        var includeOptionalProjectFile = _openApiIncludeOptionalProjectFileField.value;
        var includeNullableReferenceTypes = _openApiIncludeNullableReferenceFileField.value;
        var includeOptionalAssemblyInfo = _openApiIncludeOptionalAssemblyInfoField.value;
        var dependenceLibrary = ((OpenApiDependenceLibrary)_openApiDependenceLibraryField.value).ToConfigString();
        var isValidatable = _isValidatableField.value;

        var result = _presenter.SaveOpenApi(
            packageName,
            targetFramework,
            includeOptionalAssemblyInfo,
            includeOptionalProjectFile,
            includeNullableReferenceTypes,
            dependenceLibrary,
            isValidatable
            );

        var dto = new OpenApiSettingsDto(
            result.PackageName,
            result.TargetFramework,
            result.IncludeNullableReferenceTypes,
            result.IncludeOptionalAssemblyInfo,
            result.IncludeOptionalProjectFile,
            result.DependenceLibrary,
            result.IsValidatable
        );

        InjectOpenApiSettings(dto);
    }

    void InjectOpenApiSettings(OpenApiSettingsDto openApiSettingsDao)
    {
        if (_openApiPackageNameField is null
        || _openApiTargetFrameworkField is null
        || _openApiIncludeNullableReferenceFileField is null
        || _openApiIncludeOptionalAssemblyInfoField is null
        || _openApiIncludeOptionalProjectFileField is null
        || _openApiDependenceLibraryField is null
        || _isValidatableField is null)
        {
            Debug.LogError("Fatal Error: OpenAPI Setting Fields is null");
            return;
        }

        var dto = openApiSettingsDao;
        _openApiPackageNameField.value = dto.PackageName;
        _openApiTargetFrameworkField.value = dto.TargetFramework;
        _openApiIncludeNullableReferenceFileField.value = dto.IncludeNullableReferenceTypes;
        _openApiIncludeOptionalAssemblyInfoField.value = dto.IncludeOptionalAssemblyInfo;
        _openApiIncludeOptionalProjectFileField.value = dto.IncludeOptionalProjectFile;
        _openApiDependenceLibraryField.value = dto.DependenceLibrary;
        _isValidatableField.value = dto.IsValidatable;
    }

    void InjectGeneralSettings(GeneralSettingsDto generalSettingsDto)
    {
        if (_generateProviderField is null || _javaPathField is null || _apiDocumentPathField is null || _apiClientOutputFolderPath is null)
        {
            Debug.LogError("Fatal Error: General Setting Fields is null");
            return;
        }

        var dto = generalSettingsDto;

        _generateProviderField.value = dto.GenerateProvider;
        _javaPathField.value = dto.JavaPath;
        _apiDocumentPathField.value = dto.ApiDocumentFilePathOrUrl;
        _apiClientOutputFolderPath.value = dto.ApiClientOutputFolderPath;
    }

    void HandleOnClickTest()
    {
        if (_javaPathField is null)
        {
            Debug.LogWarning("Field is null");
            return;
        }

        var result = _presenter.RunJavaTest(_javaPathField.value);
        switch (result.ExitStatus)
        {
            case 0:
                DisplayJavaTestResultLabel("OK! This is valid path.", StyleStatus.Success);
                break;
            default:
                DisplayJavaTestResultLabel("NG! This is invalid path.", StyleStatus.Error);
                break;

        }
    }

    void DisplayJavaTestResultLabel(string content, StyleStatus status)
    {
        if (_javaTestResultLabel is null)
        {
            Debug.LogWarning("Field is null");
            return;
        }

        _javaTestResultLabel.text = content;
        _javaTestResultLabel.style.color = status switch
        {
            StyleStatus.Success => Color.green,
            StyleStatus.Info => Color.white,
            StyleStatus.Warn => Color.yellow,
            StyleStatus.Error => Color.red,
            _ => throw new System.NotImplementedException(),
        };
    }
}
