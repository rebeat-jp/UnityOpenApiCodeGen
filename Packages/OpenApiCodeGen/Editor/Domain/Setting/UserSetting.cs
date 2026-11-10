namespace Rhycol.OpenApiCodeGen.Core
{
    internal class UserSetting
    {
        public string DockerPath { get; private set; }

        /// <summary>
        /// User-local GitHub token used only for allowed GitHub origins. Stored in the
        /// user settings folder (outside the Unity project) and never in project settings.
        /// </summary>
        public string GitHubToken { get; private set; }

        public UserSetting(string dockerPath, string gitHubToken = "")
        {
            this.DockerPath = dockerPath;
            this.GitHubToken = gitHubToken ?? "";
        }
    }
}
