using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        [Test]
        public void GeneratedClientRejectsMalformedUtf8ResponseBytes()
        {
            byte[] prefix = Encoding.UTF8.GetBytes("[{\"id\":1,\"label\":\"");
            byte[] suffix = Encoding.UTF8.GetBytes("\"}]");
            byte[] body = prefix.Concat(new byte[] { 0xC3, 0x28 }).Concat(suffix).ToArray();

            Exception exception = Assert.CatchAsync<Exception>(
                async () => { await InvokeGeneratedClient(body, null); });

            Assert.That(exception.GetType().FullName,
                Is.EqualTo("Newtonsoft.Json.JsonSerializationException"));
            Assert.That(exception.InnerException, Is.InstanceOf<DecoderFallbackException>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GeneratedClientReadsUnicodeResponseBytes(bool quotedUtf16)
        {
            const string json = "[{\"id\":1,\"label\":\"日本語😀\"}]";
            Encoding encoding = quotedUtf16 ? Encoding.Unicode : Encoding.UTF8;
            byte[] body = encoding.GetPreamble().Concat(encoding.GetBytes(json)).ToArray();

            object response = await InvokeGeneratedClient(
                body, quotedUtf16 ? "\"utf-16\"" : null);
            var items = (IList)response;
            Assert.That(items.Count, Is.EqualTo(1));
            Assert.That(items[0].GetType().GetProperty("Id").GetValue(items[0]), Is.EqualTo(1));
            Assert.That(items[0].GetType().GetProperty("Label").GetValue(items[0]),
                Is.EqualTo("日本語😀"));
        }

        private static async Task<object> InvokeGeneratedClient(byte[] body, string charset)
        {
            Type clientType = GeneratedClientProbe.ClientType;
            Assert.That(clientType, Is.Not.Null);
            MethodInfo method = clientType.GetMethod("getUpdatedItems") ??
                clientType.GetMethod("getItems");
            Assert.That(method, Is.Not.Null);
            using (var httpClient = new HttpClient(new ByteResponseHandler(body, charset)))
            {
                httpClient.BaseAddress = new Uri("https://example.test/");
                object client = Activator.CreateInstance(clientType, httpClient);
                var task = (Task)method.Invoke(client, new object[] { CancellationToken.None });
                await task.ConfigureAwait(false);
                return task.GetType().GetProperty("Result").GetValue(task);
            }
        }

        private sealed class ByteResponseHandler : HttpMessageHandler
        {
            private readonly byte[] _body;
            private readonly string _charset;

            internal ByteResponseHandler(byte[] body, string charset)
            {
                _body = body;
                _charset = charset;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var content = new ByteArrayContent(_body);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
                {
                    CharSet = _charset
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                });
            }
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
            MethodInfo generatedMethod = clientType.GetMethod(expectedMethod);
            Assert.That(
                generatedMethod,
                Is.Not.Null,
                $"Analyzer output did not contain method '{expectedMethod}'.");
            Assert.That(
                clientType.GetMethod(obsoleteMethod),
                Is.Null,
                $"Analyzer output still contained obsolete method '{obsoleteMethod}'.");

            Assert.That(generatedMethod.ReturnType.IsGenericType, Is.True);
            Assert.That(
                generatedMethod.ReturnType.GetGenericTypeDefinition(),
                Is.EqualTo(typeof(Task<>)));
            Type responseType = generatedMethod.ReturnType.GetGenericArguments().Single();
            Assert.That(responseType.GetGenericTypeDefinition(), Is.EqualTo(typeof(List<>)));
            Type itemType = responseType.GetGenericArguments().Single();
            Assert.That(
                itemType.FullName,
                Is.EqualTo("Rhycol.OpenApiCodeGen.Generated.VerificationItem"));
            Assert.That(itemType.GetProperty("Id").PropertyType, Is.EqualTo(typeof(int)));
            Assert.That(
                itemType.GetCustomAttributes(inherit: false)
                    .Select(attribute => attribute.GetType().FullName),
                Does.Contain("Newtonsoft.Json.JsonObjectAttribute"));
            Assert.That(
                itemType.GetProperty("Id").GetCustomAttributes(inherit: false)
                    .Select(attribute => attribute.GetType().FullName),
                Does.Contain("Newtonsoft.Json.JsonPropertyAttribute"));
        }

        private static string[] GetFileNames(string[] paths)
        {
            return (paths ?? Array.Empty<string>()).Select(Path.GetFileName).ToArray();
        }
    }
}
