#nullable enable

using System;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.UI;

namespace Rhycol.OpenApiCodeGen.UI
{
    interface ISetupPresenter
    {
        void Bind(ISetupView setupView);
        void Unbind();
    }
}
