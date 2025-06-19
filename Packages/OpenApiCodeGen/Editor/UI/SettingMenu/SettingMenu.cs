#nullable enable
using System;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;
using ReBeat.OpenApiCodeGen.Presenter;
using ReBeat.OpenApiCodeGen.UI;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

internal class SettingMenu : EditorWindow
{
    [SerializeField]
    private VisualTreeAsset? _visualTreeAsset = default;

    readonly ISettingPresenter _presenter;

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

    public void SetValue(SettingSchema settingSchema)
    {
        SetGeneralValue(settingSchema.GeneralSettings);
        SetOepnApiValue(settingSchema.OpenApiCsharpOption);
    }

    public void SetOnChangeHandler(Action<SettingSchema> onChangeHandler)
    {

        EventCallback<FocusOutEvent> focusOutEvent = (e) =>
        {
            var settings = GetCurrentValue();
            onChangeHandler(settings);
        };
        EventCallback<ChangeEvent<bool>> toggleChangeEvent = (e) =>
        {
            var settings = GetCurrentValue();
            onChangeHandler(settings);
        };
        EventCallback<ChangeEvent<Enum>> enumChangeEvent = (e) =>
        {
            var settings = GetCurrentValue();
            onChangeHandler(settings);
        };


        // General
        _generateProviderField?.RegisterValueChangedCallback(enumChangeEvent);
        _dockerPathField?.RegisterCallback(focusOutEvent);
        _defaultApiClientOutputFolderPathField?.RegisterCallback(focusOutEvent);
        _defaultApiDocumentFilePathOrUrlField?.RegisterCallback(focusOutEvent);

        // Open API
        _allowUnicodeIdentifiersField?.RegisterValueChangedCallback(toggleChangeEvent);
        _apiNameField?.RegisterCallback(focusOutEvent);
        _caseInsensitiveResponseHeadersField?.RegisterValueChangedCallback(toggleChangeEvent);
        _conditionalSerializationField?.RegisterValueChangedCallback(toggleChangeEvent);
        _disallowAdditionalPropertiesIfNotPresentField?.RegisterValueChangedCallback(toggleChangeEvent);
        _equatableField?.RegisterValueChangedCallback(toggleChangeEvent);
        _hideGenerationTimestampField?.RegisterValueChangedCallback(toggleChangeEvent);
        _interfacePrefixField?.RegisterCallback(focusOutEvent);
        _libraryField?.RegisterValueChangedCallback(enumChangeEvent);
        _licenseIdField?.RegisterCallback(focusOutEvent);
        _modelPropertyNamingField?.RegisterCallback(focusOutEvent);
        _netCoreProjectFileField?.RegisterValueChangedCallback(toggleChangeEvent);
        _nonPublicApiField?.RegisterValueChangedCallback(toggleChangeEvent);
        _nullableReferenceTypesField?.RegisterValueChangedCallback(toggleChangeEvent);
        _optionalEmitDefaultValuesField?.RegisterValueChangedCallback(toggleChangeEvent);
        _optionalMethodArgumentField?.RegisterValueChangedCallback(toggleChangeEvent);
        _optionalAssemblyInfoField?.RegisterValueChangedCallback(toggleChangeEvent);
        _optionalProjectFileField?.RegisterValueChangedCallback(toggleChangeEvent);
        _packageNameField?.RegisterCallback(focusOutEvent);
        _returnICollectionField?.RegisterValueChangedCallback(toggleChangeEvent);
        _targetFrameworkField?.RegisterCallback(focusOutEvent);
        _useCollectionField?.RegisterValueChangedCallback(toggleChangeEvent);
        _useOneOfDiscriminatorLookupField?.RegisterValueChangedCallback(toggleChangeEvent);
        _validatableField?.RegisterValueChangedCallback(toggleChangeEvent);




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
        _validatableField = root.Q<Toggle>("ValidatableFieldField");

        _presenter.Bind(this);

    }

    void SetGeneralValue(GeneralConfigSchema generalConfigSchema)
    {
        if (_generateProviderField != null)
        {
            _generateProviderField.value = generalConfigSchema.GenerateProvider;
        }
        if (_dockerPathField != null)
        {
            _dockerPathField.value = generalConfigSchema.DockerPath;
        }
        if (_defaultApiClientOutputFolderPathField != null)
        {
            _defaultApiClientOutputFolderPathField.value = generalConfigSchema.ApiClientOutputFolderPath;
        }
        if (_defaultApiDocumentFilePathOrUrlField != null)
        {
            _defaultApiDocumentFilePathOrUrlField.value = generalConfigSchema.ApiDocumentFilePathOrUrl;
        }
    }

