#nullable enable

using Rhycol.OpenApiCodeGen.UI;

namespace Rhycol.OpenApiCodeGen.Presenter
{
    interface IGenerationPresenter
    {
        void Bind(IGenerationView generationView);
        void Unbind();
    }
}
