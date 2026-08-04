using System;
using System.IO;
using System.Linq;

using NUnit.Framework;
using Rhycol.OpenApiCodeGen.SourceGenerator;
using UnityEditor.Compilation;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Tests
{
    internal sealed class SourceGeneratorUnityVerificationTests
    {
        private const string AnalyzerFileName = "Rhycol.OpenApiCodeGen.SourceGenerator.dll";
        private const string AdditionalFileSuffix =
            ".Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile";
        private const string TargetAssemblyName =
            "Unity.OpenApiCodeGen.SourceGenerator.Verification";
        private const string TestsAssemblyName =
            "Unity.OpenApiCodeGen.SourceGenerator.Verification.Tests";
        private const string UnrelatedAssemblyName =
            "Unity.OpenApiCodeGen.SourceGenerator.Verification.Unrelated";
        private const string PredefinedEditorAssemblyName = "Assembly-CSharp-Editor";
        private const string RawSpecRelativePath =
            "Assets/OpenApiCodeGen/SourceGeneratorVerification/Specs/openapi.json";

        [Test]
        public void GeneratedClientIsAvailableToTargetAssembly()
        {
            Type clientType = GeneratedClientProbe.ClientType;
            Assert.That(clientType, Is.Not.Null);
            Assert.That(
                clientType.FullName,
                Is.EqualTo("Rhycol.OpenApiCodeGen.Generated.Api"));
            AssertGeneratedOperationMatchesRawDocument(clientType);

            var definition = (OpenApiClientDefinitionAttribute)Attribute.GetCustomAttribute(
                clientType,
                typeof(OpenApiClientDefinitionAttribute));
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.SpecId, Does.Match("^[0-9a-f]{32}$"));
            Assert.That(definition.ApiName, Is.EqualTo("Api"));
            Assert.That(
                definition.GeneratedNamespace,
                Is.EqualTo("Rhycol.OpenApiCodeGen.Generated"));
            Assert.That(definition.DocumentFormat, Is.EqualTo(OpenApiDocumentFormat.Json));
        }

        [Test]
        public void AnalyzerAndAdditionalFileFollowAsmdefReferenceScope()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            var target = FindAssembly(assemblies, TargetAssemblyName);
            var tests = FindAssembly(assemblies, TestsAssemblyName);
            var unrelated = FindAssembly(assemblies, UnrelatedAssemblyName);
            var predefinedEditor = FindAssembly(assemblies, PredefinedEditorAssemblyName);
            string expectedAdditionalFileName =
                GetGeneratedDefinition().SpecId + AdditionalFileSuffix;

            AssertAnalyzerIncluded(target);
            AssertAnalyzerIncluded(tests);
            AssertAnalyzerExcluded(unrelated);
            AssertAnalyzerExcluded(predefinedEditor);

            AssertAdditionalFileIncluded(target, expectedAdditionalFileName);
            AssertAdditionalFileIncluded(tests, expectedAdditionalFileName);
            AssertAdditionalFileExcluded(unrelated, expectedAdditionalFileName);
            AssertAdditionalFileExcluded(predefinedEditor, expectedAdditionalFileName);
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

        private static void AssertAdditionalFileIncluded(
            UnityEditor.Compilation.Assembly assembly,
            string expectedFileName)
        {
            Assert.That(
                GetFileNames(assembly.compilerOptions.RoslynAdditionalFilePaths),
                Does.Contain(expectedFileName));
        }

        private static void AssertAdditionalFileExcluded(
            UnityEditor.Compilation.Assembly assembly,
            string expectedFileName)
        {
            Assert.That(
                GetFileNames(assembly.compilerOptions.RoslynAdditionalFilePaths),
                Does.Not.Contain(expectedFileName));
        }

        private static OpenApiClientDefinitionAttribute GetGeneratedDefinition()
        {
            Type clientType = GeneratedClientProbe.ClientType;
            Assert.That(clientType, Is.Not.Null);
            var definition = (OpenApiClientDefinitionAttribute)Attribute.GetCustomAttribute(
                clientType,
                typeof(OpenApiClientDefinitionAttribute));
            Assert.That(definition, Is.Not.Null);
            return definition;
        }

        private static void AssertGeneratedOperationMatchesRawDocument(Type clientType)
        {
            string rawDocument = File.ReadAllText(Path.GetFullPath(RawSpecRelativePath));
            bool expectsInitialOperation =
                rawDocument.Contains("\"operationId\": \"getItems\"");
            bool expectsUpdatedOperation =
                rawDocument.Contains("\"operationId\": \"getUpdatedItems\"");
            Assert.That(
                expectsInitialOperation ^ expectsUpdatedOperation,
                Is.True,
                "The verification document must declare exactly one known operationId.");

            string expectedMethod = expectsUpdatedOperation ? "getUpdatedItems" : "getItems";
            string obsoleteMethod = expectsUpdatedOperation ? "getItems" : "getUpdatedItems";
            Assert.That(
                clientType.GetMethod(expectedMethod),
                Is.Not.Null,
                $"Analyzer output did not contain method '{expectedMethod}'.");
            Assert.That(
                clientType.GetMethod(obsoleteMethod),
                Is.Null,
                $"Analyzer output still contained obsolete method '{obsoleteMethod}'.");
        }

        private static string[] GetFileNames(string[] paths)
        {
            return (paths ?? Array.Empty<string>()).Select(Path.GetFileName).ToArray();
        }
    }
}
