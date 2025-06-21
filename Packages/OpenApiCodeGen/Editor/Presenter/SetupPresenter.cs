#nullable enable

using ReBeat.OpenApiCodeGen.Model;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    internal class SetupPresenter : ISetupPresenter
    {
        readonly SetupModel _setupModel;
        SetupMenu? _setupMenu;
        public SetupPresenter()
        {
            _setupModel = new();
        }

        public void Bind(SetupMenu setupMenu)
        {
            _setupMenu = setupMenu;
            _setupMenu.SetOnChangeHandler((dto) => _setupModel.SetupMenuDto = dto);
            _setupModel.OnChangeStatus += (s) => _setupMenu.SetProgressStatus(s);
        }

        public bool CheckRunnableDockerPath()
        {
            return _setupModel.CheckRunnableDockerPath();
        }

        public void Setup()
        {
            _setupModel.Setup();
        }
    }
}
