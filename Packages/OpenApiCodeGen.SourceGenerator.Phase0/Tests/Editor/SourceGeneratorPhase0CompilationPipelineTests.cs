using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.Tests
{
    internal sealed class SourceGeneratorPhase0CompilationPipelineTests
    {
        private const string AnalyzerFileName = "Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.dll";
        private const string OwnerAssemblyName = "Unity.OpenApiCodeGen.SourceGenerator.Phase0";
        private const string TestsAssemblyName = "Unity.OpenApiCodeGen.SourceGenerator.Phase0.Tests";
        private const string UnrelatedAssemblyName = "Unity.OpenApiCodeGen.SourceGenerator.Phase0.Unrelated";
        private const string PredefinedEditorAssemblyName = "Assembly-CSharp-Editor";
        private const string RootAdditionalFileName = "Root.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.additionalfile";
        private const string ScopedAdditionalFileName = "Scoped.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.additionalfile";
        private const string IgnoredAdditionalFileName = "Ignored.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.Other.additionalfile";

        [Test]
        public void AnalyzerAndAdditionalFilesFollowAsmdefReferenceScope()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            var owner = FindAssembly(assemblies, OwnerAssemblyName);
            var tests = FindAssembly(assemblies, TestsAssemblyName);
            var unrelated = FindAssembly(assemblies, UnrelatedAssemblyName);
            var predefinedEditor = FindAssembly(assemblies, PredefinedEditorAssemblyName);

            AssertAnalyzerIncluded(owner);
            AssertAnalyzerIncluded(tests);
            AssertAnalyzerExcluded(unrelated);
            AssertAnalyzerExcluded(predefinedEditor);

            AssertAdditionalFiles(owner);
            AssertAdditionalFiles(tests);
            AssertAdditionalFilesExcluded(unrelated);
            AssertAdditionalFilesExcluded(predefinedEditor);
        }

        private static UnityEditor.Compilation.Assembly FindAssembly(
            UnityEditor.Compilation.Assembly[] assemblies,
            string assemblyName)
        {
            var assembly = assemblies.SingleOrDefault(candidate => candidate.name == assemblyName);
            Assert.That(assembly, Is.Not.Null, $"Assembly '{assemblyName}' was not returned by the compilation pipeline.");
            return assembly;
        }

        private static void AssertAnalyzerIncluded(UnityEditor.Compilation.Assembly assembly)
        {
            Assert.That(GetFileNames(assembly.compilerOptions.RoslynAnalyzerDllPaths), Does.Contain(AnalyzerFileName));
        }

        private static void AssertAnalyzerExcluded(UnityEditor.Compilation.Assembly assembly)
        {
            Assert.That(GetFileNames(assembly.compilerOptions.RoslynAnalyzerDllPaths), Does.Not.Contain(AnalyzerFileName));
        }

        private static void AssertAdditionalFiles(UnityEditor.Compilation.Assembly assembly)
        {
            var fileNames = GetFileNames(assembly.compilerOptions.RoslynAdditionalFilePaths);
            Assert.That(fileNames, Does.Contain(RootAdditionalFileName));
            Assert.That(fileNames, Does.Contain(ScopedAdditionalFileName));
            Assert.That(fileNames, Does.Not.Contain(IgnoredAdditionalFileName));
        }

        private static void AssertAdditionalFilesExcluded(UnityEditor.Compilation.Assembly assembly)
        {
            var fileNames = GetFileNames(assembly.compilerOptions.RoslynAdditionalFilePaths);
            Assert.That(fileNames, Does.Not.Contain(RootAdditionalFileName));
            Assert.That(fileNames, Does.Not.Contain(ScopedAdditionalFileName));
            Assert.That(fileNames, Does.Not.Contain(IgnoredAdditionalFileName));
        }

        private static string[] GetFileNames(string[] paths)
        {
            return (paths ?? Array.Empty<string>()).Select(Path.GetFileName).ToArray();
        }
    }
}
