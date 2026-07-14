using System;
using System.IO;
using System.Linq;

using NUnit.Framework;
using UnityEditor.Compilation;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Tests
{
    internal sealed class SourceGeneratorUnityVerificationTests
    {
        private const string AnalyzerFileName = "Rhycol.OpenApiCodeGen.SourceGenerator.dll";
        private const string AdditionalFileName =
            "0123456789abcdef0123456789abcdef.Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile";
        private const string TargetAssemblyName =
            "Unity.OpenApiCodeGen.SourceGenerator.Verification";
        private const string TestsAssemblyName =
            "Unity.OpenApiCodeGen.SourceGenerator.Verification.Tests";
        private const string UnrelatedAssemblyName =
            "Unity.OpenApiCodeGen.SourceGenerator.Verification.Unrelated";
        private const string PredefinedEditorAssemblyName = "Assembly-CSharp-Editor";

        [Test]
        public void GeneratedClientIsAvailableToTargetAssembly()
        {
            Assert.That(
                GeneratedClientProbe.ClientType.FullName,
                Is.EqualTo("Rhycol.OpenApiCodeGen.Generated.Api"));
        }

        [Test]
        public void AnalyzerAndAdditionalFileFollowAsmdefReferenceScope()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            var target = FindAssembly(assemblies, TargetAssemblyName);
            var tests = FindAssembly(assemblies, TestsAssemblyName);
            var unrelated = FindAssembly(assemblies, UnrelatedAssemblyName);
            var predefinedEditor = FindAssembly(assemblies, PredefinedEditorAssemblyName);

            AssertAnalyzerIncluded(target);
            AssertAnalyzerIncluded(tests);
            AssertAnalyzerExcluded(unrelated);
            AssertAnalyzerExcluded(predefinedEditor);

            AssertAdditionalFileIncluded(target);
            AssertAdditionalFileIncluded(tests);
            AssertAdditionalFileExcluded(unrelated);
            AssertAdditionalFileExcluded(predefinedEditor);
        }

        private static UnityEditor.Compilation.Assembly FindAssembly(
            UnityEditor.Compilation.Assembly[] assemblies,
            string assemblyName)
        {
            var assembly = assemblies.SingleOrDefault(candidate => candidate.name == assemblyName);
            Assert.That(
                assembly,
                Is.Not.Null,
                $"Assembly '{assemblyName}' was not returned by the compilation pipeline.");
            return assembly;
        }

        private static void AssertAnalyzerIncluded(UnityEditor.Compilation.Assembly assembly)
        {
            Assert.That(
                GetFileNames(assembly.compilerOptions.RoslynAnalyzerDllPaths),
                Does.Contain(AnalyzerFileName));
        }

        private static void AssertAnalyzerExcluded(UnityEditor.Compilation.Assembly assembly)
        {
            Assert.That(
                GetFileNames(assembly.compilerOptions.RoslynAnalyzerDllPaths),
                Does.Not.Contain(AnalyzerFileName));
        }

        private static void AssertAdditionalFileIncluded(UnityEditor.Compilation.Assembly assembly)
        {
            Assert.That(
                GetFileNames(assembly.compilerOptions.RoslynAdditionalFilePaths),
                Does.Contain(AdditionalFileName));
        }

        private static void AssertAdditionalFileExcluded(UnityEditor.Compilation.Assembly assembly)
        {
            Assert.That(
                GetFileNames(assembly.compilerOptions.RoslynAdditionalFilePaths),
                Does.Not.Contain(AdditionalFileName));
        }

        private static string[] GetFileNames(string[] paths)
        {
            return (paths ?? Array.Empty<string>()).Select(Path.GetFileName).ToArray();
        }
    }
}
