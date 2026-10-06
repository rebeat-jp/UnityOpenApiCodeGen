#nullable enable

using System;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.UI;

using UnityEngine;

namespace Rhycol.OpenApiCodeGen.Presenter
{
    class SettingPresenter : ISettingPresenter
    {
        ISettingView? _settingView;
        readonly SettingService _settingService;
        readonly Func<ProjectSettingDisplayDto, Task> _saveProjectSetting;
        readonly Action<Exception> _reportProjectSettingSaveFailure;
        readonly object _projectSettingSaveLock = new object();
        Task _projectSettingSaveTail = Task.CompletedTask;

        public SettingPresenter()
        {
            _settingService = new();
            _saveProjectSetting = _settingService.SaveProjectSettingAsync;
            _reportProjectSettingSaveFailure = Debug.LogException;
        }

        internal SettingPresenter(
            Func<ProjectSettingDisplayDto, Task> saveProjectSetting,
            Action<Exception> reportProjectSettingSaveFailure) : this()
        {
            _saveProjectSetting = saveProjectSetting ?? throw new ArgumentNullException(nameof(saveProjectSetting));
            _reportProjectSettingSaveFailure = reportProjectSettingSaveFailure
                ?? throw new ArgumentNullException(nameof(reportProjectSettingSaveFailure));
        }

        public void Bind(ISettingView settingView)
        {
            Unbind();

            _settingView = settingView;

            _settingView.UserSettingChanged += OnUserSettingChanged;
            _settingView.ProjectSettingChanged += OnProjectSettingChanged;
            _settingView.GenerationCSharpSettingChanged += OnGenerationCSharpSettingChanged;

            _ = LoadSettingsAsync();
        }

        public void Unbind()
        {
            if (_settingView == null)
            {
                return;
            }

            _settingView.UserSettingChanged -= OnUserSettingChanged;
            _settingView.ProjectSettingChanged -= OnProjectSettingChanged;
            _settingView.GenerationCSharpSettingChanged -= OnGenerationCSharpSettingChanged;
            _settingView = null;
        }

        async Task LoadSettingsAsync()
        {
            _settingView?.SetInputEnabled(false);

            try
            {
                var projectSetting = await _settingService.GetProjectSettingAsync();
                var userSetting = await _settingService.GetUserSettingAsync();
                var generationCSharpSetting = await _settingService.GetGenerationCSharpSettingAsync();

                _settingView?.SetProjectSettingValue(projectSetting ?? new ProjectSettingDisplayDto());
                _settingView?.SetUserSettingValue(userSetting ?? new UserSettingDisplayDto(""));
                _settingView?.SetGenerationCSharpSettingValue(generationCSharpSetting ?? new GenerationCSharpSettingDisplayDto());
            }
            finally
            {
                _settingView?.SetInputEnabled(true);
            }
        }

        void OnUserSettingChanged(UserSettingDisplayDto userSetting)
        {
            _ = SaveUserSettingAsync(userSetting);
        }

        async Task SaveUserSettingAsync(UserSettingDisplayDto userSetting)
        {
            await _settingService.SaveUserSettingAsync(userSetting);
        }

        void OnProjectSettingChanged(ProjectSettingDisplayDto projectSetting)
        {
            _ = SaveProjectSettingAsync(projectSetting);
        }

        internal Task SaveProjectSettingAsync(ProjectSettingDisplayDto projectSetting)
        {
            if (projectSetting == null)
            {
                throw new ArgumentNullException(nameof(projectSetting));
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task previous;
            lock (_projectSettingSaveLock)
            {
                previous = _projectSettingSaveTail;
                _projectSettingSaveTail = completion.Task;
            }

            _ = SaveProjectSettingInOrderAsync(previous, projectSetting, completion);
            return completion.Task;
        }

        async Task SaveProjectSettingInOrderAsync(
            Task previous,
            ProjectSettingDisplayDto projectSetting,
            TaskCompletionSource<bool> completion)
        {
            try
            {
                await previous;
                await _saveProjectSetting(projectSetting);
            }
            catch (Exception exception)
            {
                try
                {
                    _reportProjectSettingSaveFailure(exception);
                }
                catch (Exception reportingException)
                {
                    Debug.LogException(reportingException);
                }
            }
            finally
            {
                completion.TrySetResult(true);
            }
        }

        void OnGenerationCSharpSettingChanged(GenerationCSharpSettingDisplayDto generationCSharpSetting)
        {
            _ = SaveGenerationCSharpSettingAsync(generationCSharpSetting);
        }

        async Task SaveGenerationCSharpSettingAsync(GenerationCSharpSettingDisplayDto generationCSharpSetting)
        {
            await _settingService.SaveGenerationCSharpSettingAsync(generationCSharpSetting);
        }
    }
}
