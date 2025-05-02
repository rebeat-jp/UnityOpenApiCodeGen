#nullable enable

using System;

using ReBeat.OpenApiCodeGen.Core;
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
        Label? _documentFilePathComment;
        TextField? _outputFolderPath;
        Label? _outputFolderPathComment;
        ProgressBar? _progressBar;

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
            _documentFilePathComment = root.Q<Label>("DocumentFilePathComment");
            _outputFolderPathComment = root.Q<Label>("OutputPathComment");
            _progressBar = root.Q<ProgressBar>("Progress");

            root.Q<Button>("GenerateButton").clicked += OnGenerate;

            _presenter.Bind(this);
        }

        public void SetOnChangeHandler(Action<GenerateMenuDto> onChangeHandler)
        {
            if (_documentFilePath == null || _outputFolderPath == null)
            {
                return;
            }

            EventCallback<FocusOutEvent> textEditedCallback = (e) =>
            {
                var dto = new GenerateMenuDto(
                    generateProvider: GenerateProvider.OpenApi,
                    apiDocumentFilePathOrUrl: _documentFilePath.text,
                    apiClientOutputFolderPath: _outputFolderPath.text);
                onChangeHandler(dto);
            };

            _documentFilePath.RegisterCallback(textEditedCallback);
            _outputFolderPath.RegisterCallback(textEditedCallback);
        }

        public void SetFormValue(GenerateMenuDto generateMenuDto)
        {
            var dto = generateMenuDto;

            _documentFilePath?.SetValueWithoutNotify(dto.ApiDocumentFilePathOrUrl);
            _outputFolderPath?.SetValueWithoutNotify(dto.ApiClientOutputFolderPath);
        }

        public void SetGenerateStatus(GenerateStatus generateStatus)
        {
            SetProgressBarValue(generateStatus.Progress, generateStatus.Message ?? "");
        }

        void OnDestroy()
        {
            _presenter.OnDestroy();
        }


        void OnGenerate()
        {
            var canGenerable = true;

            var apiDocumentFilePathOrUrl = this._documentFilePath?.value ?? "";
            var apiClientOutputPath = this._outputFolderPath?.value ?? "";

            if (string.IsNullOrEmpty(apiDocumentFilePathOrUrl))
            {
                canGenerable = false;
                SetDocumentFilePathComment("Api document file path or url is Empty");
            }

            if (string.IsNullOrEmpty(apiClientOutputPath))
            {
                canGenerable = false;
                SetOutputPathComment("Api Client file output Folder is Empty");
            }

            // 生成できない場合は返す
            if (!canGenerable)
            {
                return;
            }

            _presenter.Generate();
            // if (this._progressBar != null)
            // {
            //     Debug.Log(response.Message);
            //     this._progressBar.title = "Generating was finished";
            // }
        }

        void SetDocumentFilePathComment(string comment)
        {
            if (this._documentFilePathComment == null)
            {
                Debug.LogError(comment);
            }
            else
            {
                this._documentFilePathComment.text = comment;
            }
        }
        void SetOutputPathComment(string comment)
        {
            if (this._outputFolderPathComment == null)
            {
                Debug.LogError(comment);
            }
            else
            {
                this._outputFolderPathComment.text = comment;
            }
        }

        void SetProgressBarValue(float progress, string message)
        {
            if (this._progressBar == null)
            {
                return;
            }

            this._progressBar.value = progress;
            _progressBar.title = message;

        }

    }
}