#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Lib;

namespace Rhycol.OpenApiCodeGen.Editor.Generation
{
    /// <summary>
    /// Adapts the existing Docker/OpenAPI Generator implementation to the
    /// generation provider contract.
    /// </summary>
    internal sealed class DockerGenerationProvider : IGenerationProvider
    {
        readonly IGenerator _generator;
        readonly Func<Task<GenerationCSharpSetting?>> _readCSharpSettingAsync;
        readonly Func<Task<UserSetting?>> _readUserSettingAsync;

        public GenerationProviderDescriptor Descriptor { get; } =
            new GenerationProviderDescriptor(
                GenerateProvider.OpenApi,
                "OpenAPI Generator (Docker)",
                GenerationProviderAvailability.Available());

        public DockerGenerationProvider()
            : this(
                new OpenApiCodeGenerator(),
                () => ApplicationConfig.GenerationCsharpSettingRepository.ReadAsync(),
                () => ApplicationConfig.UserSettingsRepository.ReadAsync())
        {
        }

        internal DockerGenerationProvider(
            IGenerator generator,
            IAsyncRepository<GenerationCSharpSetting> cSharpSettingRepository,
            IAsyncRepository<UserSetting> userSettingRepository)
            : this(
                generator,
                CreateReader(cSharpSettingRepository, nameof(cSharpSettingRepository)),
                CreateReader(userSettingRepository, nameof(userSettingRepository)))
        {
        }

        DockerGenerationProvider(
            IGenerator generator,
            Func<Task<GenerationCSharpSetting?>> readCSharpSettingAsync,
            Func<Task<UserSetting?>> readUserSettingAsync)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _readCSharpSettingAsync = readCSharpSettingAsync
                ?? throw new ArgumentNullException(nameof(readCSharpSettingAsync));
            _readUserSettingAsync = readUserSettingAsync
                ?? throw new ArgumentNullException(nameof(readUserSettingAsync));
        }

        static Func<Task<T?>> CreateReader<T>(
            IAsyncRepository<T>? repository,
            string parameterName) where T : class
        {
            if (repository == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return repository.ReadAsync;
        }

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            GenerationCSharpSetting cSharpSetting =
                _readCSharpSettingAsync().GetAwaiter().GetResult()
                ?? new GenerationCSharpSetting();
            UserSetting userSetting =
                _readUserSettingAsync().GetAwaiter().GetResult()
                ?? new UserSetting(dockerPath: string.Empty);

            ProcessResponse response = _generator.Generate(
                CreateProjectSetting(request),
                cSharpSetting,
                userSetting);
            return ToGenerationResult(response);
        }

        public async Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();
            GenerationCSharpSetting cSharpSetting =
                await _readCSharpSettingAsync() ?? new GenerationCSharpSetting();
            cancellationToken.ThrowIfCancellationRequested();
            UserSetting userSetting =
                await _readUserSettingAsync()
                ?? new UserSetting(dockerPath: string.Empty);

            ProcessResponse response = await _generator.GenerateAsync(
                CreateProjectSetting(request),
                cSharpSetting,
                userSetting,
                cancellationToken);
            return ToGenerationResult(response);
        }

        static ProjectSetting CreateProjectSetting(GenerationRequest request)
        {
            return new ProjectSetting(
                generateProvider: GenerateProvider.OpenApi,
                apiDocumentFilePathOrUrl: request.ApiDocumentFilePathOrUrl,
                apiClientOutputFolderPath: request.OutputFolderPath);
        }

        static GenerationResult ToGenerationResult(ProcessResponse response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            if (response.Status == ExitStatus.Success)
            {
                return GenerationResult.Success(response.Message);
            }

            return GenerationResult.Failure(
                string.IsNullOrWhiteSpace(response.Message)
                    ? "Docker/OpenAPI Generator exited with an error."
                    : response.Message);
        }
    }
}
