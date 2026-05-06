namespace ReBeat.OpenApiCodeGen.Core
{
    internal class CheckDockerInstalledDto
    {
        public string DockerPath { get; private set; }

        public CheckDockerInstalledDto(string dockerPath)
        {
            this.DockerPath = dockerPath;
        }
    }

}