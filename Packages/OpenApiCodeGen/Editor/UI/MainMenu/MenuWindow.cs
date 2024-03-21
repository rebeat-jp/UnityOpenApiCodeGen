#nullable enable

using ReBeat.OpenApiCodeGen.Presenter;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

namespace ReBeat.OpenApiCodeGen.UI
{
    public class MenuWindow : EditorWindow
    {
        [SerializeField]
        private VisualTreeAsset? _visualTreeAsset = default;
        TextField? _documentFilePath;
        TextField? _outputFolderPath;

        readonly IGenerablePresenter _presenter;

        public MenuWindow()
        {
            _presenter = new MenuPresenter();
        }

        [MenuItem("Window/OpenAPI Code Generator/Generator")]
        public static void ShowExample()
        {
            MenuWindow wnd = GetWindow<MenuWindow>();
            wnd.titleContent = new GUIContent("OpenAPI Code Generator");
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

            _documentFilePath = root.Q<TextField>("DocumentFilePath");
            _outputFolderPath = root.Q<TextField>("OutputFolderPath");
            var config = _presenter.GetGenerateConfig();
            this._documentFilePath.value = config.ApiDocumentFilePathOrUrl;
            this._outputFolderPath.value = config.ApiClientOutputFolderPath;

            root.Q<Button>("GenerateButton").clicked += OnGenerate;
        }


        public void OnGenerate()
        {
            var apiDocumentFilePathOrUrl = this._documentFilePath?.value ?? "";
            var apiClientOutputPath = this._outputFolderPath?.value ?? "";

            if (string.IsNullOrEmpty(apiDocumentFilePathOrUrl))
            {
                Debug.LogError("Api document file path or url is Empty");
                return;
            }

            if (string.IsNullOrEmpty(apiClientOutputPath))
            {
                Debug.LogWarning("Api Client file output Folder is Empty");
            }

            var response = _presenter.Generate(apiDocumentFilePathOrUrl, apiClientOutputPath);
        }

    }
}