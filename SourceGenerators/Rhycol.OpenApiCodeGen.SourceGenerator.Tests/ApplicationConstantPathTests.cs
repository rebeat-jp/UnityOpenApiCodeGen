using System;
using System.IO;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class ApplicationConstantPathTests
    {
        [Fact]
        public void ProjectFolderUsesUnityDataPathInsteadOfProcessWorkingDirectory()
        {
            string stubAssetsPath = UnityEngine.Application.dataPath;

            Assert.Equal(
                Path.Combine(stubAssetsPath, "OpenApiCodeGen"),
                Rhycol.OpenApiCodeGen.ApplicationConstant.PROJECT_FOLDER_PATH);
            Assert.NotEqual(
                Path.Combine(Environment.CurrentDirectory, "Assets", "OpenApiCodeGen"),
                Rhycol.OpenApiCodeGen.ApplicationConstant.PROJECT_FOLDER_PATH);
        }
    }
}

namespace UnityEngine
{
    internal static class Application
    {
        internal static string dataPath { get; } = Path.Combine(
            Path.GetTempPath(),
            "OpenApiCodeGen-Unity-DataPath-" + Guid.NewGuid().ToString("N"),
            "Assets");
    }
}
