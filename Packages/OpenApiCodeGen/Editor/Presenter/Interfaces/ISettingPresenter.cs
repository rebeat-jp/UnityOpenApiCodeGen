using System;

using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    internal interface ISettingPresenter
    {
        public void SaveSettings();
        public void OnDestroy();
        public void Bind(SettingMenu settingMenu);
    }
}