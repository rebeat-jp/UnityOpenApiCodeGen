#nullable enable
using System;

using Rhycol.OpenApiCodeGen.Editor.Generation;

namespace Rhycol.OpenApiCodeGen.UI
{
    internal interface IGenerationView
    {
        event Action<GenerateApiClientDto>? GenerateRequested;
        event Action<GenerateApiClientDto>? GenerateSettingChanged;

        void SetFormValue(GenerateApiClientDto generateMenuDto);
        void SetGenerateProvider(GenerateProvider generateProvider);
        void SetGenerateStatus(IProgressStatus generateStatus);
        void SetDocumentFilePathComment(string comment);
        void SetOutputPathComment(string comment);
        void SetInputEnabled(bool isEnabled);
    }

}