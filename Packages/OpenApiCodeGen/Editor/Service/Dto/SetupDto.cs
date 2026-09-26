namespace Rhycol.OpenApiCodeGen.Core
{
    internal class SetupDto
    {
        public string DockerPath { get; }

        public SetupDto()
        {
            DockerPath = string.Empty;
        }

        public SetupDto(string dockerPath)
        {
            DockerPath = dockerPath;
        }
    }
}
