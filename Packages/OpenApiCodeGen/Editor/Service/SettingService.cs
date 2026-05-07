#nullable enable
using System;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.UI;


namespace Rhycol.OpenApiCodeGen.Core
{
    internal class SettingService
    {
        /// <summary>
        /// プロジェクト設定を取得します。
        /// </summary>
        /// <returns>プロジェクト設定のインスタンス。</returns>
        public async Task<ProjectSettingDisplayDto?> GetProjectSettingAsync()
        {
            try
            {
                var projectSetting = await ApplicationConfig.ProjectSettingRepository.ReadAsync();
                return projectSetting != null ? ToDisplayDto(projectSetting) : null;
            }
            catch (Exception e)
            {
                throw new ApplicationServiceException("プロジェクト設定の取得に失敗しました。", e);
            }
        }

        /// <summary>
        /// C#コード生成時の設定を取得します。
        /// </summary>
        /// <returns>C#コード生成時の設定のインスタンス。</returns>
        public async Task<GenerationCSharpSettingDisplayDto?> GetGenerationCSharpSettingAsync()
        {
            try
            {
                var generationCSharpSetting = await ApplicationConfig.GenerationCsharpSettingRepository.ReadAsync();
                return generationCSharpSetting != null ? ToDisplayDto(generationCSharpSetting) : null;
            }
            catch (Exception e)
            {
                throw new ApplicationServiceException("C#コード生成設定の取得に失敗しました。", e);
            }
        }

        /// <summary>
        /// ユーザー設定を取得します。
        /// </summary>
        /// <returns>ユーザー設定のインスタンス。</returns>
        public async Task<UserSettingDisplayDto?> GetUserSettingAsync()
        {
            try
            {
                var userSetting = await ApplicationConfig.UserSettingsRepository.ReadAsync();
                return userSetting != null ? ToDisplayDto(userSetting) : null;
            }
            catch (Exception e)
            {
                throw new ApplicationServiceException("ユーザー設定の取得に失敗しました。", e);
            }
        }

        /// <summary>
        /// プロジェクト設定を保存します。
        /// </summary>
        /// <param name="projectSetting">保存するプロジェクト設定のインスタンス。</param>
        /// <returns></returns>
        public async Task SaveProjectSettingAsync(ProjectSettingDisplayDto projectSetting)
        {
            try
            {
                await ApplicationConfig.ProjectSettingRepository.SaveAsync(ToDomain(projectSetting));
            }
            catch (Exception e) when (!(e is ApplicationServiceException))
            {
                throw new ApplicationServiceException("プロジェクト設定の保存ユースケースに失敗しました。", e);
            }
        }

        /// <summary>
        /// C#コード生成時の設定を保存します。
        /// </summary>
        /// <param name="generationCSharpSetting">保存するC#コード生成時の設定のインスタンス。</param>
        /// <returns></returns>
        public async Task SaveGenerationCSharpSettingAsync(GenerationCSharpSettingDisplayDto generationCSharpSetting)
        {
            try
            {
                await ApplicationConfig.GenerationCsharpSettingRepository.SaveAsync(ToDomain(generationCSharpSetting));
            }
            catch (Exception e) when (!(e is ApplicationServiceException))
            {
                throw new ApplicationServiceException("C#コード生成設定の保存ユースケースに失敗しました。", e);
            }
        }

        /// <summary>
        /// ユーザー設定を保存します。
        /// </summary>
        /// <param name="userSetting">保存するユーザー設定のインスタンス。</param>
        /// <returns></returns>
        public async Task SaveUserSettingAsync(UserSettingDisplayDto userSetting)
        {
            try
            {
                await ApplicationConfig.UserSettingsRepository.SaveAsync(ToDomain(userSetting));
            }
            catch (Exception e) when (!(e is ApplicationServiceException))
            {
                throw new ApplicationServiceException("ユーザー設定の保存ユースケースに失敗しました。", e);
            }
        }

        static ProjectSettingDisplayDto ToDisplayDto(ProjectSetting projectSetting)
        {
            return new ProjectSettingDisplayDto(
                generateProvider: projectSetting.GenerateProvider,
                apiDocumentFilePathOrUrl: projectSetting.ApiDocumentFilePathOrUrl,
                apiClientOutputFolderPath: projectSetting.ApiClientOutputFolderPath
            );
        }

        static ProjectSetting ToDomain(ProjectSettingDisplayDto projectSetting)
        {
            return new ProjectSetting(
                generateProvider: projectSetting.GenerateProvider,
                apiDocumentFilePathOrUrl: projectSetting.ApiDocumentFilePathOrUrl,
                apiClientOutputFolderPath: projectSetting.ApiClientOutputFolderPath
            );
        }

