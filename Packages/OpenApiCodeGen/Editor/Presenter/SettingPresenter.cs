#nullable enable

using ReBeat.OpenApiCodeGen.Model;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    class SettingPresenter : ISettingPresenter
    {
        readonly SettingModel _settingModel;
        SettingMenu? _settingMenu;

        public SettingPresenter()
        {
            _settingModel = new();
        }

        public void Bind(SettingMenu settingMenu)
        {
            _settingMenu = settingMenu;

            _settingModel.OnChangeSettings += (s) => _settingMenu.SetValue(s);
            _settingMenu.SetOnChangeHandler((s) => _settingModel.Settings = s);

            _settingModel.FetchSettings();
        }

        public void SaveSettings()
        {
            _ = _settingModel.SaveSettingAsync();
        }

        public void OnDestroy()
        {
            SaveSettings();
        }
    }
}