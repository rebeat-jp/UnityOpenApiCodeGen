namespace Rhycol.OpenApiCodeGen.UI
{
    internal class UserSettingDisplayDto
    {
        public string DockerPath { get; private set; }
        public string GitHubToken { get; private set; }

        public UserSettingDisplayDto(string dockerPath, string gitHubToken = "")
        {
            this.DockerPath = dockerPath;
            this.GitHubToken = gitHubToken ?? "";
        }
    }
}