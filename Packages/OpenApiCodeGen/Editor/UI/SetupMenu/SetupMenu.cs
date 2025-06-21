#nullable enable

using System;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Dto;
using ReBeat.OpenApiCodeGen.Model;
using ReBeat.OpenApiCodeGen.Presenter;
using ReBeat.OpenApiCodeGen.UI;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

public class SetupMenu : EditorWindow
{
    [SerializeField]
    private VisualTreeAsset? _visualTreeAsset = default;

    readonly ISetupPresenter _presenter;
    Label? _messageLabel;
    EnumField? _providerField;
    TextField? _dockerPathField;
    Button? _checkRunnableDockerButton;
    Button? _setupButton;
    public SetupMenu()
    {
        _presenter = new SetupPresenter();
    }

    [MenuItem("Window/OpenAPI Code Generator/Setup")]
    public static void ShowExample()
    {
        SetupMenu wnd = GetWindow<SetupMenu>();
        wnd.titleContent = new GUIContent("SetupMenu");
    }

    internal void SetOnChangeHandler(Action<SetupMenuDto> onChangeHandler)
    {
        if (_dockerPathField == null || _providerField == null)
        {
            return;
        }

        _dockerPathField.RegisterCallback<FocusOutEvent>((e) =>
        {
            var dto = new SetupMenuDto((GenerateProvider)_providerField.value, _dockerPathField.text);
            onChangeHandler(dto);
        });

        _providerField.RegisterValueChangedCallback(e =>
        {
            var dto = new SetupMenuDto((GenerateProvider)e.newValue, _dockerPathField.text);
            onChangeHandler(dto);

        });
    }

    internal void SetProgressStatus(SetupStatus setupStatus)
    {
        if (setupStatus.Message != null)
        {
            var textColor = setupStatus.Type switch
            {
                SetupStatusType.None => new Color(r: 1, g: 1, b: 1),
                SetupStatusType.Pending => new Color(r: 1, g: 1, b: 1),
                SetupStatusType.Success => new Color(r: 1, g: 1, b: 1),
                SetupStatusType.Error => new Color(r: 1, g: 0, b: 0),
                _ => new Color(r: 1, g: 1, b: 1),
            };
            DisplayMessage(setupStatus.Message, textColor);
        }

        var isPending = setupStatus.Type == SetupStatusType.Pending;

        SetOperable(isPending);
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

        _messageLabel = root.Q<Label>("ErrorMessageLabel");
        _dockerPathField = root.Q<TextField>("DockerPathField");
        _providerField = root.Q<EnumField>("ProviderField");
        _checkRunnableDockerButton = root.Q<Button>("CheckRunnableDockerButton");
        _setupButton = root.Q<Button>("SetupButton");

        _setupButton.clicked += OnSetup;
        _checkRunnableDockerButton.clicked += CheckRunnableDockerPath;

        _presenter.Bind(this);
    }


    void SetOperable(bool isOperable)
    {
        if (_dockerPathField != null)
        {
            _dockerPathField.isReadOnly = isOperable;
        }
    }


    void OnSetup()
    {
        _presenter.Setup();
    }

    void DisplayMessage(string message, Color textColor)
    {
        if (_messageLabel == null)
        {
            return;
        }
        _messageLabel.text = message;
        _messageLabel.style.color = textColor;
    }


    void CheckRunnableDockerPath()
    {
        var isRunnable = _presenter.CheckRunnableDockerPath();
        if (_checkRunnableDockerButton != null)
        {
            _checkRunnableDockerButton.text = isRunnable ? "OK" : "NG";
        }
    }
}
