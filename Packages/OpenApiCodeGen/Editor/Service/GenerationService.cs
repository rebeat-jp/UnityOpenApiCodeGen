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
        {
            _projectSettingRepository = projectSettingRepository
                ?? throw new ArgumentNullException(nameof(projectSettingRepository));
            _generationCSharpSettingRepository = generationCSharpSettingRepository
                ?? throw new ArgumentNullException(nameof(generationCSharpSettingRepository));
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
                        apiClientOutputFolderPath: projectSetting.ApiClientOutputFolderPath)
                    : new GenerateApiClientDto();
            }
            catch (Exception e)
            {
                throw new ApplicationServiceException("デフォルトのAPIクライアント生成設定の取得に失敗しました。", e);
            }
        }

        public async Task GenerateApiClientAsync(
            GenerateApiClientDto generateApiClientDto,
            CancellationToken cancellationToken = default)
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
                cancellationToken.ThrowIfCancellationRequested();
                var request = new GenerationRequest(
                    generateApiClientDto.ApiDocumentFilePathOrUrl,
                    Path.GetFullPath(generateApiClientDto.ApiClientOutputFolderPath),
                    generationCSharpSetting.ApiName,
                    generationCSharpSetting.PackageName);
                GenerationResult result =
                    await provider.GenerateAsync(request, cancellationToken);

                if (!result.IsSuccess)
                {
                    throw new ApplicationServiceException(
                        $"APIクライアント生成に失敗しました。Provider: "
                        + $"{provider.Descriptor.DisplayName}{Environment.NewLine}"
                        + result.Message);
                }
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
