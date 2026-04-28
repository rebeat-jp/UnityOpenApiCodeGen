#nullable enable
using System;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Presenter;
using ReBeat.OpenApiCodeGen.UI;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

internal class SettingMenu : EditorWindow, ISettingView
{
    public event Action<UserSettingDisplayDto>? UserSettingChanged;
    public event Action<ProjectSettingDisplayDto>? ProjectSettingChanged;
    public event Action<GenerationCSharpSettingDisplayDto>? GenerationCSharpSettingChanged;

    [SerializeField]
    private VisualTreeAsset? _visualTreeAsset = default;

    readonly ISettingPresenter _presenter;

    #region UI Elements
    /* General */
    EnumField? _generateProviderField;
    TextField? _dockerPathField;
    TextField? _defaultApiDocumentFilePathOrUrlField;
    TextField? _defaultApiClientOutputFolderPathField;

    /* Open API */
    Toggle? _allowUnicodeIdentifiersField;
    TextField? _apiNameField;
    Toggle? _caseInsensitiveResponseHeadersField;
    Toggle? _conditionalSerializationField;
    Toggle? _disallowAdditionalPropertiesIfNotPresentField;
    Toggle? _equatableField;
    Toggle? _hideGenerationTimestampField;
    TextField? _interfacePrefixField;
    EnumField? _libraryField;
    TextField? _licenseIdField;
    TextField? _modelPropertyNamingField;
    Toggle? _netCoreProjectFileField;
    Toggle? _nonPublicApiField;
    Toggle? _nullableReferenceTypesField;
    Toggle? _optionalEmitDefaultValuesField;
    Toggle? _optionalMethodArgumentField;
    Toggle? _optionalAssemblyInfoField;
    Toggle? _optionalProjectFileField;
    TextField? _packageNameField;
    Toggle? _returnICollectionField;
    TextField? _targetFrameworkField;
    Toggle? _useCollectionField;
    Toggle? _useOneOfDiscriminatorLookupField;
    Toggle? _validatableField;
    #endregion

    public SettingMenu()
    {
        _presenter = new SettingPresenter();
    }

    [MenuItem("Window/OpenAPI Code Generator/Settings")]
    public static void ShowExample()
    {
        SettingMenu wnd = GetWindow<SettingMenu>();
        wnd.titleContent = new GUIContent("SettingMenu");
    }

    void RegisterProjectSettingChangeHandlers()
    {

        void FocusOutEvent(FocusOutEvent e)
        {
            var settings = GetCurrentProjectSettingValue();
            ProjectSettingChanged?.Invoke(settings);
        }
        void EnumChangeEvent(ChangeEvent<Enum> e)
        {
            var settings = GetCurrentProjectSettingValue();
            ProjectSettingChanged?.Invoke(settings);
        }


        // General
        _generateProviderField?.RegisterValueChangedCallback(EnumChangeEvent);
        _defaultApiClientOutputFolderPathField?.RegisterCallback((EventCallback<FocusOutEvent>)FocusOutEvent);
        _defaultApiDocumentFilePathOrUrlField?.RegisterCallback((EventCallback<FocusOutEvent>)FocusOutEvent);

    }

    void RegisterUserSettingChangeHandlers()
    {
        void FocusOutEvent(FocusOutEvent e)
        {
            var settings = GetUserSettingDisplayDto();
            UserSettingChanged?.Invoke(settings);
        }

        _dockerPathField?.RegisterCallback((EventCallback<FocusOutEvent>)FocusOutEvent);
    }

