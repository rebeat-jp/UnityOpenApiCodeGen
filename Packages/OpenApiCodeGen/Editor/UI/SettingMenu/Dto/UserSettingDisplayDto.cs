namespace ReBeat.OpenApiCodeGen.UI
{
    internal class UserSettingDisplayDto
    {
        public string DockerPath { get; private set; }

        public UserSettingDisplayDto(string dockerPath)
        {
            this.DockerPath = dockerPath;
        }
    }
}