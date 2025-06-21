#nullable enable
using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.UI;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    interface IGenerablePresenter
    {
        void Generate();
        void Bind(MenuWindow menuWindow);
    }
}