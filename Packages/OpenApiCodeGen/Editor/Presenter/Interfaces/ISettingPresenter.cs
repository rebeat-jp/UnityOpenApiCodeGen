using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    internal interface ISettingPresenter
    {
        public SettingSchema LoadSetting();
        public SettingSchema SaveSettings(SettingSchema settingSchema);
    }
}