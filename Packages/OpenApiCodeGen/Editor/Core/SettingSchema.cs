using ReBeat.OpenApiCodeGen.Lib;

namespace ReBeat.OpenApiCodeGen.Core
{
    public class SettingSchema
    {
        public GeneralConfigSchema GeneralSettings { get; private set; }
        public OpenApiConfigSchema OpenApiSettings { get; private set; }

        public SettingSchema(GeneralConfigSchema generalSettings, OpenApiConfigSchema openApiSettings)
        {
            this.GeneralSettings = generalSettings;
            this.OpenApiSettings = openApiSettings;
        }

    }
}