    void RegisterGenerationCSharpSettingChangeHandlers()
    {
        void FocusOutEvent(FocusOutEvent e)
        {
            var settings = GetGenerationCSharpSetting();
            GenerationCSharpSettingChanged?.Invoke(settings);
        }
        void ToggleChangeEvent(ChangeEvent<bool> e)
        {
            var settings = GetGenerationCSharpSetting();
            GenerationCSharpSettingChanged?.Invoke(settings);
        }
        void EnumChangeEvent(ChangeEvent<Enum> e)
        {
            var settings = GetGenerationCSharpSetting();
            GenerationCSharpSettingChanged?.Invoke(settings);
        }
        // Attach callbacks for Open API settings fields
        _allowUnicodeIdentifiersField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _apiNameField?.RegisterCallback((EventCallback<FocusOutEvent>)FocusOutEvent);
        _caseInsensitiveResponseHeadersField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _conditionalSerializationField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _disallowAdditionalPropertiesIfNotPresentField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _equatableField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _hideGenerationTimestampField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _interfacePrefixField?.RegisterCallback((EventCallback<FocusOutEvent>)FocusOutEvent);
        _libraryField?.RegisterValueChangedCallback(EnumChangeEvent);
        _licenseIdField?.RegisterCallback((EventCallback<FocusOutEvent>)FocusOutEvent);
        _modelPropertyNamingField?.RegisterCallback((EventCallback<FocusOutEvent>)FocusOutEvent);
        _netCoreProjectFileField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _nonPublicApiField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _nullableReferenceTypesField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _optionalEmitDefaultValuesField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _optionalMethodArgumentField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _optionalAssemblyInfoField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _optionalProjectFileField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _packageNameField?.RegisterCallback((EventCallback<FocusOutEvent>)FocusOutEvent);
        _returnICollectionField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _targetFrameworkField?.RegisterCallback((EventCallback<FocusOutEvent>)FocusOutEvent);
        _useCollectionField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _useOneOfDiscriminatorLookupField?.RegisterValueChangedCallback(ToggleChangeEvent);
        _validatableField?.RegisterValueChangedCallback(ToggleChangeEvent);
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

        // General
        _generateProviderField = root.Q<EnumField>("GenerateProviderField");
        _dockerPathField = root.Q<TextField>("DockerPathField");
        _defaultApiClientOutputFolderPathField = root.Q<TextField>("DefaultOutputFolderPathField");
        _defaultApiDocumentFilePathOrUrlField = root.Q<TextField>("DefaultApiDocumentFilePathOrUrlField");

        // Open API
        _allowUnicodeIdentifiersField = root.Q<Toggle>("AllowUnicodeIdentifiersField");
        _apiNameField = root.Q<TextField>("ApiNameField");
        _caseInsensitiveResponseHeadersField = root.Q<Toggle>("CaseInsensitiveResponseHeadersField");
        _conditionalSerializationField = root.Q<Toggle>("ConditionalSerializationField");
        _disallowAdditionalPropertiesIfNotPresentField = root.Q<Toggle>("DisallowAdditionalPropertiesIfNotPresentField");
        _equatableField = root.Q<Toggle>("EquatableField");
        _hideGenerationTimestampField = root.Q<Toggle>("HideGenerationTimestampField");
        _interfacePrefixField = root.Q<TextField>("InterfacePrefixField");
        _libraryField = root.Q<EnumField>("LibraryField");
        _licenseIdField = root.Q<TextField>("LicenseIdField");
        _modelPropertyNamingField = root.Q<TextField>("ModelPropertyNamingField");
        _netCoreProjectFileField = root.Q<Toggle>("NetCoreProjectFileField");
        _nonPublicApiField = root.Q<Toggle>("NonPublicApiField");
        _nullableReferenceTypesField = root.Q<Toggle>("NullableReferenceTypesField");
        _optionalEmitDefaultValuesField = root.Q<Toggle>("OptionalEmitDefaultValuesField");
        _optionalMethodArgumentField = root.Q<Toggle>("OptionalMethodArgumentField");
        _optionalAssemblyInfoField = root.Q<Toggle>("OptionalAssemblyInfoField");
        _optionalProjectFileField = root.Q<Toggle>("OptionalProjectFileField");
        _packageNameField = root.Q<TextField>("PackageNameField");
        _returnICollectionField = root.Q<Toggle>("ReturnICollectionField");
        _targetFrameworkField = root.Q<TextField>("TargetFrameworkField");
        _useCollectionField = root.Q<Toggle>("UseCollectionField");
        _useOneOfDiscriminatorLookupField = root.Q<Toggle>("UseOneOfDiscriminatorLookupField");
        _validatableField = root.Q<Toggle>("ValidatableField");

        RegisterUserSettingChangeHandlers();
        RegisterProjectSettingChangeHandlers();
        RegisterGenerationCSharpSettingChangeHandlers();

        _presenter.Bind(this);

    }

    void OnDisable()
    {
        _presenter.Unbind();
    }

    public void SetProjectSettingValue(ProjectSettingDisplayDto projectSetting)
    {
        _generateProviderField?.SetValueWithoutNotify(projectSetting.GenerateProvider);
        _defaultApiClientOutputFolderPathField?.SetValueWithoutNotify(projectSetting.ApiClientOutputFolderPath);
        _defaultApiDocumentFilePathOrUrlField?.SetValueWithoutNotify(projectSetting.ApiDocumentFilePathOrUrl);
    }
    public void SetUserSettingValue(UserSettingDisplayDto userSetting)
    {
        _dockerPathField?.SetValueWithoutNotify(userSetting.DockerPath);
    }

