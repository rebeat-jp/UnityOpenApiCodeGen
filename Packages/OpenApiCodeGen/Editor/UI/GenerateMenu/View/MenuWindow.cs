#nullable enable

using System;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.Presenter;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

namespace Rhycol.OpenApiCodeGen.UI
{
    internal class MenuWindow : EditorWindow, IGenerationView
    {
        public event Action<GenerateApiClientDto>? GenerateRequested;
        public event Action<GenerateApiClientDto>? GenerateSettingChanged;
        public event Action? CancelRequested;

        [SerializeField]
        private VisualTreeAsset? _visualTreeAsset = default;
        TextField? _generateProviderField;
        VisualElement? _sourceGeneratorBetaNotice;
        TextField? _documentFilePath;
        Label? _documentFilePathComment;
        TextField? _outputFolderPath;
        Label? _outputFolderPathComment;
        ProgressBar? _progressBar;
        TextField? _generationFailureLog;
        TextField? _generationWarningLog;
        Button? _generateButton;
        Button? _cancelButton;
        GenerateProvider _generateProvider = GenerateProvider.OpenApi;

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

            _generateProviderField = root.Q<TextField>("GenerateProvider");
            _sourceGeneratorBetaNotice = root.Q<VisualElement>("SourceGeneratorBetaNotice");
            GenerationProviderPresentation.BindSupportLink(root);
            _documentFilePath = root.Q<TextField>("DocumentFilePath");
            _outputFolderPath = root.Q<TextField>("OutputFolderPath");
            _documentFilePathComment = root.Q<Label>("DocumentFilePathComment");
            _outputFolderPathComment = root.Q<Label>("OutputPathComment");
            _progressBar = root.Q<ProgressBar>("Progress");
            _generationFailureLog = root.Q<TextField>("GenerationFailureLog");
            _generationWarningLog = root.Q<TextField>("GenerationWarningLog");

            _generateButton = root.Q<Button>("GenerateButton");
            if (_generateButton != null)
            {
                _generateButton.clicked += OnGenerate;
            }
            _cancelButton = root.Q<Button>("CancelButton");
            if (_cancelButton != null)
            {
                _cancelButton.clicked += OnCancel;
                _cancelButton.SetEnabled(false);
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
                    generateProvider: _generateProvider,
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

            SetGenerateProvider(dto.GenerateProvider);
            _documentFilePath?.SetValueWithoutNotify(dto.ApiDocumentFilePathOrUrl);
            _outputFolderPath?.SetValueWithoutNotify(dto.ApiClientOutputFolderPath);
        }

        public void SetGenerateProvider(GenerateProvider generateProvider)
        {
            _generateProvider = generateProvider;
            GenerationProviderPresentation.UpdateBetaNotice(_sourceGeneratorBetaNotice, generateProvider);
            _generateProviderField?.SetValueWithoutNotify(GenerationProviderPresentation.GetDisplayName(_generateProvider));
            if (_outputFolderPath != null)
            {
                _outputFolderPath.label = _generateProvider == GenerateProvider.SourceGenerator
                    ? "Definition Output Folder"
                    : "Output path name";
            }
        }

        void OnCancel()
        {
            CancelRequested?.Invoke();
        }

        public void SetGenerateStatus(IProgressStatus generateStatus)
        {
            var (message, failureLog, warningLog) = generateStatus switch
            {
                FailedProgressStatus failed => ("Generating was failed.", failed.Reason ?? "", ""),
                PendingProgressStatus pending => (string.IsNullOrEmpty(pending.Message) ? "Generating is pending..." : pending.Message, "", ""),
                SucceedProgressStatus succeed => (string.IsNullOrEmpty(succeed.Message) ? "Generating was succeeded." : succeed.Message, "", ""),
                CanceledProgressStatus canceled => (canceled.Message, "", ""),
                WarningProgressStatus warning => ("Generating was succeeded with warnings.", "", warning.Message),
                _ => ("", "", "")
            };
            SetProgressBarValue((float)generateStatus.Progress, message);
            SetGenerationFailureLog(failureLog);
            SetGenerationWarningLog(warningLog);
        }

        void OnGenerate()
        {
            var dto = new GenerateApiClientDto(
                generateProvider: _generateProvider,
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

        void SetGenerationWarningLog(string log)
        {
            if (_generationWarningLog == null) return;
            _generationWarningLog.SetValueWithoutNotify(log);
            _generationWarningLog.style.display = string.IsNullOrWhiteSpace(log) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetCancelEnabled(bool isEnabled)
        {
            _cancelButton?.SetEnabled(isEnabled);
        }

    }
}
