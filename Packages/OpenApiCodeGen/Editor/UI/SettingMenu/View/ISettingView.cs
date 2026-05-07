#nullable enable
using System;

using Rhycol.OpenApiCodeGen.Core;

namespace Rhycol.OpenApiCodeGen.UI
{
    internal interface ISettingView
    {
        event Action<UserSettingDisplayDto>? UserSettingChanged;
        event Action<ProjectSettingDisplayDto>? ProjectSettingChanged;
        event Action<GenerationCSharpSettingDisplayDto>? GenerationCSharpSettingChanged;

        void SetProjectSettingValue(ProjectSettingDisplayDto projectSetting);
        void SetUserSettingValue(UserSettingDisplayDto userSetting);
        void SetGenerationCSharpSettingValue(GenerationCSharpSettingDisplayDto generationCSharpSetting);
        void SetInputEnabled(bool isEnabled);
    }


}