    public void SetGenerationCSharpSettingValue(GenerationCSharpSettingDisplayDto generationCSharpSetting)
    {
        _allowUnicodeIdentifiersField?.SetValueWithoutNotify(generationCSharpSetting.AllowUnicodeIdentifiers);
        _apiNameField?.SetValueWithoutNotify(generationCSharpSetting.ApiName);
        _caseInsensitiveResponseHeadersField?.SetValueWithoutNotify(generationCSharpSetting.CaseInsensitiveResponseHeaders);
        _conditionalSerializationField?.SetValueWithoutNotify(generationCSharpSetting.ConditionalSerialization);
        _disallowAdditionalPropertiesIfNotPresentField?.SetValueWithoutNotify(generationCSharpSetting.DisallowAdditionalPropertiesIfNotPresent);
        _equatableField?.SetValueWithoutNotify(generationCSharpSetting.Equatable);
        _hideGenerationTimestampField?.SetValueWithoutNotify(generationCSharpSetting.HideGenerationTimestamp);
        _interfacePrefixField?.SetValueWithoutNotify(generationCSharpSetting.InterfacePrefix);
        _libraryField?.SetValueWithoutNotify(OpenApiDependenceLibraryExtend.ConvertFromString(generationCSharpSetting.Library));
        _licenseIdField?.SetValueWithoutNotify(generationCSharpSetting.LicenseId);
        _modelPropertyNamingField?.SetValueWithoutNotify(generationCSharpSetting.ModelPropertyNaming);
        _netCoreProjectFileField?.SetValueWithoutNotify(generationCSharpSetting.NetCoreProjectFile);
        _nonPublicApiField?.SetValueWithoutNotify(generationCSharpSetting.NonPublicApi);
        _nullableReferenceTypesField?.SetValueWithoutNotify(generationCSharpSetting.NullableReferenceTypes);
        _optionalEmitDefaultValuesField?.SetValueWithoutNotify(generationCSharpSetting.OptionalEmitDefaultValues);
        _optionalMethodArgumentField?.SetValueWithoutNotify(generationCSharpSetting.OptionalMethodArgument);
        _optionalAssemblyInfoField?.SetValueWithoutNotify(generationCSharpSetting.OptionalAssemblyInfo);
        _optionalProjectFileField?.SetValueWithoutNotify(generationCSharpSetting.OptionalProjectFile);
        _packageNameField?.SetValueWithoutNotify(generationCSharpSetting.PackageName);
        _returnICollectionField?.SetValueWithoutNotify(generationCSharpSetting.ReturnICollection);
        _targetFrameworkField?.SetValueWithoutNotify(generationCSharpSetting.TargetFramework);
        _useCollectionField?.SetValueWithoutNotify(generationCSharpSetting.UseCollection);
        _useOneOfDiscriminatorLookupField?.SetValueWithoutNotify(generationCSharpSetting.UseOneOfDiscriminatorLookup);
        _validatableField?.SetValueWithoutNotify(generationCSharpSetting.Validatable);

    }

    public ProjectSettingDisplayDto GetCurrentProjectSettingValue()
    {
        var projectSetting = new ProjectSettingDisplayDto(
            generateProvider: (GenerateProvider)(_generateProviderField?.value ?? GenerateProvider.OpenApi),
            apiClientOutputFolderPath: _defaultApiClientOutputFolderPathField?.value ?? "",
            apiDocumentFilePathOrUrl: _defaultApiDocumentFilePathOrUrlField?.value ?? ""
        );
        return projectSetting;
    }

    public UserSettingDisplayDto GetUserSettingDisplayDto()
    {
        var userSettingDisplayDto = new UserSettingDisplayDto(
            dockerPath: _dockerPathField?.value ?? ""
        );
        return userSettingDisplayDto;
    }

