#nullable enable

using System.IO;

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;

using UnityEngine;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    class SettingPresenter : ISettingPresenter
    {
        readonly JsonRepository<GeneralConfigSchema> _generalSettingRepository;
        readonly JsonRepository<OpenApiCsharpOption> _openApiSettingRepository;
        public SettingPresenter()
        {
            _generalSettingRepository ??= new(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Assets",
                    "OpenApiCodeGen",
                    "general.json")
                );
            _openApiSettingRepository ??=
            new(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Assets",
                    "OpenApiCodeGen",
                    "openapi.json")
                    );
        }

        public SettingSchema LoadSetting()
        {
            var generalSettings = _generalSettingRepository.Read() ?? new();

            var openApiSettings = _openApiSettingRepository.Read() ?? new();

            return new(generalSettings, openApiSettings);
        }

        public ProcessResponse RunJavaTest(string javaPath)
        {
            var javaTester = new JavaProcess();
            return javaTester.Send("--version");
        }


        public GeneralConfigSchema SaveGeneral(GenerateProvider generateProvider, string javaPath, string apiDocumentFilePathOrUrl, string apiClientOutputFolder)
        {
            var saveValue = new GeneralConfigSchema(
                generateProvider: generateProvider,
                apiClientOutputFolderPath: apiClientOutputFolder,
                apiDocumentFilePathOrUrl: apiDocumentFilePathOrUrl,
                cacheFolderPath: ""
                );

            var result =
            _generalSettingRepository.Save(
                saveValue
            );
            return result;
        }

        public OpenApiCsharpOption SaveOpenApi(
            string packageName,
            string targetFramework,
            bool includeOptionalAssemblyInfo,
            bool includeOptionalProjectFile,
            bool includeNullableReferenceTypes,
            string dependenceLibrary,
            bool isValidatable
            )
        {
            var openApiSettings = new OpenApiCsharpOption
            {
                PackageName = packageName,
                TargetFramework = targetFramework,
                Library = dependenceLibrary,
                Validatable = isValidatable,
                NullableReferenceTypes = includeNullableReferenceTypes,
                OptionalAssemblyInfo = includeOptionalAssemblyInfo,
                OptionalProjectFile = includeOptionalProjectFile,
            };

            var result = _openApiSettingRepository.Save(
              openApiSettings
                );
            return result;
        }

        public SettingSchema SaveSettings(SettingSchema settingSchema)
        {
            var generalSettingSaveResult = _generalSettingRepository.Save(settingSchema.GeneralSettings);
            var openApiCsharpOptionSaveResult = _openApiSettingRepository.Save(settingSchema.OpenApiCsharpOption);

            return settingSchema;
        }
    }
}