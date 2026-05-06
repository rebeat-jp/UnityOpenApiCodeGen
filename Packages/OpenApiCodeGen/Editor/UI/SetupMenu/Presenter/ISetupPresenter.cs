#nullable enable

using System;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.UI;

namespace ReBeat.OpenApiCodeGen.UI
{
    interface ISetupPresenter
    {
        void Bind(ISetupView setupView);
        void Unbind();
    }
}
