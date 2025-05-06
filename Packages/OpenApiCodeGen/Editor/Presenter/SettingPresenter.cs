#nullable enable

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Model;
using R3;
using Cysharp.Threading.Tasks;

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

            _settingModel.Settings.Subscribe(s => _settingMenu.SetValue(s));
            _settingMenu.SetOnChangeHandler((s) => _settingModel.Settings.Value = s);
        }

        public void SaveSettings()
        {
            _settingModel.SaveSettingAsync().Forget();
        }

        public void OnDestroy()
        {
            SaveSettings();
            _settingModel.Dispose();
        }
    }
}