#nullable enable

using ReBeat.OpenApiCodeGen.UI;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    internal interface ISettingPresenter
    {
        void Bind(ISettingView settingView);
        void Unbind();
    }
}
