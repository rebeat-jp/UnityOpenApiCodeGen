using ReBeat.OpenApiCodeGen.Lib;

namespace ReBeat.OpenApiCodeGen.Core
{
    internal class SettingSchema
    {
        public GeneralConfigSchema GeneralSettings { get; private set; }
        public OpenApiCsharpOption OpenApiCsharpOption { get; private set; }

        public SettingSchema(GeneralConfigSchema generalSettings, OpenApiCsharpOption openApiCsharpOption)
        {
            this.GeneralSettings = generalSettings;
            this.OpenApiCsharpOption = openApiCsharpOption;
        }

    }
}