using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Callbacks;

public static class CsProjConfig
{
    [DidReloadScripts]
    private static void OnScriptsReloaded()
    {
        string[] csprojFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");

        foreach (string file in csprojFiles)
        {
            string content = File.ReadAllText(file);

            // Check if the nullable setting is already present
            if (!content.Contains("<Nullable>enable</Nullable>"))
            {
                // Insert the nullable setting
                content = Regex.Replace(content, "<PropertyGroup>", "<PropertyGroup>\n    <Nullable>enable</Nullable>", RegexOptions.Multiline);
                File.WriteAllText(file, content);
            }
        }
    }
}
