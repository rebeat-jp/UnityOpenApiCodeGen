#nullable enable
using System;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;


namespace ReBeat.OpenApiCodeGen.Presenter
{
    public class MenuPresenter : IGenerablePresenter
    {
        readonly JsonRepository<GeneralConfigSchema> _generalConfigSchemaRepository;

        public MenuPresenter()
        {
            _generalConfigSchemaRepository = new();
        }

        public GeneralConfigSchema GetGenerateConfig()
        {
            return _generalConfigSchemaRepository.Read(GeneralConfigSchema.ConfigFilePath)
            ?? _generalConfigSchemaRepository.Save(GeneralConfigSchema.ConfigFilePath, new GeneralConfigSchema());

        }

        public ProcessResponse Generate(string documentFilePath, string outputFolderPath)
        {
            var generalConfig = _generalConfigSchemaRepository.Read(GeneralConfigSchema.ConfigFilePath)
            ?? _generalConfigSchemaRepository.Save(GeneralConfigSchema.ConfigFilePath, new GeneralConfigSchema());
            var generable = new OpenApiCodeGenerator(generalConfig);

            var currentDirectory = Environment.CurrentDirectory;


            var outputPath = !string.IsNullOrEmpty(outputFolderPath) ? outputFolderPath : $"{currentDirectory}/Packages/SwaggerCodeGen/Runtime/Generate/";

            var response = generable.Generate(documentFilePath, outputPath);
            return response;
        }

        public GeneralConfigSchema Save(string apiDocumentFilePathOrUrl, string apiClientOutputFolderPath)
        {
            var generalSettings = _generalConfigSchemaRepository.Read(GeneralConfigSchema.ConfigFilePath);
            if (generalSettings is null)
            {
                var defaultSaveResult = _generalConfigSchemaRepository.Save(GeneralConfigSchema.ConfigFilePath,
                new GeneralConfigSchema(GenerateProvider.OpenApi, "", apiDocumentFilePathOrUrl, apiClientOutputFolderPath)
                );

                return defaultSaveResult;
            }

            var result = _generalConfigSchemaRepository.Save(GeneralConfigSchema.ConfigFilePath,
            new GeneralConfigSchema(generalSettings.GenerateProvider, generalSettings.JavaPath, apiDocumentFilePathOrUrl, apiClientOutputFolderPath)
            );

            return result;

        }
    }
}
