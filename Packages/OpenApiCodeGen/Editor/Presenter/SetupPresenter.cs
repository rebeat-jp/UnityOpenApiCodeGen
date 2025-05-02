#nullable enable

using System.IO;

using Cysharp.Threading.Tasks;

using R3;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Dto;
using ReBeat.OpenApiCodeGen.Lib;
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
            _setupMenu.SetOnChangeHandler((dto) => _setupModel.SetupMenuDto.Value = dto);
            _setupModel.Status.Subscribe(s => _setupMenu.SetProgressStatus(s));
        }

        public bool CheckRunnableDockerPath()
        {
            return _setupModel.CheckRunnableDockerPath();
        }

        public void OnDestroy()
        {
            _setupModel.Dispose();
        }

        public void Setup()
        {
            _setupModel.Setup();
        }
    }
}
