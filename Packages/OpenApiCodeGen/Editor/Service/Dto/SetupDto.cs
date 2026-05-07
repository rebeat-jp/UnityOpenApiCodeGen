
namespace Rhycol.OpenApiCodeGen.Core
{
    internal class SetupDto
    {
        public string DockerPath { get; }
        public GenerateProvider ProviderType { get; }

        public SetupDto()
        {
            DockerPath = string.Empty;
            ProviderType = GenerateProvider.OpenApi;
        }

        public SetupDto(string dockerPath, GenerateProvider providerType)
        {
            DockerPath = dockerPath;
            ProviderType = providerType;
        }
    }
}