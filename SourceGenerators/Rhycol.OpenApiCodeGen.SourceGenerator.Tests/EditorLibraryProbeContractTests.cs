using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Unity パッケージの Editor 側 SourceGeneratorLibraryProbe は、アナライザの LibraryAvailability と同じ
    /// マーカー型・最低版で判定しなければならない。Editor はアナライザ DLL を参照できないため、定数をソースから読んで比較する。
    /// The Unity package's Editor-side SourceGeneratorLibraryProbe must judge with the same marker types and
    /// minimum versions as the analyzer's LibraryAvailability. The Editor cannot reference the analyzer DLL, so
    /// this reads the probe's constants from its source and compares them.
    /// </summary>
    public sealed class EditorLibraryProbeContractTests
    {
        private const string ProbeRelativePath =
            "Packages/OpenApiCodeGen.SourceGenerator/Editor/SourceGeneratorLibraryProbe.cs";

        [Fact]
        public void ProbeMarkerTypesMatchTheAnalyzer()
        {
            Dictionary<string, string> probe = ReadProbeConstants();

            Assert.Equal(LibraryAvailability.NewtonsoftJsonMarkerType, probe["NewtonsoftJsonMarkerType"]);
            Assert.Equal(LibraryAvailability.SystemTextJsonMarkerType, probe["SystemTextJsonMarkerType"]);
            Assert.Equal(LibraryAvailability.UnityWebRequestMarkerType, probe["UnityWebRequestMarkerType"]);
            Assert.Equal(LibraryAvailability.HttpClientMarkerType, probe["HttpClientMarkerType"]);
        }

        [Fact]
        public void ProbeMinimumVersionsMatchTheAnalyzer()
        {
            Dictionary<string, string> probe = ReadProbeConstants();

            Assert.Equal(
                FormatMajorMinor(LibraryAvailability.MinimumNewtonsoftJsonVersion),
                probe["NewtonsoftJsonMinimumVersion"]);
            Assert.Equal(
                FormatMajorMinor(LibraryAvailability.MinimumSystemTextJsonVersion),
                probe["SystemTextJsonMinimumVersion"]);
        }

        [Fact]
        public void ProbeAssemblyFileNamesAreTheAssembliesThatDefineTheMarkerTypes()
        {
            Dictionary<string, string> probe = ReadProbeConstants();

            Assert.Equal(
                typeof(Newtonsoft.Json.JsonConvert).Assembly.GetName().Name + ".dll",
                probe["NewtonsoftJsonAssemblyFileName"]);
            Assert.Equal(
                typeof(System.Text.Json.JsonSerializer).Assembly.GetName().Name + ".dll",
                probe["SystemTextJsonAssemblyFileName"]);
            Assert.Equal(
                typeof(System.Text.Encodings.Web.JavaScriptEncoder).Assembly.GetName().Name + ".dll",
                probe["SystemTextEncodingsWebAssemblyFileName"]);
            Assert.Equal(
                typeof(System.Net.Http.HttpClient).Assembly.GetName().Name + ".dll",
                probe["HttpClientAssemblyFileName"]);
            // UnityWebRequest lives in Unity's engine module assembly named after the namespace's module.
            Assert.Equal("UnityEngine.UnityWebRequestModule.dll", probe["UnityWebRequestAssemblyFileName"]);
        }

        [Fact]
        public void EditorSelectionEnumsMatchTheAnalyzerKinds()
        {
            // The analyzer reads the attribute's named arguments as numbers, so the Runtime enums, the Editor
            // contract enums and the analyzer kinds must keep the same numeric values.
            Assert.Equal(0, (int)HttpTransportKind.HttpClient);
            Assert.Equal(1, (int)HttpTransportKind.UnityWebRequest);
            Assert.Equal(0, (int)JsonSerializerKind.NewtonsoftJson);
            Assert.Equal(1, (int)JsonSerializerKind.SystemTextJson);
        }

        private static string FormatMajorMinor(Version version)
        {
            return version.Major + "." + version.Minor;
        }

        private static Dictionary<string, string> ReadProbeConstants()
        {
            string path = FindProbeSource();
            var constants = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(
                         File.ReadAllText(path),
                         "internal const string (\\w+) =\\s*\"([^\"]*)\";"))
            {
                constants[match.Groups[1].Value] = match.Groups[2].Value;
            }

            return constants;
        }

        private static string FindProbeSource()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, ProbeRelativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Run the tests inside the repository checkout; " + ProbeRelativePath + " was not found above " +
                AppContext.BaseDirectory + ".");
        }
    }
}
