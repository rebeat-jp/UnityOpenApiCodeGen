namespace Rhycol.OpenApiCodeGen.Core
{
    internal class UserSetting
    {
        public string DockerPath { get; private set; }

        public UserSetting(string dockerPath)
        {
            this.DockerPath = dockerPath;
        }
    }
}
