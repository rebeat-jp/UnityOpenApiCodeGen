#nullable enable

using ReBeat.OpenApiCodeGen.Core;
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

    TextField? _javaPathField;
    EnumField? _providerField;
    Label? _javaTestResultText;
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

        _javaTestResultText = root.Q<Label>("JavaTestResultText");
        _javaPathField = root.Q<TextField>("JavaPathField");
        _providerField = root.Q<EnumField>("ProviderField");

        root.Q<Button>("TestButton").clicked += OnRunTest;
        root.Q<Button>("SetupButton").clicked += OnSetup;

    }

    void OnEnable()
    {
        _presenter.OnEnable();
    }


    void OnRunTest()
    {
        if (_javaPathField is null)
        {
            Debug.LogWarning("Java Path Field is null");
            return;
        }
        var result = _presenter.RunJavaTest(_javaPathField.value);

        switch (result.ExitStatus)
        {
            case 0:
                DisplayJavaTestResultLabel("OK! This is valid path.", StyleStatus.Success);
                break;
            default:
                DisplayJavaTestResultLabel("NG! This is invalid Path.", StyleStatus.Error);
                break;
        }
    }

    void DisplayJavaTestResultLabel(string content, StyleStatus status)
    {
        if (_javaTestResultText is null)
        {
            Debug.LogWarning("JavaTestResultLabel is empty");
            return;
        }

        _javaTestResultText.text = content;
        _javaTestResultText.style.color = status switch
        {
            StyleStatus.Success => Color.green,
            StyleStatus.Info => Color.white,
            StyleStatus.Warn => Color.yellow,
            StyleStatus.Error => Color.red,
            _ => throw new System.NotImplementedException(),
        }; ;
    }

    void OnSetup()
    {
        if (_javaPathField is null || _providerField is null)
        {
            Debug.LogWarning("Field is null");
            return;
        }
        _presenter.SetUp(_javaPathField.value, (GenerateProvider)_providerField.value);
    }
}
