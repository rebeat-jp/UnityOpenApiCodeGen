#nullable enable

using ReBeat.OpenApiCodeGen.UI;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    interface IGenerationPresenter
    {
        void Bind(IGenerationView generationView);
        void Unbind();
    }
}
