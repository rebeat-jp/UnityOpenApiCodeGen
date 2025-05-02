using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.Dto
{
    internal class SetupMenuDto
    {
        public GenerateProvider GenerateProvider { get; private set; }
        public string DockerPath { get; private set; }

        public SetupMenuDto(GenerateProvider generateProvider, string dockerPath)
        {
            GenerateProvider = generateProvider;
            DockerPath = dockerPath;
        }
    }
}