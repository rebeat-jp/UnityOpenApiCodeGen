#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.UI;

namespace Rhycol.OpenApiCodeGen.Core
{
    internal class GenerationService
    {
        readonly IAsyncRepository<ProjectSetting> _projectSettingRepository;
        readonly IAsyncRepository<GenerationCSharpSetting> _generationCSharpSettingRepository;
        readonly IAsyncRepository<UserSetting> _userSettingRepository;
        readonly GenerationProviderRegistry _providerRegistry;

        public GenerationService()
            : this(
                ApplicationConfig.ProjectSettingRepository,
                ApplicationConfig.GenerationCsharpSettingRepository,
                GenerationProviderRegistry.Shared)
        {
        }

        internal GenerationService(
            IAsyncRepository<ProjectSetting> projectSettingRepository,
            GenerationProviderRegistry providerRegistry)
            : this(
                projectSettingRepository,
                ApplicationConfig.GenerationCsharpSettingRepository,
                providerRegistry)
        {
        }

        internal GenerationService(
            IAsyncRepository<ProjectSetting> projectSettingRepository,
            IAsyncRepository<GenerationCSharpSetting> generationCSharpSettingRepository,
            GenerationProviderRegistry providerRegistry)
            : this(
                projectSettingRepository,
                generationCSharpSettingRepository,
                ApplicationConfig.UserSettingsRepository,
                providerRegistry)
        {
        }

        internal GenerationService(
            IAsyncRepository<ProjectSetting> projectSettingRepository,
            IAsyncRepository<GenerationCSharpSetting> generationCSharpSettingRepository,
            IAsyncRepository<UserSetting> userSettingRepository,
            GenerationProviderRegistry providerRegistry)
        {
            _projectSettingRepository = projectSettingRepository
                ?? throw new ArgumentNullException(nameof(projectSettingRepository));
            _generationCSharpSettingRepository = generationCSharpSettingRepository
                ?? throw new ArgumentNullException(nameof(generationCSharpSettingRepository));
            _userSettingRepository = userSettingRepository
                ?? throw new ArgumentNullException(nameof(userSettingRepository));
            _providerRegistry = providerRegistry
                ?? throw new ArgumentNullException(nameof(providerRegistry));
        }

        public async Task<GenerateApiClientDto> GetDefaultGenerateApiClientDtoAsync()
        {
            try
            {
                var projectSetting = await _projectSettingRepository.ReadAsync();

                return projectSetting != null
                    ? new GenerateApiClientDto(
                        generateProvider: projectSetting.GenerateProvider,
                        apiDocumentFilePathOrUrl: projectSetting.ApiDocumentFilePathOrUrl,
                        apiClientOutputFolderPath: projectSetting.ApiClientOutputFolderPath,
                        httpTransport: projectSetting.HttpTransport,
                        jsonSerializer: projectSetting.JsonSerializer)
                    : new GenerateApiClientDto();
            }
            catch (Exception e)
            {
                throw new ApplicationServiceException("デフォルトのAPIクライアント生成設定の取得に失敗しました。", e);
            }
        }

        /// <summary>
        /// 選択中の組み合わせに対するライブラリの準備状況を返します。Docker など対象外の Provider は NotApplicable です。
        /// Evaluates the libraries of the selected combination; providers without library checks are not applicable.
        /// </summary>
        internal GenerationLibraryReadinessDto GetLibraryReadiness(
            GenerateProvider provider,
            string outputFolderPath,
            GenerationHttpTransport httpTransport,
            GenerationJsonSerializer jsonSerializer)
        {
            try
            {
                GenerationLibraryAssessment assessment = GenerationLibraryAssessment.Assess(
                    _providerRegistry,
                    provider,
                    outputFolderPath,
                    httpTransport,
                    jsonSerializer);
                return SourceGeneratorLibraryPresentation.BuildReadiness(
                    assessment,
                    SourceGeneratorLibraryPresentation.IsWebGlBuildTarget());
            }
            catch (Exception)
            {
                // The readiness line is informational; the provider re-checks when generating.
                return GenerationLibraryReadinessDto.NotApplicable;
            }
        }

        public async Task<GenerationResult> GenerateApiClientAsync(
            GenerateApiClientDto generateApiClientDto,
            CancellationToken cancellationToken = default)
        {
            return await GenerateApiClientAsync(generateApiClientDto, cancellationToken, null);
        }

        internal async Task<GenerationResult> GenerateApiClientAsync(
            GenerateApiClientDto generateApiClientDto,
            CancellationToken cancellationToken,
            IProgress<GenerationProgress>? progress)
        {
            try
            {
                ProjectSetting savedProjectSetting =
                    await _projectSettingRepository.ReadAsync() ?? new ProjectSetting();
                GenerationProviderResolution resolution =
                    _providerRegistry.Resolve(savedProjectSetting.GenerateProvider);

                if (!resolution.IsResolved || resolution.Provider == null)
                {
                    throw new ApplicationServiceException(
                        $"生成Providerを解決できませんでした。{Environment.NewLine}"
                        + resolution.FailureReason);
                }

                IGenerationProvider provider = resolution.Provider;
                GenerationProviderAvailability availability =
                    provider.Descriptor.Availability;
                if (!availability.IsAvailable)
                {
                    throw new ApplicationServiceException(
                        $"生成Provider '{provider.Descriptor.DisplayName}' は利用できません。"
                        + $"{Environment.NewLine}{availability.Reason}");
                }

                GenerationCSharpSetting generationCSharpSetting =
                    await _generationCSharpSettingRepository.ReadAsync()
                    ?? new GenerationCSharpSetting();
                UserSetting? userSetting = await _userSettingRepository.ReadAsync();
                cancellationToken.ThrowIfCancellationRequested();
                var request = new GenerationRequest(
                    generateApiClientDto.ApiDocumentFilePathOrUrl,
                    Path.GetFullPath(generateApiClientDto.ApiClientOutputFolderPath),
                    generationCSharpSetting.ApiName,
                    generationCSharpSetting.PackageName);
                request.HttpTransport = savedProjectSetting.HttpTransport;
                request.JsonSerializer = savedProjectSetting.JsonSerializer;
                request.Progress = progress;
                request.GitHubToken = userSetting?.GitHubToken;
                GenerationResult result =
                    await provider.GenerateAsync(request, cancellationToken);

                if (!result.IsSuccess)
                {
                    throw new ApplicationServiceException(
                        $"APIクライアント生成に失敗しました。Provider: "
                        + $"{provider.Descriptor.DisplayName}{Environment.NewLine}"
                        + result.Message);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) when (e is not ApplicationServiceException)
            {
                throw new ApplicationServiceException("APIクライアント生成ユースケースに失敗しました。", e);
            }
        }
    }
}
