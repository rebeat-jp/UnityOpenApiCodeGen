#nullable enable

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    public class SetupPresenter : ISetupPresenter
    {


        public void OnEnable()
        {
        }


        public ProcessResponse RunJavaTest(string javaPath)
        {
            var javaRuntimeTester = new JavaRuntimeTester();

            return javaRuntimeTester.Test(javaPath);
        }

        public void SetUp(string javaPath, GenerateProvider generateProvider)
        {
            var generalSettingsRepository = new JsonRepository<GeneralConfigSchema>();
            var openApiSettingsRepository = new JsonRepository<OpenApiConfigSchema>();
            generalSettingsRepository.Save(
                GeneralConfigSchema.ConfigFilePath,
                new GeneralConfigSchema(generateProvider, javaPath)
                );

            openApiSettingsRepository.Save(OpenApiConfigSchema.ConfigFilePath, new OpenApiConfigSchema());
        }
    }
}