    void SetOepnApiValue(OpenApiCsharpOption openApiCsharpOption)
    {
        if (_allowUnicodeIdentifiersField != null)
        {
            _allowUnicodeIdentifiersField.value = openApiCsharpOption.AllowUnicodeIdentifiers;
        }
        if (_apiNameField != null)
        {
            _apiNameField.value = openApiCsharpOption.ApiName;
        }
        if (_caseInsensitiveResponseHeadersField != null)
        {
            _caseInsensitiveResponseHeadersField.value = openApiCsharpOption.CaseInsensitiveResponseHeaders;
        }
        if (_conditionalSerializationField != null)
        {
            _conditionalSerializationField.value = openApiCsharpOption.ConditionalSerialization;
        }
        if (_disallowAdditionalPropertiesIfNotPresentField != null)
        {
            _disallowAdditionalPropertiesIfNotPresentField.value = openApiCsharpOption.DisallowAdditionalPropertiesIfNotPresent;
        }
        if (_equatableField != null)
        {
            _equatableField.value = openApiCsharpOption.Equatable;
        }
        if (_hideGenerationTimestampField != null)
        {
            _hideGenerationTimestampField.value = openApiCsharpOption.HideGenerationTimestamp;
        }
        if (_interfacePrefixField != null)
        {
            _interfacePrefixField.value = openApiCsharpOption.InterfacePrefix;
        }
        if (_libraryField != null)
        {
            _libraryField.value = OpenApiDependenceLibraryExtend.ConvertFromString(openApiCsharpOption.Library);
        }
        if (_licenseIdField != null)
        {
            _licenseIdField.value = openApiCsharpOption.LicenseId;
        }
        if (_modelPropertyNamingField != null)
        {
            _modelPropertyNamingField.value = openApiCsharpOption.ModelPropertyNaming;
        }
        if (_netCoreProjectFileField != null)
        {
            _netCoreProjectFileField.value = openApiCsharpOption.NetCoreProjectFile;
        }
        if (_nonPublicApiField != null)
        {
            _nonPublicApiField.value = openApiCsharpOption.NonPublicApi;
        }
        if (_nullableReferenceTypesField != null)
        {
            _nullableReferenceTypesField.value = openApiCsharpOption.NullableReferenceTypes;
        }
        if (_optionalEmitDefaultValuesField != null)
        {
            _optionalEmitDefaultValuesField.value = openApiCsharpOption.OptionalEmitDefaultValues;
        }
        if (_optionalMethodArgumentField != null)
        {
            _optionalMethodArgumentField.value = openApiCsharpOption.OptionalMethodArgument;
        }
        if (_optionalAssemblyInfoField != null)
        {
            _optionalAssemblyInfoField.value = openApiCsharpOption.OptionalAssemblyInfo;
        }
        if (_optionalProjectFileField != null)
        {
            _optionalProjectFileField.value = openApiCsharpOption.OptionalProjectFile;
        }
        if (_packageNameField != null)
        {
            _packageNameField.value = openApiCsharpOption.PackageName;
        }
        if (_returnICollectionField != null)
        {
            _returnICollectionField.value = openApiCsharpOption.ReturnICollection;
        }
        if (_targetFrameworkField != null)
        {
            _targetFrameworkField.value = openApiCsharpOption.TargetFramework;
        }
        if (_useCollectionField != null)
        {
            _useCollectionField.value = openApiCsharpOption.UseCollection;
        }
        if (_useOneOfDiscriminatorLookupField != null)
        {
            _useOneOfDiscriminatorLookupField.value = openApiCsharpOption.UseOneOfDiscriminatorLookup;
        }
        if (_validatableField != null)
        {
            _validatableField.value = openApiCsharpOption.Validatable;
        }

    }

    SettingSchema GetCurrentValue()
    {
        var generalConfigSchema = new GeneralConfigSchema(
        generateProvider: (GenerateProvider)(_generateProviderField?.value ?? GenerateProvider.OpenApi),
        dockerPath: _dockerPathField?.value ?? "",
        apiClientOutputFolderPath: _defaultApiClientOutputFolderPathField?.value ?? "",
        apiDocumentFilePathOrUrl: _defaultApiDocumentFilePathOrUrlField?.value ?? "");

        var openApiCsharpOption = new OpenApiCsharpOption();
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

        var settings = new SettingSchema(generalConfigSchema, openApiCsharpOption);

        return settings;

    }
    void OnDestroy()
    {
        _presenter.OnDestroy();
    }
}