    public GenerationCSharpSettingDisplayDto GetGenerationCSharpSetting()
    {
        var openApiCsharpOption = new GenerationCSharpSettingDisplayDto();
        if (_allowUnicodeIdentifiersField != null)
        {
            openApiCsharpOption.AllowUnicodeIdentifiers = _allowUnicodeIdentifiersField.value;
        }
        if (_apiNameField != null)
        {
            openApiCsharpOption.ApiName = _apiNameField.value;
        }
        if (_caseInsensitiveResponseHeadersField != null)
        {
            openApiCsharpOption.CaseInsensitiveResponseHeaders = _caseInsensitiveResponseHeadersField.value;
        }
        if (_conditionalSerializationField != null)
        {
            openApiCsharpOption.ConditionalSerialization = _conditionalSerializationField.value;
        }
        if (_disallowAdditionalPropertiesIfNotPresentField != null)
        {
            openApiCsharpOption.DisallowAdditionalPropertiesIfNotPresent = _disallowAdditionalPropertiesIfNotPresentField.value;
        }
        if (_equatableField != null)
        {
            openApiCsharpOption.Equatable = _equatableField.value;
        }
        if (_hideGenerationTimestampField != null)
        {
            openApiCsharpOption.HideGenerationTimestamp = _hideGenerationTimestampField.value;
        }
        if (_interfacePrefixField != null)
        {
            openApiCsharpOption.InterfacePrefix = _interfacePrefixField.value;
        }
        if (_libraryField != null)
        {
            openApiCsharpOption.Library = ((OpenApiDependenceLibrary)_libraryField.value).ToConfigString();
        }
        if (_licenseIdField != null)
        {
            openApiCsharpOption.LicenseId = _licenseIdField.value != string.Empty ? _licenseIdField.value : null;
        }
        if (_modelPropertyNamingField != null)
        {
            openApiCsharpOption.ModelPropertyNaming = _modelPropertyNamingField.value;
        }
        if (_netCoreProjectFileField != null)
        {
            openApiCsharpOption.NetCoreProjectFile = _netCoreProjectFileField.value;
        }
        if (_nonPublicApiField != null)
        {
            openApiCsharpOption.NonPublicApi = _nonPublicApiField.value;
        }
        if (_nullableReferenceTypesField != null)
        {
            openApiCsharpOption.NullableReferenceTypes = _nullableReferenceTypesField.value;
        }
        if (_optionalEmitDefaultValuesField != null)
        {
            openApiCsharpOption.OptionalEmitDefaultValues = _optionalEmitDefaultValuesField.value;
        }
        if (_optionalMethodArgumentField != null)
        {
            openApiCsharpOption.OptionalMethodArgument = _optionalMethodArgumentField.value;
        }
        if (_optionalAssemblyInfoField != null)
        {
            openApiCsharpOption.OptionalAssemblyInfo = _optionalAssemblyInfoField.value;
        }
        if (_optionalProjectFileField != null)
        {
            openApiCsharpOption.OptionalProjectFile = _optionalProjectFileField.value;
        }
        if (_packageNameField != null)
        {
            openApiCsharpOption.PackageName = _packageNameField.value;
        }
        if (_returnICollectionField != null)
        {
            openApiCsharpOption.ReturnICollection = _returnICollectionField.value;
        }
        if (_targetFrameworkField != null)
        {
            openApiCsharpOption.TargetFramework = _targetFrameworkField.value;
        }
        if (_useCollectionField != null)
        {
            openApiCsharpOption.UseCollection = _useCollectionField.value;
        }
        if (_useOneOfDiscriminatorLookupField != null)
        {
            openApiCsharpOption.UseOneOfDiscriminatorLookup = _useOneOfDiscriminatorLookupField.value;
        }
        if (_validatableField != null)
        {
            openApiCsharpOption.Validatable = _validatableField.value;
        }


        return openApiCsharpOption;

    }

    public void SetInputEnabled(bool isEnabled)
    {
        _generateProviderField?.SetEnabled(isEnabled);
        _dockerPathField?.SetEnabled(isEnabled);
        _defaultApiClientOutputFolderPathField?.SetEnabled(isEnabled);
        _defaultApiDocumentFilePathOrUrlField?.SetEnabled(isEnabled);

        _allowUnicodeIdentifiersField?.SetEnabled(isEnabled);
        _apiNameField?.SetEnabled(isEnabled);
        _caseInsensitiveResponseHeadersField?.SetEnabled(isEnabled);
        _conditionalSerializationField?.SetEnabled(isEnabled);
        _disallowAdditionalPropertiesIfNotPresentField?.SetEnabled(isEnabled);
        _equatableField?.SetEnabled(isEnabled);
        _hideGenerationTimestampField?.SetEnabled(isEnabled);
        _interfacePrefixField?.SetEnabled(isEnabled);
        _libraryField?.SetEnabled(isEnabled);
        _licenseIdField?.SetEnabled(isEnabled);
        _modelPropertyNamingField?.SetEnabled(isEnabled);
        _netCoreProjectFileField?.SetEnabled(isEnabled);
        _nonPublicApiField?.SetEnabled(isEnabled);
        _nullableReferenceTypesField?.SetEnabled(isEnabled);
        _optionalEmitDefaultValuesField?.SetEnabled(isEnabled);
        _optionalMethodArgumentField?.SetEnabled(isEnabled);
        _optionalAssemblyInfoField?.SetEnabled(isEnabled);
        _optionalProjectFileField?.SetEnabled(isEnabled);
        _packageNameField?.SetEnabled(isEnabled);
        _returnICollectionField?.SetEnabled(isEnabled);
        _targetFrameworkField?.SetEnabled(isEnabled);
        _useCollectionField?.SetEnabled(isEnabled);
        _useOneOfDiscriminatorLookupField?.SetEnabled(isEnabled);
        _validatableField?.SetEnabled(isEnabled);
    }
}
