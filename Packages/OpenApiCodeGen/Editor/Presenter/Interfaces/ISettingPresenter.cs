using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    public interface ISettingPresenter
    {
        public (GeneralConfigSchema generalSettings, OpenApiConfigSchema openApiSettings) LoadSetting();
        public ProcessResponse RunJavaTest(string javaPath);
        public GeneralConfigSchema SaveGeneral(GenerateProvider generateProvider, string javaPath, string apiDocumentFilePathOrUrl, string apiClientOutputFolder);
        public OpenApiConfigSchema SaveOpenApi(
            string packageName,
            string targetFramework,
            bool includeOptionalAssemblyInfo,
            bool includeOptionalProjectFile,
            bool includeNullableReferenceTypes,
            string dependenceLibrary,
            bool isValidatable
        );
    }
}