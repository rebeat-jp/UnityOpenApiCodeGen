using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P3 (G05, G20, G27): operationId synthesis, Path Item $ref / components.pathItems, and
    /// server selection (document / Path Item / operation, variables, several servers).
    /// </summary>
    public sealed class Phase4OperationIdentityAndServerTests
    {
        private const string Ns = Phase4WireHarness.Ns;

        // ---- G05 operationId synthesis --------------------------------------------------------------

        [Theory]
        [InlineData("get", "/pets/{petId}", "GetPetsByPetId")]
        [InlineData("post", "/pets", "PostPets")]
        [InlineData("delete", "/a/{x}/b/{y}", "DeleteAByXBByY")]
        [InlineData("get", "/", "GetRoot")]
        [InlineData("get", "/report.{format}", "GetReportByFormat")]
        [InlineData("get", "/v1/user_profiles", "GetV1UserProfiles")]
        [InlineData("put", "/{id}", "PutById")]
        public void SynthesizedOperationIdIsDeterministic(string method, string path, string expected)
        {
            Assert.Equal(expected, OpenApiSemanticParser.SynthesizeOperationId(method, path));
        }

        [Fact]
        public async Task OperationsWithoutOperationIdGetNamesFromMethodAndPath()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://api.example.test'}],
  'paths':{
    '/pets/{petId}':{'get':{'parameters':[{'name':'petId','in':'path','required':true,'schema':{'type':'string'}}],
       'responses':{'204':{'description':'ok'}}}},
    '/pets':{'post':{'responses':{'204':{'description':'ok'}}}}
  }
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();

            Phase4WireHarness.WireRequest get = await Phase4WireHarness.Send(assembly, "GetPetsByPetId", "p1");
            Phase4WireHarness.WireRequest post = await Phase4WireHarness.Send(assembly, "PostPets");

            Assert.Equal("https://api.example.test/pets/p1", get.Uri.AbsoluteUri);
            Assert.Equal("https://api.example.test/pets", post.Uri.AbsoluteUri);
        }

        [Fact]
        public void SynthesizedNamesAvoidCollisionsWithEachOtherAndWithExplicitIds()
        {
            // '/pets-list' sorts before '/pets/list'; both synthesize GetPetsList. The explicit id
            // GetPetsList on /other wins, so the synthesized ones are numbered.
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{
    '/other':{'get':{'operationId':'GetPetsList','responses':{'204':{'description':'ok'}}}},
    '/pets-list':{'get':{'responses':{'204':{'description':'ok'}}}},
    '/pets/list':{'get':{'responses':{'204':{'description':'ok'}}}}
  }
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);

            Type client = execution.EmitAssembly().GetType(Ns + "Phase4Api")!;
            Assert.NotNull(client.GetMethod("GetPetsList"));
            Assert.NotNull(client.GetMethod("GetPetsList2"));
            Assert.NotNull(client.GetMethod("GetPetsList3"));
        }

        [Fact]
        public void DuplicateExplicitOperationIdStillFails()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{
    '/a':{'get':{'operationId':'same','responses':{'204':{'description':'ok'}}}},
    '/b':{'get':{'operationId':'same','responses':{'204':{'description':'ok'}}}}
  }
}".Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG106", diagnostic.Id);
            Assert.Contains("declared more than once", diagnostic.GetMessage());
        }

        // ---- G20 Path Item $ref ---------------------------------------------------------------------

        [Fact]
        public async Task InternalPathItemReferenceIsFollowed()
        {
            // Both paths use the same Path Item; the operations have no operationId, so each path
            // gets its own synthesized name.
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://api.example.test'}],
  'paths':{
    '/pets/{id}':{'$ref':'#/paths/~1animals~1{id}'},
    '/animals/{id}':{
      'parameters':[{'name':'id','in':'path','required':true,'schema':{'type':'string'}}],
      'get':{'responses':{'204':{'description':'ok'}}}}
  }
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();

            Phase4WireHarness.WireRequest viaRef = await Phase4WireHarness.Send(assembly, "GetPetsById", "7");
            Phase4WireHarness.WireRequest direct = await Phase4WireHarness.Send(assembly, "GetAnimalsById", "7");

            Assert.Equal("https://api.example.test/pets/7", viaRef.Uri.AbsoluteUri);
            Assert.Equal("https://api.example.test/animals/7", direct.Uri.AbsoluteUri);
        }

        [Fact]
        public void ReferencedPathItemIsValidatedAgainstTheReferencingPath()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{
    '/pets/{petId}':{'$ref':'#/paths/~1animals~1{id}'},
    '/animals/{id}':{
      'parameters':[{'name':'id','in':'path','required':true,'schema':{'type':'string'}}],
      'get':{'responses':{'204':{'description':'ok'}}}}
  }
}".Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("petId", diagnostic.GetMessage());
        }

        [Fact]
        public void TwoPathsSharingAnExplicitOperationIdThroughOnePathItemAreDuplicates()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{
    '/a':{'$ref':'#/paths/~1c'},
    '/b':{'$ref':'#/paths/~1c'},
    '/c':{'get':{'operationId':'shared','responses':{'204':{'description':'ok'}}}}
  }
}".Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG106", diagnostic.Id);
            Assert.Contains("declared more than once", diagnostic.GetMessage());
        }


        [Fact]
        public async Task ComponentsPathItemsAreReferencedByOpenApi31()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://api.example.test'}],
  'paths':{
    '/a':{'$ref':'#/components/pathItems/Ping','summary':'A ping'},
    '/b':{'$ref':'#/components/pathItems/Ping'}
  },
  'components':{'pathItems':{'Ping':{
    'get':{'responses':{'204':{'description':'ok'}}},
    'servers':[{'url':'https://ping.example.test/api'}]}}}
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();

            Phase4WireHarness.WireRequest a = await Phase4WireHarness.Send(assembly, "GetA");
            Phase4WireHarness.WireRequest b = await Phase4WireHarness.Send(assembly, "GetB");

            Assert.Equal("https://ping.example.test/api/a", a.Uri.AbsoluteUri);
            Assert.Equal("https://ping.example.test/api/b", b.Uri.AbsoluteUri);
        }

        [Fact]
        public void ComponentsPathItemsAreNotPartOfOpenApi30()
        {
            string spec = @"{
  'openapi':'3.0.3','info':{'title':'t','version':'1'},
  'paths':{},
  'components':{'pathItems':{'Ping':{'get':{'responses':{'204':{'description':'ok'}}}}}}
}".Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/components/pathItems'", diagnostic.GetMessage());
        }

        [Fact]
        public void PathItemReferenceCannotBeCombinedWithOperations()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{
    '/a':{'$ref':'#/paths/~1b','get':{'responses':{'204':{'description':'ok'}}}},
    '/b':{'get':{'responses':{'204':{'description':'ok'}}}}
  }
}".Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/paths/~1a/get'", diagnostic.GetMessage());
        }

        [Fact]
        public void CyclicPathItemReferenceIsRejected()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{'/a':{'$ref':'#/paths/~1b'},'/b':{'$ref':'#/paths/~1a'}}
}".Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG103", diagnostic.Id);
        }

        [Fact]
        public async Task ExternalPathItemReferenceIsResolvedWithItsOwnReferences()
        {
            const string externalId = "Assets/Specs/pets.json";
            string root = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://api.example.test'}],
  'paths':{'/pets':{'$ref':'pets.json#/paths/~1catalog'}}
}".Replace('\'', '"');
            // The external Path Item refers to a parameter, which refers to a schema, all inside
            // the external document.
            string external = @"{
  'openapi':'3.1.0','info':{'title':'ext','version':'1'},
  'paths':{'/catalog':{
    'parameters':[{'$ref':'#/components/parameters/Limit'}],
    'get':{'operationId':'listPets','responses':{'204':{'description':'ok'}}}}},
  'components':{
    'parameters':{'Limit':{'name':'limit','in':'query','schema':{'$ref':'#/components/schemas/Count'}}},
    'schemas':{'Count':{'type':'integer'}}}
}".Replace('\'', '"');
            string bundle = TestBundleFactory.CreateV2(
                root,
                new[] { new TestBundleFactory.V2DocumentSpec(externalId, externalId, "json", external) },
                new[]
                {
                    // Sorted by source document id, then source pointer (ordinal).
                    new TestBundleFactory.V2ReferenceSpec(
                        externalId,
                        "/components/parameters/Limit/schema/$ref",
                        externalId,
                        "/components/schemas/Count"),
                    new TestBundleFactory.V2ReferenceSpec(
                        externalId,
                        "/paths/~1catalog/parameters/0/$ref",
                        externalId,
                        "/components/parameters/Limit"),
                    new TestBundleFactory.V2ReferenceSpec("root", "/paths/~1pets/$ref", externalId, "/paths/~1catalog")
                });

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(root, bundle: bundle);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Phase4WireHarness.WireRequest request = await Phase4WireHarness.Send(
                execution.EmitAssembly(), "listPets", 5);
            Assert.Equal("https://api.example.test/pets?limit=5", request.Uri.AbsoluteUri);
        }


        // ---- G20 / G27 servers ----------------------------------------------------------------------

        private const string ServerSpec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://doc.example.test/v1'}],
  'paths':{
    '/d':{'get':{'operationId':'docOp','responses':{'204':{'description':'ok'}}}},
    '/p':{
      'servers':[{'url':'https://path.example.test/pv'}],
      'get':{'operationId':'pathOp','responses':{'204':{'description':'ok'}}}},
    '/o':{
      'servers':[{'url':'https://path.example.test/ignored'}],
      'get':{'operationId':'opOp',
             'servers':[{'url':'https://op.example.test/ov'}],
             'responses':{'204':{'description':'ok'}}}}
  }
}";

        private static Assembly ServerAssembly()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                ServerSpec.Replace('\'', '"'));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        [Fact]
        public async Task OperationServerBeatsPathServerBeatsDocumentServer()
        {
            Assembly assembly = ServerAssembly();

            Assert.Equal("https://doc.example.test/v1/d", (await Phase4WireHarness.Send(assembly, "docOp")).Uri.AbsoluteUri);
            Assert.Equal("https://path.example.test/pv/p", (await Phase4WireHarness.Send(assembly, "pathOp")).Uri.AbsoluteUri);
            Assert.Equal("https://op.example.test/ov/o", (await Phase4WireHarness.Send(assembly, "opOp")).Uri.AbsoluteUri);
        }

        [Fact]
        public async Task ExplicitBaseUrlOnlyOverridesTheDocumentLevelServers()
        {
            Assembly assembly = ServerAssembly();

            Phase4WireHarness.WireRequest doc = await Phase4WireHarness.SendWith(
                assembly, "docOp", "https://override.example.test/root", null);
            Phase4WireHarness.WireRequest path = await Phase4WireHarness.SendWith(
                assembly, "pathOp", "https://override.example.test/root", null);
            Phase4WireHarness.WireRequest op = await Phase4WireHarness.SendWith(
                assembly, "opOp", "https://override.example.test/root", null);

            Assert.Equal("https://override.example.test/root/d", doc.Uri.AbsoluteUri);
            Assert.Equal("https://path.example.test/pv/p", path.Uri.AbsoluteUri);
            Assert.Equal("https://op.example.test/ov/o", op.Uri.AbsoluteUri);
        }

        [Fact]
        public async Task OperationServerUrlsReplacesAnOperationServerAtRuntime()
        {
            Assembly assembly = ServerAssembly();
            Type client = Phase4WireHarness.ClientType(assembly);

            Phase4WireHarness.WireRequest path = await Phase4WireHarness.SendWith(
                assembly,
                "pathOp",
                null,
                instance =>
                {
                    var urls = (IDictionary<string, string>)client.GetProperty("OperationServerUrls")!.GetValue(instance)!;
                    urls["pathOp"] = "https://custom.example.test/c";
                });

            Assert.Equal("https://custom.example.test/c/p", path.Uri.AbsoluteUri);
        }

        private const string VariablesSpec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[
    {'url':'https://{region}.example.com/{base}','variables':{
       'region':{'default':'us','enum':['us','eu']},
       'base':{'default':'v1'}}},
    {'url':'https://staging.example.com'}],
  'paths':{
    '/d':{'get':{'operationId':'docOp','responses':{'204':{'description':'ok'}}}},
    '/p':{'get':{'operationId':'opOp',
       'servers':[{'url':'https://{tenant}.op.example.com','variables':{'tenant':{'default':'acme','enum':['acme','beta']}}}],
       'responses':{'204':{'description':'ok'}}}}
  }
}";

        [Fact]
        public async Task DefaultServerIsTheFirstOneWithVariableDefaultsApplied()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                VariablesSpec.Replace('\'', '"'));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();

            Assert.Equal("https://us.example.com/v1/d", (await Phase4WireHarness.Send(assembly, "docOp")).Uri.AbsoluteUri);
            Assert.Equal("https://acme.op.example.com/p", (await Phase4WireHarness.Send(assembly, "opOp")).Uri.AbsoluteUri);
        }

        [Fact]
        public void ServersAndBuildServerUrlExposeAllCandidatesAndValidateEnums()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                VariablesSpec.Replace('\'', '"'));
            Type client = Phase4WireHarness.ClientType(execution.EmitAssembly());
            MethodInfo build = client.GetMethod("BuildServerUrl")!;

            var servers = (IReadOnlyList<string>)client.GetProperty("Servers")!.GetValue(null)!;
            Assert.Equal(
                new[] { "https://{region}.example.com/{base}", "https://staging.example.com" },
                servers.ToArray());

            string Build(int index, Dictionary<string, string>? values)
            {
                try
                {
                    return (string)build.Invoke(null, new object?[] { index, values })!;
                }
                catch (TargetInvocationException exception)
                {
                    throw exception.InnerException!;
                }
            }

            Assert.Equal("https://us.example.com/v1", Build(0, null));
            Assert.Equal("https://eu.example.com/v1", Build(0, new Dictionary<string, string> { ["region"] = "eu" }));
            Assert.Equal(
                "https://us.example.com/v9",
                Build(0, new Dictionary<string, string> { ["base"] = "v9" }));
            Assert.Equal("https://staging.example.com", Build(1, null));
            Assert.Contains("not allowed", Assert.Throws<ArgumentException>(
                () => Build(0, new Dictionary<string, string> { ["region"] = "asia" })).Message);
            Assert.Contains("not declared", Assert.Throws<ArgumentException>(
                () => Build(0, new Dictionary<string, string> { ["nope"] = "x" })).Message);
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(2, null));
        }

        [Fact]
        public async Task ExpandedServerUrlCanBePassedToTheConstructor()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                VariablesSpec.Replace('\'', '"'));
            Assembly assembly = execution.EmitAssembly();
            Type client = Phase4WireHarness.ClientType(assembly);
            string url = (string)client.GetMethod("BuildServerUrl")!.Invoke(
                null,
                new object?[] { 0, new Dictionary<string, string> { ["region"] = "eu" } })!;

            Phase4WireHarness.WireRequest request = await Phase4WireHarness.SendWith(assembly, "docOp", url, null);

            Assert.Equal("https://eu.example.com/v1/d", request.Uri.AbsoluteUri);
        }

        [Fact]
        public void OperationServerCatalogIsPublicAndValidatesEnums()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                VariablesSpec.Replace('\'', '"'));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Type client = Phase4WireHarness.ClientType(execution.EmitAssembly());
            MethodInfo list = client.GetMethod("GetOperationServers")!;
            MethodInfo build = client.GetMethod("BuildOperationServerUrl")!;
            Type names = client.GetNestedType("OperationNames")!;

            // The constant is the key for OperationServerUrls and the operation-server helpers.
            string key = (string)names.GetField("opOp")!.GetValue(null)!;
            Assert.Equal("opOp", key);
            Assert.Equal(
                new[] { "https://{tenant}.op.example.com" },
                ((IReadOnlyList<string>)list.Invoke(null, new object[] { key })!).ToArray());

            string Build(string name, int index, Dictionary<string, string>? values)
            {
                try
                {
                    return (string)build.Invoke(null, new object?[] { name, index, values })!;
                }
                catch (TargetInvocationException exception)
                {
                    throw exception.InnerException!;
                }
            }

            Assert.Equal("https://acme.op.example.com", Build(key, 0, null));
            Assert.Equal(
                "https://beta.op.example.com",
                Build(key, 0, new Dictionary<string, string> { ["tenant"] = "beta" }));
            Assert.Contains("not allowed", Assert.Throws<ArgumentException>(
                () => Build(key, 0, new Dictionary<string, string> { ["tenant"] = "zzz" })).Message);
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(key, 1, null));
            Assert.Throws<ArgumentException>(() => Build("docOp", 0, null));
            Assert.Throws<TargetInvocationException>(
                () => list.Invoke(null, new object[] { "docOp" }));
            Assert.Contains("not thread-safe", execution.GeneratedSource);
        }

        [Fact]
        public void ServerVariableValuesAreSubstitutedInOnePass()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                VariablesSpec.Replace('\'', '"'));
            Type client = Phase4WireHarness.ClientType(execution.EmitAssembly());

            string url = (string)client.GetMethod("BuildServerUrl")!.Invoke(
                null,
                new object?[] { 0, new Dictionary<string, string> { ["base"] = "{region}" } })!;

            // The substituted "{region}" is not expanded again.
            Assert.Equal("https://us.example.com/{region}", url);
        }

        [Theory]
        [InlineData("get", "/café/{naïve id}", "GetCaféByNaïveId")]
        [InlineData("get", "//a--b//{x}__y", "GetABByXY")]
        [InlineData("post", "/---/", "PostRoot")]
        [InlineData("get", "/{}/x", "GetX")]
        public void SynthesizedOperationIdHandlesNonAsciiAndRepeatedSeparators(string method, string path, string expected)
        {
            Assert.Equal(expected, OpenApiSemanticParser.SynthesizeOperationId(method, path));
        }

        [Fact]
        public void NonAsciiPathSynthesizesACompilableMethod()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{'/café/{naïve id}':{'get':{'parameters':[{'name':'naïve id','in':'path','required':true,'schema':{'type':'string'}}],
    'responses':{'204':{'description':'ok'}}}}}
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.NotNull(Phase4WireHarness.ClientType(execution.EmitAssembly()).GetMethod("GetCaféByNaïveId"));
        }

        [Fact]
        public void DocumentWithoutServersExposesTheImplicitRootServer()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{'/d':{'get':{'operationId':'docOp','responses':{'204':{'description':'ok'}}}}}
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);
            Type client = Phase4WireHarness.ClientType(execution.EmitAssembly());

            var servers = (IReadOnlyList<string>)client.GetProperty("Servers")!.GetValue(null)!;

            Assert.Equal(new[] { "/" }, servers.ToArray());
        }

        [Fact]
        public void UndeclaredServerVariableIsReportedAtTheUrl()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://{host}.example.com','variables':{'other':{'default':'x'}}}],
  'paths':{}
}".Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/servers/0/url'", diagnostic.GetMessage());
            Assert.Contains("{host}", diagnostic.GetMessage());
        }

        [Fact]
        public void ServerVariableDefaultOutsideItsEnumIsInvalid()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'servers':[{'url':'https://{r}.example.com','variables':{'r':{'default':'asia','enum':['us','eu']}}}],
  'paths':{}
}".Replace('\'', '"');

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(spec).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("logical path '/servers/0/variables/r/default'", diagnostic.GetMessage());
        }

        [Fact]
        public void SchemaNamedLikeAGeneratedServerMemberIsRenamed()
        {
            string spec = @"{
  'openapi':'3.1.0','info':{'title':'t','version':'1'},
  'paths':{'/d':{'get':{'operationId':'docOp','responses':{'200':{'description':'ok','content':{'application/json':{
    'schema':{'$ref':'#/components/schemas/Servers'}}}}}}}},
  'components':{'schemas':{'Servers':{'type':'object','additionalProperties':false,'properties':{'n':{'type':'integer'}}}}}
}".Replace('\'', '"');
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(spec);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("class Servers2", execution.GeneratedSource);
        }
    }
}
