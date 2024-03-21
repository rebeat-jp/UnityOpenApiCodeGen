using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    interface ISetupPresenter
    {
        ProcessResponse RunJavaTest(string javaPath);
        void OnEnable();
        void SetUp(string javaPath, GenerateProvider generateProvider);
    }
}