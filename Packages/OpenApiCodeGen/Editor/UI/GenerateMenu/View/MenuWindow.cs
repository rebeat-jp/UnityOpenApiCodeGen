#nullable enable

using System;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Presenter;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

namespace ReBeat.OpenApiCodeGen.UI
{
    internal class MenuWindow : EditorWindow, IGenerationView
    {
        public event Action<GenerateApiClientDto>? GenerateRequested;
        public event Action<GenerateApiClientDto>? GenerateSettingChanged;

        [SerializeField]
        private VisualTreeAsset? _visualTreeAsset = default;
        TextField? _documentFilePath;
        Label? _documentFilePathComment;
        TextField? _outputFolderPath;
        Label? _outputFolderPathComment;
        ProgressBar? _progressBar;
        TextField? _generationFailureLog;
        Button? _generateButton;

        readonly IGenerationPresenter _presenter;

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
            _generationFailureLog = root.Q<TextField>("GenerationFailureLog");

            _generateButton = root.Q<Button>("GenerateButton");
            if (_generateButton != null)
            {
                _generateButton.clicked += OnGenerate;
            }

            RegisterGenerateSettingChangeHandlers();

            _presenter.Bind(this);
        }

        void OnDisable()
        {
            _presenter.Unbind();
        }

        void RegisterGenerateSettingChangeHandlers()
        {
            if (_documentFilePath == null || _outputFolderPath == null)
            {
                return;
            }

            EventCallback<FocusOutEvent> textEditedCallback = (e) =>
            {
                var dto = new GenerateApiClientDto(
                    generateProvider: GenerateProvider.OpenApi,
                    apiDocumentFilePathOrUrl: _documentFilePath.text,
                    apiClientOutputFolderPath: _outputFolderPath.text);
                GenerateSettingChanged?.Invoke(dto);
            };

            _documentFilePath.RegisterCallback(textEditedCallback);
            _outputFolderPath.RegisterCallback(textEditedCallback);
        }

        public void SetFormValue(GenerateApiClientDto generateMenuDto)
        {
            var dto = generateMenuDto;

            _documentFilePath?.SetValueWithoutNotify(dto.ApiDocumentFilePathOrUrl);
            _outputFolderPath?.SetValueWithoutNotify(dto.ApiClientOutputFolderPath);
        }

        public void SetGenerateStatus(IProgressStatus generateStatus)
        {
            var (message, failureLog) = generateStatus switch
            {
                FailedProgressStatus failed => ("Generating was failed.", failed.Reason ?? ""),
                PendingProgressStatus => ("Generating is pending...", ""),
                SucceedProgressStatus => ("Generating was succeeded.", ""),
                _ => ("", "")
            };
            SetProgressBarValue((float)generateStatus.Progress, message);
            SetGenerationFailureLog(failureLog);
        }

        void OnGenerate()
        {
            var dto = new GenerateApiClientDto(
                generateProvider: GenerateProvider.OpenApi,
                apiDocumentFilePathOrUrl: _documentFilePath?.value ?? "",
                apiClientOutputFolderPath: _outputFolderPath?.value ?? "");

            GenerateRequested?.Invoke(dto);
        }

        public void SetDocumentFilePathComment(string comment)
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

        public void SetOutputPathComment(string comment)
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

            this._progressBar.value = progress * 100.0f;
            _progressBar.title = message;

        }

        void SetGenerationFailureLog(string log)
        {
            if (_generationFailureLog == null)
            {
                if (!string.IsNullOrEmpty(log))
                {
                    Debug.LogError(log);
                }

                return;
            }

            _generationFailureLog.SetValueWithoutNotify(log);
            _generationFailureLog.style.display = string.IsNullOrWhiteSpace(log)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        public void SetInputEnabled(bool isEnabled)
        {
            _documentFilePath?.SetEnabled(isEnabled);
            _outputFolderPath?.SetEnabled(isEnabled);
            _generateButton?.SetEnabled(isEnabled);
        }

    }
}
