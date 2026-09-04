using System;
using System.IO;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal static class TestAssetLoader
    {
        public static string LoadAsset(string name)
        {
            var baseDir = AppContext.BaseDirectory;
            var path = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "TestAssets", name));
            return File.ReadAllText(path);
        }
    }
}
