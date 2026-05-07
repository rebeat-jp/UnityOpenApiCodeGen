#nullable enable

using Rhycol.OpenApiCodeGen.UI;

namespace Rhycol.OpenApiCodeGen.Presenter
{
    internal interface ISettingPresenter
    {
        void Bind(ISettingView settingView);
        void Unbind();
    }
}
