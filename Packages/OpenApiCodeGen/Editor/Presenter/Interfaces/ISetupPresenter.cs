using System;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Dto;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    interface ISetupPresenter
    {
        void Bind(SetupMenu setupMenu);
        void Setup();
        bool CheckRunnableDockerPath();
    }
}