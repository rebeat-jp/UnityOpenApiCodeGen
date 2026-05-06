#nullable enable
using System;

namespace ReBeat.OpenApiCodeGen.UI
{
    internal interface IGenerationView
    {
        event Action<GenerateApiClientDto>? GenerateRequested;
        event Action<GenerateApiClientDto>? GenerateSettingChanged;

        void SetFormValue(GenerateApiClientDto generateMenuDto);
        void SetGenerateStatus(IProgressStatus generateStatus);
        void SetDocumentFilePathComment(string comment);
        void SetOutputPathComment(string comment);
        void SetInputEnabled(bool isEnabled);
    }

}