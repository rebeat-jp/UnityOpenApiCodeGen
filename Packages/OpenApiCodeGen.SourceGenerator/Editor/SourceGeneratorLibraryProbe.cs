#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using UnityEditor.Compilation;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    /// <summary>One player assembly and the precompiled and engine DLL paths it is compiled against.</summary>
    internal sealed class PlayerAssemblyInfo
    {
        internal PlayerAssemblyInfo(string name, IReadOnlyList<string> compiledReferencePaths)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            CompiledReferencePaths = compiledReferencePaths
                ?? throw new ArgumentNullException(nameof(compiledReferencePaths));
        }

        internal string Name { get; }

        internal IReadOnlyList<string> CompiledReferencePaths { get; }
    }

    /// <summary>
    /// The Unity facts the library probe depends on. Replaceable so the decision logic can be tested without a
    /// compiled project.
    /// </summary>
    internal interface ISourceGeneratorLibraryEnvironment
    {
        IReadOnlyList<PlayerAssemblyInfo> GetPlayerAssemblies();

        /// <summary>The assembly version of a managed DLL, or null when it cannot be read.</summary>
        Version? GetAssemblyVersion(string assemblyPath);
    }

    internal sealed class CompilationPipelineSourceGeneratorLibraryEnvironment
        : ISourceGeneratorLibraryEnvironment
    {
        public IReadOnlyList<PlayerAssemblyInfo> GetPlayerAssemblies()
        {
            UnityEditor.Compilation.Assembly[] assemblies = CompilationPipeline.GetAssemblies(
                AssembliesType.PlayerWithoutTestAssemblies);
            var result = new List<PlayerAssemblyInfo>(assemblies.Length);
            foreach (UnityEditor.Compilation.Assembly assembly in assemblies)
            {
                result.Add(new PlayerAssemblyInfo(
                    assembly.name,
                    assembly.compiledAssemblyReferences ?? Array.Empty<string>()));
            }

            return result;
        }

        public Version? GetAssemblyVersion(string assemblyPath)
        {
            try
            {
                return AssemblyName.GetAssemblyName(assemblyPath).Version;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is BadImageFormatException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Decides, before generation, whether the output assembly can compile the libraries a generated client uses.
    /// This is a UX aid; the Roslyn generator re-checks the same conditions (OACG011-OACG013) and is final.
    /// The assembly file names and minimum versions below must match <c>LibraryAvailability</c> in the analyzer;
    /// a test in the analyzer test project compares them.
    /// </summary>
    internal sealed class SourceGeneratorLibraryProbe
    {
        // Marker types of the analyzer's LibraryAvailability and the assemblies that define them.
        internal const string NewtonsoftJsonMarkerType = "Newtonsoft.Json.JsonConvert";
        internal const string SystemTextJsonMarkerType = "System.Text.Json.JsonSerializer";
        internal const string UnityWebRequestMarkerType = "UnityEngine.Networking.UnityWebRequest";
        internal const string HttpClientMarkerType = "System.Net.Http.HttpClient";

        internal const string NewtonsoftJsonAssemblyFileName = "Newtonsoft.Json.dll";
        internal const string SystemTextJsonAssemblyFileName = "System.Text.Json.dll";
        internal const string SystemTextEncodingsWebAssemblyFileName = "System.Text.Encodings.Web.dll";
        internal const string UnityWebRequestAssemblyFileName = "UnityEngine.UnityWebRequestModule.dll";
        internal const string HttpClientAssemblyFileName = "System.Net.Http.dll";

        // .NET Standard 2.1 profiles can provide System.Net.Http through netstandard.dll alone. Accepting it
        // is deliberately lenient: the analyzer checks the public HttpClient type and reports OACG012.
        internal const string NetStandardAssemblyFileName = "netstandard.dll";

        internal const string NewtonsoftJsonMinimumVersion = "13.0";
        internal const string SystemTextJsonMinimumVersion = "8.0";

        internal const string NewtonsoftJsonPackageId = "com.unity.nuget.newtonsoft-json";
        internal const string NewtonsoftJsonPackageVersion = "3.2.2";
        internal const string UnityWebRequestModuleId = "com.unity.modules.unitywebrequest";

        readonly ISourceGeneratorLibraryEnvironment _environment;

        internal SourceGeneratorLibraryProbe(ISourceGeneratorLibraryEnvironment environment)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        internal static SourceGeneratorLibraryProbe CreateForCurrentProject()
        {
            return new SourceGeneratorLibraryProbe(
                new CompilationPipelineSourceGeneratorLibraryEnvironment());
        }

        /// <summary>
        /// Evaluates the libraries available to <paramref name="targetAssemblyName"/>. When the assembly is not
        /// known to the compilation pipeline yet (for example a just-created asmdef), the references of every
        /// player assembly are used instead, so a project that has the library somewhere is not blocked.
        /// </summary>
        internal GenerationLibraryReport Inspect(string targetAssemblyName, bool noEngineReferences)
        {
            try
            {
                return InspectCore(targetAssemblyName, noEngineReferences);
            }
            catch (Exception exception)
            {
                return GenerationLibraryReport.Unresolved(
                    "The library check failed: " + exception.Message);
            }
        }

        /// <summary>Whether any player assembly in the project references the library file.</summary>
        internal bool IsLibraryPresentInProject(string assemblyFileName)
        {
            foreach (PlayerAssemblyInfo assembly in _environment.GetPlayerAssemblies())
            {
                if (FindReferences(assembly.CompiledReferencePaths, assemblyFileName).Count != 0)
                {
                    return true;
                }
            }

            return false;
        }

        GenerationLibraryReport InspectCore(string targetAssemblyName, bool noEngineReferences)
        {
            IReadOnlyList<PlayerAssemblyInfo> assemblies = _environment.GetPlayerAssemblies();
            PlayerAssemblyInfo? target = null;
            var projectWide = new List<string>();
            foreach (PlayerAssemblyInfo assembly in assemblies)
            {
                if (string.Equals(assembly.Name, targetAssemblyName, StringComparison.Ordinal))
                {
                    target = assembly;
                }

                projectWide.AddRange(assembly.CompiledReferencePaths);
            }

            IReadOnlyList<string> referenced = target != null
                ? target.CompiledReferencePaths
                : projectWide;

            GenerationLibraryState newtonsoft = Evaluate(
                "Newtonsoft.Json",
                NewtonsoftJsonAssemblyFileName,
                referenced,
                projectWide,
                NewtonsoftJsonMinimumVersion,
                "Install the " + NewtonsoftJsonPackageId + " package " + NewtonsoftJsonPackageVersion
                    + " (Project Settings > OpenAPI Code Generator has an Install button), or add Newtonsoft.Json 13.0 or later.",
                "Reference Newtonsoft.Json.dll from the output assembly: clear 'Override References' on its asmdef, or add Newtonsoft.Json.dll to 'Assembly References'.",
                "Update to Newtonsoft.Json 13.0 or later (" + NewtonsoftJsonPackageId + " 3.2.x).");

            GenerationLibraryState systemTextJson = EvaluateSystemTextJson(referenced, projectWide);

            GenerationLibraryState httpClient = Evaluate(
                "System.Net.Http",
                HttpClientAssemblyFileName,
                referenced,
                projectWide,
                null,
                "System.Net.Http is not available to the project. Use an API Compatibility Level that includes it (.NET Standard 2.1 or .NET Framework).",
                "Reference System.Net.Http from the output assembly: clear 'Override References' on its asmdef, or add System.Net.Http.dll to 'Assembly References'.",
                string.Empty);
            if (!httpClient.IsInstalled &&
                Evaluate(
                    "System.Net.Http",
                    NetStandardAssemblyFileName,
                    referenced,
                    projectWide,
                    null,
                    string.Empty,
                    string.Empty,
                    string.Empty).IsInstalled)
            {
                httpClient = new GenerationLibraryState("System.Net.Http", GenerationLibraryStatus.Installed);
            }

            GenerationLibraryState unityWebRequest = EvaluateUnityWebRequest(
                referenced,
                projectWide,
                noEngineReferences);

            return new GenerationLibraryReport(newtonsoft, systemTextJson, httpClient, unityWebRequest);
        }

        GenerationLibraryState EvaluateSystemTextJson(
            IReadOnlyList<string> referenced,
            IReadOnlyList<string> projectWide)
        {
            GenerationLibraryState core = Evaluate(
                "System.Text.Json",
                SystemTextJsonAssemblyFileName,
                referenced,
                projectWide,
                SystemTextJsonMinimumVersion,
                "Install org.nuget.system.text.json 8.0 or later from the UnityNuGet scoped registry (see the steps in Project Settings), or add System.Text.Json 8.0 DLLs with NuGetForUnity.",
                "Reference System.Text.Json.dll from the output assembly: clear 'Override References' on its asmdef, or add System.Text.Json.dll to 'Assembly References'.",
                "Update to System.Text.Json 8.0 or later (org.nuget.system.text.json).");
            if (!core.IsInstalled)
            {
                return core;
            }

            // The generated code resolves JavaScriptEncoder at run time, so a missing
            // System.Text.Encodings.Web only fails after the project compiles. Catch it here.
            GenerationLibraryState encodings = Evaluate(
                "System.Text.Encodings.Web",
                SystemTextEncodingsWebAssemblyFileName,
                referenced,
                projectWide,
                null,
                "System.Text.Json needs System.Text.Encodings.Web at run time. Install org.nuget.system.text.encodings.web (UnityNuGet installs it as a dependency of org.nuget.system.text.json).",
                "Reference System.Text.Encodings.Web.dll from the output assembly: clear 'Override References' on its asmdef, or add it to 'Assembly References'.",
                string.Empty);
            return encodings.IsInstalled ? core : encodings;
        }

        GenerationLibraryState EvaluateUnityWebRequest(
            IReadOnlyList<string> referenced,
            IReadOnlyList<string> projectWide,
            bool noEngineReferences)
        {
            const string Name = "UnityWebRequest";
            if (noEngineReferences)
            {
                return new GenerationLibraryState(
                    Name,
                    GenerationLibraryStatus.NotReferenced,
                    null,
                    null,
                    "The output asmdef has 'No Engine References' enabled; clear it so the assembly can use UnityEngine.Networking.");
            }

            return Evaluate(
                Name,
                UnityWebRequestAssemblyFileName,
                referenced,
                projectWide,
                null,
                "Enable the Unity Web Request module (" + UnityWebRequestModuleId + ") in Package Manager > Packages: Built-in.",
                "The output assembly does not reference the Unity Web Request module; check the asmdef platform and reference settings.",
                string.Empty);
        }

        GenerationLibraryState Evaluate(
            string name,
            string assemblyFileName,
            IReadOnlyList<string> referenced,
            IReadOnlyList<string> projectWide,
            string? minimumVersion,
            string missingGuidance,
            string notReferencedGuidance,
            string tooOldGuidance)
        {
            IReadOnlyList<string> matches = FindReferences(referenced, assemblyFileName);
            if (matches.Count == 0)
            {
                bool presentElsewhere = FindReferences(projectWide, assemblyFileName).Count != 0;
                return presentElsewhere
                    ? new GenerationLibraryState(
                        name,
                        GenerationLibraryStatus.NotReferenced,
                        null,
                        minimumVersion,
                        notReferencedGuidance)
                    : new GenerationLibraryState(
                        name,
                        GenerationLibraryStatus.Missing,
                        null,
                        minimumVersion,
                        missingGuidance);
            }

            // Several references can define the library (a package plus a stray DLL). Like the
            // analyzer, the highest version wins.
            Version? highest = null;
            foreach (string match in matches)
            {
                Version? version = _environment.GetAssemblyVersion(match);
                if (version != null && (highest == null || version > highest))
                {
                    highest = version;
                }
            }

            // Only libraries with a minimum version show one; engine modules and framework facades report
            // meaningless assembly versions such as 0.0.0.
            string versionText = highest == null || minimumVersion == null
                ? string.Empty
                : FormatVersion(highest);
            if (minimumVersion != null && highest != null && highest < Version.Parse(minimumVersion))
            {
                return new GenerationLibraryState(
                    name,
                    GenerationLibraryStatus.TooOld,
                    versionText,
                    minimumVersion,
                    tooOldGuidance);
            }

            return new GenerationLibraryState(
                name,
                GenerationLibraryStatus.Installed,
                versionText,
                minimumVersion);
        }

        static IReadOnlyList<string> FindReferences(IReadOnlyList<string> paths, string assemblyFileName)
        {
            var matches = new List<string>();
            foreach (string path in paths)
            {
                if (string.Equals(
                        GetFileName(path),
                        assemblyFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(path);
                }
            }

            return matches;
        }

        /// <summary>
        /// '/' と '\' の両方を区切りとして扱う。Path.GetFileName は macOS や Linux の Editor で '\' を区切りとみなさない。
        /// Treats both '/' and '\' as separators; Path.GetFileName ignores '\' in macOS and Linux Editors.
        /// </summary>
        static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            int separator = path.LastIndexOfAny(new[] { '/', '\\' });
            return separator < 0 ? path : path.Substring(separator + 1);
        }

        static string FormatVersion(Version version)
        {
            return version.Revision == 0 && version.Build >= 0
                ? version.ToString(3)
                : version.ToString();
        }
    }
}
