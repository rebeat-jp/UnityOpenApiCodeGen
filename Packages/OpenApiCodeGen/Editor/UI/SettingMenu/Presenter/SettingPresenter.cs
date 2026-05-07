#nullable enable

using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.UI;

namespace Rhycol.OpenApiCodeGen.Presenter
{
    class SettingPresenter : ISettingPresenter
    {
        ISettingView? _settingView;
        readonly SettingService _settingService;

        public SettingPresenter()
        {
            _settingService = new();
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

        async Task SaveProjectSettingAsync(ProjectSettingDisplayDto projectSetting)
        {
            await _settingService.SaveProjectSettingAsync(projectSetting);
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
