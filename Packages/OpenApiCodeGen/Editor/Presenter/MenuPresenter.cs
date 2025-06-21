#nullable enable
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

            _menuWindow.SetOnChangeHandler(dto => _generateModel.GenerateDto = dto);
            _generateModel.OnChangedDto += (dto) => _menuWindow.SetFormValue(dto);
            _generateModel.OnChangeStatus += (status) => _menuWindow.SetGenerateStatus(status);

            _generateModel.FetchGenerateConfig();
        }

        public void Generate()
        {
            _ = _generateModel.GenerateAsync();
        }

    }
}
