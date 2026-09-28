#nullable enable

using System;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Presenter;
using Rhycol.OpenApiCodeGen.UI;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

internal class SetupMenu : EditorWindow, ISetupView
{
    public event Action<SetupDto>? SetupRequested;
    public event Action<CheckDockerInstalledDto>? DockerCheckRequested;

    [SerializeField]
    private VisualTreeAsset? _visualTreeAsset = default;

    readonly ISetupPresenter _presenter;
    Label? _messageLabel;
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

    internal void SetOnSaveHandler(Action<SetupDto> onChangeHandler)
    {
        if (_dockerPathField == null)
        {
            return;
        }

        _dockerPathField.RegisterCallback<FocusOutEvent>((e) =>
        {
            var dto = new SetupDto(_dockerPathField.text);
            onChangeHandler(dto);
        });
    }

    public void SetProgressStatus(IProgressStatus setupStatus)
    {
        var (message, textColor) = setupStatus switch
        {
            PendingProgressStatus => ("Checking...", new Color(r: 1, g: 1, b: 1)),
            SucceedProgressStatus => ("Success", new Color(r: 1, g: 1, b: 1)),
            FailedProgressStatus => ("Failed", new Color(r: 1, g: 0, b: 0)),
            _ => ("", new Color(r: 1, g: 1, b: 1)),
        };
        DisplayMessage(message, textColor);

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
        _checkRunnableDockerButton = root.Q<Button>("CheckRunnableDockerButton");
        _setupButton = root.Q<Button>("SetupButton");

        if (_setupButton != null)
        {
            _setupButton.clicked += OnSetupInternal;
        }

        if (_checkRunnableDockerButton != null)
        {
            _checkRunnableDockerButton.clicked += CheckRunnableDockerPath;
        }

        _presenter.Bind(this);
    }

    void OnDisable()
    {
        _presenter.Unbind();
    }

    private void CheckRunnableDockerPath()
    {
        var dto = new CheckDockerInstalledDto(_dockerPathField?.text ?? "");
        DockerCheckRequested?.Invoke(dto);
    }

    public void SetDockerCheckResult(bool isRunnable)
    {
        if (_checkRunnableDockerButton == null)
        {
            return;
        }
        _checkRunnableDockerButton.text = isRunnable ? "Docker is runnable." : "Docker is not runnable.";
    }

    public void SetInputEnabled(bool isEnabled)
    {
        _dockerPathField?.SetEnabled(isEnabled);
        _checkRunnableDockerButton?.SetEnabled(isEnabled);
        _setupButton?.SetEnabled(isEnabled);
    }


    void OnSetupInternal()
    {
        var dockerPath = _dockerPathField?.text ?? "";
        var dto = new SetupDto(dockerPath);
        SetupRequested?.Invoke(dto);
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


}
