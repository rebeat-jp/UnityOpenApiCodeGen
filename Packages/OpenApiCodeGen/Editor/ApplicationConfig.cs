using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Lib;

namespace Rhycol.OpenApiCodeGen
{
    internal static class ApplicationConfig
    {
        public static readonly IAsyncRepository<ProjectSetting> ProjectSettingRepository
        = new ProjectSettingJsonRepository();
        public static readonly IAsyncRepository<GenerationCSharpSetting> GenerationCsharpSettingRepository
        = new GenerationCSharpSettingJsonRepository();
        public static readonly IAsyncRepository<UserSetting> UserSettingsRepository
        = new UserSettingJsonRepository();
    }
}