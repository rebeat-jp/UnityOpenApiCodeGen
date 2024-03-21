#nullable enable
using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    interface IGenerablePresenter
    {
        GeneralConfigSchema Save(string apiDocumentFilePathOrUrl, string apiClientOutputFolderPath);
        ProcessResponse Generate(string documentFilePath, string outputFolderPath);
        GeneralConfigSchema GetGenerateConfig();
    }
}