        static GenerationCSharpSettingDisplayDto ToDisplayDto(GenerationCSharpSetting generationCSharpSetting)
        {
            return new GenerationCSharpSettingDisplayDto
            {
                AllowUnicodeIdentifiers = generationCSharpSetting.AllowUnicodeIdentifiers,
                ApiName = generationCSharpSetting.ApiName,
                CaseInsensitiveResponseHeaders = generationCSharpSetting.CaseInsensitiveResponseHeaders,
                ConditionalSerialization = generationCSharpSetting.ConditionalSerialization,
                DisallowAdditionalPropertiesIfNotPresent = generationCSharpSetting.DisallowAdditionalPropertiesIfNotPresent,
                Equatable = generationCSharpSetting.Equatable,
                HideGenerationTimestamp = generationCSharpSetting.HideGenerationTimestamp,
                InterfacePrefix = generationCSharpSetting.InterfacePrefix,
                Library = generationCSharpSetting.Library,
                LicenseId = generationCSharpSetting.LicenseId,
                ModelPropertyNaming = generationCSharpSetting.ModelPropertyNaming,
                NetCoreProjectFile = generationCSharpSetting.NetCoreProjectFile,
                NonPublicApi = generationCSharpSetting.NonPublicApi,
                NullableReferenceTypes = generationCSharpSetting.NullableReferenceTypes,
                OptionalEmitDefaultValues = generationCSharpSetting.OptionalEmitDefaultValues,
                OptionalMethodArgument = generationCSharpSetting.OptionalMethodArgument,
                OptionalAssemblyInfo = generationCSharpSetting.OptionalAssemblyInfo,
                OptionalProjectFile = generationCSharpSetting.OptionalProjectFile,
                PackageName = generationCSharpSetting.PackageName,
                ReturnICollection = generationCSharpSetting.ReturnICollection,
                TargetFramework = generationCSharpSetting.TargetFramework,
                UseCollection = generationCSharpSetting.UseCollection,
                UseOneOfDiscriminatorLookup = generationCSharpSetting.UseOneOfDiscriminatorLookup,
                Validatable = generationCSharpSetting.Validatable
            };
        }

        static GenerationCSharpSetting ToDomain(GenerationCSharpSettingDisplayDto generationCSharpSetting)
        {
            return new GenerationCSharpSetting
            {
                AllowUnicodeIdentifiers = generationCSharpSetting.AllowUnicodeIdentifiers,
                ApiName = generationCSharpSetting.ApiName,
                CaseInsensitiveResponseHeaders = generationCSharpSetting.CaseInsensitiveResponseHeaders,
                ConditionalSerialization = generationCSharpSetting.ConditionalSerialization,
                DisallowAdditionalPropertiesIfNotPresent = generationCSharpSetting.DisallowAdditionalPropertiesIfNotPresent,
                Equatable = generationCSharpSetting.Equatable,
                HideGenerationTimestamp = generationCSharpSetting.HideGenerationTimestamp,
                InterfacePrefix = generationCSharpSetting.InterfacePrefix,
                Library = generationCSharpSetting.Library,
                LicenseId = generationCSharpSetting.LicenseId,
                ModelPropertyNaming = generationCSharpSetting.ModelPropertyNaming,
                NetCoreProjectFile = generationCSharpSetting.NetCoreProjectFile,
                NonPublicApi = generationCSharpSetting.NonPublicApi,
                NullableReferenceTypes = generationCSharpSetting.NullableReferenceTypes,
                OptionalEmitDefaultValues = generationCSharpSetting.OptionalEmitDefaultValues,
                OptionalMethodArgument = generationCSharpSetting.OptionalMethodArgument,
                OptionalAssemblyInfo = generationCSharpSetting.OptionalAssemblyInfo,
                OptionalProjectFile = generationCSharpSetting.OptionalProjectFile,
                PackageName = generationCSharpSetting.PackageName,
                ReturnICollection = generationCSharpSetting.ReturnICollection,
                TargetFramework = generationCSharpSetting.TargetFramework,
                UseCollection = generationCSharpSetting.UseCollection,
                UseOneOfDiscriminatorLookup = generationCSharpSetting.UseOneOfDiscriminatorLookup,
                Validatable = generationCSharpSetting.Validatable
            };
        }

        static UserSettingDisplayDto ToDisplayDto(UserSetting userSetting)
        {
            return new UserSettingDisplayDto(
                dockerPath: userSetting.DockerPath
            );
        }

        static UserSetting ToDomain(UserSettingDisplayDto userSetting)
        {
            return new UserSetting(
                dockerPath: userSetting.DockerPath
            );
        }
    }
}
