#nullable enable
using System;
using System.IO;

using Cysharp.Threading.Tasks;

using R3;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;
using ReBeat.OpenApiCodeGen.Model;
using ReBeat.OpenApiCodeGen.UI;


namespace ReBeat.OpenApiCodeGen.Presenter
{
    public class MenuPresenter : IGenerablePresenter
    {
        readonly GenerateModel _generateModel;
        MenuWindow? _menuWindow;
        public MenuPresenter()
        {
            _generateModel = new();
        }

        public void Bind(MenuWindow menuWindow)
        {
            _menuWindow = menuWindow;

            _menuWindow.SetOnChangeHandler(dto => _generateModel.GenerateDto.Value = dto);
            _generateModel.GenerateDto.Subscribe((dto) => _menuWindow.SetFormValue(dto));
            _generateModel.Status.Subscribe(status => _menuWindow.SetGenerateStatus(status));
        }

        public void Generate()
        {
            _generateModel.GenerateAsync().Forget();
        }

        public void OnDestroy()
        {
            _generateModel.Dispose();
        }
    }
}
