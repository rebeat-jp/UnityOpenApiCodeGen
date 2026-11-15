using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P2 composition (allOf, oneOf / anyOf with and without a discriminator, $ref siblings, not).
    /// Every feature is generated, compiled, then exercised through a fake HttpMessageHandler so the
    /// wire JSON and the deserialized result are both asserted. JSON in this file uses single quotes
    /// for readability (see <see cref="J"/>).
    /// </summary>
    public class Phase4CompositionRuntimeTests
    {
        public Phase4CompositionRuntimeTests()
            : this(SerializerMatrixHarness.NewtonsoftJson)
        {
        }

        protected Phase4CompositionRuntimeTests(string serializer)
        {
            MatrixMode.Current = serializer;
        }

        private const string Ns = "Generated.Phase4.";
        private const string Components = "#/components/schemas/";

        [Fact]
        public void TheRunUsesTheSerializerItIsNamedFor()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument("'Body':{'type':'string'}"));

            MatrixMode.AssertGeneratedFor(execution.GeneratedSource, ExpectsSystemTextJson);
        }

        protected virtual bool ExpectsSystemTextJson => false;

        // Cat / Dog that do not declare the discriminator property: the generator adds it.
        private const string CatDogUndeclared = @"
          'Cat':{'type':'object','additionalProperties':false,'required':['meow'],
                 'properties':{'meow':{'type':'boolean'}}},
          'Dog':{'type':'object','additionalProperties':false,'required':['bark'],
                 'properties':{'bark':{'type':'string'}}}";

        private const string PetUnionExplicit = @"
          'Pet':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                 'discriminator':{'propertyName':'petType','mapping':{
                   'cat':'#/components/schemas/Cat','dog':'Dog','puppy':'#/components/schemas/Dog'}}},
          'Body':{'$ref':'#/components/schemas/Pet'},";

        // ---- G07 oneOf + discriminator -----------------------------------------------------------

        [Fact]
        public async Task ExplicitMappingDispatchesByDiscriminatorValue()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(PetUnionExplicit + CatDogUndeclared));

            Exchange cat = await Echo(execution, J("{'petType':'cat','meow':true}"), J("{'petType':'cat','meow':false}"));
            AssertJsonEqual(J("{'petType':'cat','meow':true}"), cat.RequestJson);
            Assert.True(TryGet(cat.Result!, "TryGetCat", out object? catValue));
            Assert.False((bool)ReadPath(catValue!, "Meow")!);
            Assert.False(TryGet(cat.Result!, "TryGetDog", out _));
            Assert.Equal("cat", ReadPath(cat.Result!, "DiscriminatorValue"));

            // A second explicit mapping value for the same schema selects it too; the first one is written.
            Exchange dog = await Echo(execution, J("{'petType':'puppy','bark':'woof'}"), J("{'petType':'dog','bark':'arf'}"));
            AssertJsonEqual(J("{'petType':'dog','bark':'woof'}"), dog.RequestJson);
            Assert.Equal("arf", ReadPath(ReadPath(dog.Result!, "Value")!, "Bark"));
        }

        [Fact]
        public async Task ImplicitMappingUsesTheSchemaName()
        {
            const string Schemas = @"
              'Pet':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                     'discriminator':{'propertyName':'petType'}},
              'Body':{'$ref':'#/components/schemas/Pet'},";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas + CatDogUndeclared));

            Exchange exchange = await Echo(execution, J("{'petType':'Dog','bark':'x'}"), J("{'petType':'Cat','meow':true}"));

            AssertJsonEqual(J("{'petType':'Dog','bark':'x'}"), exchange.RequestJson);
            Assert.True(TryGet(exchange.Result!, "TryGetCat", out _));
            // The lower-case name is not the schema name.
            Exception unknown = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("{'petType':'Dog','bark':'x'}"), J("{'petType':'cat','meow':true}")));
            Assert.Contains("'cat'", unknown.Message);
        }

        [Fact]
        public async Task UnknownOrMissingDiscriminatorInAResponseIsAnError()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(PetUnionExplicit + CatDogUndeclared));
            string request = J("{'petType':'cat','meow':true}");

            Exception unknown = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, request, J("{'petType':'lizard','scales':1}")));
            Assert.Contains("'lizard'", unknown.Message);
            Assert.Contains("$.petType", unknown.Message);

            Exception missing = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, request, J("{'meow':true}")));
            Assert.Contains("discriminator", missing.Message);

            Exception notAString = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, request, J("{'petType':5,'meow':true}")));
            Assert.Contains("not a string", notAString.Message);

            // The selected member is still validated (here: closed, so an undeclared property fails).
            Exception undeclared = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, request, J("{'petType':'cat','meow':true,'extra':1}")));
            Assert.Contains("$.extra", undeclared.Message);
        }

        [Fact]
        public async Task ConcreteMemberIsWrittenWithItsDiscriminatorAndWithoutRecursion()
        {
            const string Schemas = @"
              'Pet':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                     'discriminator':{'propertyName':'petType','mapping':{'cat':'Cat','dog':'Dog'}}},
              'Body':{'$ref':'#/components/schemas/Pet'},";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas + CatDogUndeclared));
            Assembly assembly = Load(execution);
            Type body = assembly.GetType(Ns + "Pet", true)!;
            object dog = Activator.CreateInstance(assembly.GetType(Ns + "Dog", true)!)!;
            dog.GetType().GetProperty("Bark")!.SetValue(dog, "woof");

            // The discriminator is a default of the member itself ...
            Assert.Equal("dog", dog.GetType().GetProperty("PetType")!.GetValue(dog));
            // ... and a plain member serializes without the union converter.
            AssertJsonEqual(J("{'bark':'woof','petType':'dog'}"), MatrixMode.Serialize(dog));

            object viaFactory = body.GetMethod("FromDog")!.Invoke(null, new[] { dog })!;
            Exchange factory = await Echo(execution, viaFactory, J("{'petType':'dog','bark':'x'}"));
            AssertJsonEqual(J("{'petType':'dog','bark':'woof'}"), factory.RequestJson);

            // implicit conversion
            MethodInfo implicitOperator = body.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "op_Implicit" && method.GetParameters()[0].ParameterType == dog.GetType());
            object viaOperator = implicitOperator.Invoke(null, new[] { dog })!;
            Exchange operatorExchange = await Echo(execution, viaOperator, J("{'petType':'dog','bark':'x'}"));
            AssertJsonEqual(J("{'petType':'dog','bark':'woof'}"), operatorExchange.RequestJson);

            // The union wins over a stale discriminator value set by the caller.
            dog.GetType().GetProperty("PetType")!.SetValue(dog, "stale");
            object stale = body.GetMethod("FromDog")!.Invoke(null, new[] { dog })!;
            Exchange staleExchange = await Echo(execution, stale, J("{'petType':'dog','bark':'x'}"));
            AssertJsonEqual(J("{'petType':'dog','bark':'woof'}"), staleExchange.RequestJson);
        }

        [Fact]
        public async Task PolymorphicItemsInAnArrayRoundTrip()
        {
            const string Schemas = @"
              'Body':{'type':'array','items':{'$ref':'#/components/schemas/Pet'}},
              'Pet':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                     'discriminator':{'propertyName':'petType','mapping':{'cat':'Cat','dog':'Dog'}}},";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas + CatDogUndeclared));
            const string Json = "[{'petType':'cat','meow':true},{'petType':'dog','bark':'a'},{'petType':'cat','meow':false}]";

            Exchange exchange = await Echo(execution, J(Json), J(Json));

            AssertJsonEqual(J(Json), exchange.RequestJson);
            var items = ((System.Collections.IEnumerable)exchange.Result!).Cast<object>().ToArray();
            Assert.Equal(3, items.Length);
            Assert.Equal(new[] { "cat", "dog", "cat" }, items.Select(item => (string)ReadPath(item, "DiscriminatorValue")!));
            Exception bad = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J(Json), J("[{'petType':'cat','meow':true},{'petType':'bird'}]")));
            Assert.Contains("$[1].petType", bad.Message);
        }

        [Fact]
        public async Task MembersThatDeclareTheDiscriminatorKeepTheirOwnProperty()
        {
            const string Schemas = @"
              'Pet':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                     'discriminator':{'propertyName':'petType'}},
              'Body':{'$ref':'#/components/schemas/Pet'},
              'Cat':{'type':'object','additionalProperties':false,'required':['petType'],
                     'properties':{'petType':{'type':'string'},'meow':{'type':'boolean'}}},
              'Dog':{'type':'object','additionalProperties':false,
                     'properties':{'petType':{'type':'string'},'bark':{'type':'string'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, J("{'petType':'Cat','meow':true}"), J("{'petType':'Dog','bark':'x'}"));

            AssertJsonEqual(J("{'petType':'Cat','meow':true}"), exchange.RequestJson);
            Assert.Equal("Dog", ReadPath(ReadPath(exchange.Result!, "Value")!, "PetType"));
            // The default of the declared property is the (single) selecting value.
            object cat = Activator.CreateInstance(Load(execution).GetType(Ns + "Cat", true)!)!;
            Assert.Equal("Cat", ReadPath(cat, "PetType"));
        }

        // ---- G07 inheritance: base with a discriminator, children use allOf ---------------------------

        [Theory]
        [InlineData("implicit")]
        [InlineData("explicit")]
        public async Task BaseWithDiscriminatorSelectsAllOfSubtypes(string mapping)
        {
            string discriminator = mapping == "implicit"
                ? "{'propertyName':'petType'}"
                : "{'propertyName':'petType','mapping':{'cat':'#/components/schemas/Cat','dog':'#/components/schemas/Dog'}}";
            string schemas = @"
              'Body':{'$ref':'#/components/schemas/Pet'},
              'Pet':{'type':'object','required':['petType'],'discriminator':" + discriminator + @",
                     'properties':{'petType':{'type':'string'},'name':{'type':'string'}}},
              'Cat':{'allOf':[{'$ref':'#/components/schemas/Pet'},
                              {'type':'object','required':['meow'],'properties':{'meow':{'type':'boolean'}}}]},
              'Dog':{'allOf':[{'$ref':'#/components/schemas/Pet'},
                              {'type':'object','properties':{'bark':{'type':'string'}}}]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(schemas));
            string catValue = mapping == "implicit" ? "Cat" : "cat";
            string dogValue = mapping == "implicit" ? "Dog" : "dog";

            Exchange exchange = await Echo(
                execution,
                J("{'petType':'" + catValue + "','name':'tom','meow':true}"),
                J("{'petType':'" + dogValue + "','name':'rex','bark':'woof'}"));

            AssertJsonEqual(J("{'petType':'" + catValue + "','name':'tom','meow':true}"), exchange.RequestJson);
            Assert.True(TryGet(exchange.Result!, "TryGetDog", out object? dog));
            // allOf flattening: the subtype carries the inherited properties.
            Assert.Equal("rex", ReadPath(dog!, "Name"));
            Assert.Equal("woof", ReadPath(dog!, "Bark"));
            Assert.Equal(dogValue, ReadPath(dog!, "PetType"));
            await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("{'petType':'" + catValue + "','meow':true}"), J("{'petType':'Pet','name':'x'}")));
        }

        [Fact]
        public async Task UnionThatIsAlsoTheAllOfBaseOfItsMembers()
        {
            // Cat / Dog extend the very schema that lists them in oneOf: the base keeps its oneOf for
            // itself, the subtypes only inherit its properties.
            const string Schemas = @"
              'Body':{'$ref':'#/components/schemas/Pet'},
              'Pet':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                     'discriminator':{'propertyName':'kind'},
                     'properties':{'name':{'type':'string'}}},
              'Cat':{'allOf':[{'$ref':'#/components/schemas/Pet'},
                              {'type':'object','properties':{'meow':{'type':'boolean'}}}]},
              'Dog':{'allOf':[{'$ref':'#/components/schemas/Pet'},
                              {'type':'object','properties':{'bark':{'type':'string'}}}]}";
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(EchoDocument(Schemas));

            // oneOf next to properties is not supported on the same schema.
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Pet/properties'", diagnostic.GetMessage());

            const string Supported = @"
              'Body':{'$ref':'#/components/schemas/Pet'},
              'Pet':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                     'discriminator':{'propertyName':'kind'}},
              'Cat':{'allOf':[{'$ref':'#/components/schemas/Pet'},
                              {'type':'object','properties':{'meow':{'type':'boolean'}}}]},
              'Dog':{'allOf':[{'$ref':'#/components/schemas/Pet'},
                              {'type':'object','properties':{'bark':{'type':'string'}}}]}";
            Phase4GeneratorExecution ok = Generate(EchoDocument(Supported));

            Exchange exchange = await Echo(ok, J("{'kind':'Cat','meow':true}"), J("{'kind':'Dog','bark':'b'}"));

            AssertJsonEqual(J("{'kind':'Cat','meow':true}"), exchange.RequestJson);
            Assert.True(TryGet(exchange.Result!, "TryGetDog", out _));
        }

        // ---- G06 allOf -----------------------------------------------------------------------------

        private const string AllOfBaseAndExtension = @"
          'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                           {'type':'object','required':['extra'],
                            'properties':{'extra':{'type':'string'},'count':{'type':'integer'}}}]},
          'Base':{'type':'object','additionalProperties':false,'required':['id'],
                  'properties':{'id':{'type':'integer'}}}";

        [Fact]
        public async Task AllOfFlattensPartsIntoOneSealedDto()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(AllOfBaseAndExtension));
            Type body = Load(execution).GetType(Ns + "Body", true)!;
            Assert.True(body.IsSealed);
            Assert.NotNull(body.GetProperty("Id"));
            Assert.NotNull(body.GetProperty("Extra"));
            Assert.NotNull(body.GetProperty("Count"));

            Exchange exchange = await Echo(execution, J("{'id':1,'extra':'e','count':2}"), J("{'id':3,'extra':'x'}"));

            AssertJsonEqual(J("{'id':1,'extra':'e','count':2}"), exchange.RequestJson);
            Assert.Equal(3, ReadPath(exchange.Result!, "Id"));
            Assert.Equal("x", ReadPath(exchange.Result!, "Extra"));
        }

        [Fact]
        public async Task AllOfRequiredIsTheUnionOfTheParts()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(AllOfBaseAndExtension));
            string request = J("{'id':1,'extra':'e'}");

            // required from the first part
            Exception noId = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, request, J("{'extra':'x'}")));
            Assert.Contains("$.id", noId.Message);
            // required from the second part
            Exception noExtra = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, request, J("{'id':1}")));
            Assert.Contains("$.extra", noExtra.Message);
        }

        [Fact]
        public async Task AllOfIsClosedOverTheComposedPropertiesWhenAnyPartIsClosed()
        {
            // Base says additionalProperties:false, yet the other part's properties stay valid: closedness
            // is decided on the composed object.
            Phase4GeneratorExecution execution = Generate(EchoDocument(AllOfBaseAndExtension));
            Type body = Load(execution).GetType(Ns + "Body", true)!;
            Assert.Null(body.GetProperty("AdditionalProperties"));
            string request = J("{'id':1,'extra':'e'}");

            Exception undeclared = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, request, J("{'id':1,'extra':'x','zzz':true}")));

            Assert.Contains("$.zzz", undeclared.Message);
        }

        [Fact]
        public async Task AllOfIsOpenWhenNoPartIsClosed()
        {
            const string Schemas = @"
              'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},{'properties':{'extra':{'type':'string'}}}]},
              'Base':{'type':'object','properties':{'id':{'type':'integer'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, J("{'id':1,'x':1}"), J("{'id':3,'extra':'e','unknown':[1]}"));

            IReadOnlyDictionary<string, string> extra = MatrixMode.ExtensionJson(ReadPath(exchange.Result!, "AdditionalProperties"));
            AssertJsonEqual("[1]", extra["unknown"]);
            AssertJsonEqual(J("{'id':1,'x':1}"), exchange.RequestJson);
        }

        [Fact]
        public async Task AllOfWithTypedAdditionalPropertiesKeepsTheValueSchema()
        {
            const string Schemas = @"
              'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},{'additionalProperties':{'type':'integer'}}]},
              'Base':{'type':'object','properties':{'id':{'type':'integer'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            await Echo(execution, J("{'id':1,'a':1}"), J("{'id':1,'a':2}"));
            Exception bad = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("{'id':1}"), J("{'id':1,'a':'x'}")));
            Assert.Contains("$.a", bad.Message);
        }

        [Fact]
        public void AllOfSingleReferenceWithDescriptionIsAReferenceNotANewType()
        {
            const string Schemas = @"
              'Body':{'type':'object','additionalProperties':false,
                'properties':{'pet':{'description':'The favourite pet','allOf':[{'$ref':'#/components/schemas/Item'}]}}},
              'Item':{'type':'object','additionalProperties':false,'properties':{'n':{'type':'integer'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);

            Type body = assembly.GetType(Ns + "Body", true)!;
            Assert.Equal(assembly.GetType(Ns + "Item", true), body.GetProperty("Pet")!.PropertyType);
            Assert.DoesNotContain(assembly.GetTypes(), type => type.Name == "BodyPet");
            Assert.Contains("The favourite pet", execution.GeneratedSource);
        }

        [Fact]
        public async Task AllOfSingleReferenceCanBeNullableInOpenApi30()
        {
            const string Schemas = @"
              'Body':{'type':'object','additionalProperties':false,
                'properties':{'pet':{'nullable':true,'allOf':[{'$ref':'#/components/schemas/Item'}]}}},
              'Item':{'type':'object','additionalProperties':false,'properties':{'n':{'type':'integer'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas, "3.0.3"));

            Exchange exchange = await Echo(execution, J("{'pet':null}"), J("{'pet':null}"));

            Assert.Null(ReadPath(exchange.Result!, "Pet"));
            AssertJsonEqual(J("{'pet':null}"), exchange.RequestJson);
        }

        // ---- allOf part narrowing a property to a subset of an enum ----------------------------------

        private const string ProblemSchemas = @"
          'ProblemCode':{'type':'string','enum':['STORAGE_UNAVAILABLE','NOT_FOUND','BAD_REQUEST']},
          'ProblemDetails':{'type':'object','required':['code'],'properties':{'code':{'$ref':'#/components/schemas/ProblemCode'}}}";

        private static string NarrowedBody(params string[] parts)
        {
            return "'Body':{'allOf':[{'$ref':'#/components/schemas/ProblemDetails'}," + string.Join(",", parts) + "]},";
        }

        private static string CodePart(string codeSchema)
        {
            return "{'type':'object','properties':{'code':" + codeSchema + "}}";
        }

        private static async Task AssertNarrowedValues(
            Phase4GeneratorExecution execution, string[] allowed, string[] rejected)
        {
            foreach (string value in allowed)
            {
                string json = J("{'code':'" + value + "'}");
                Exchange exchange = await Echo(execution, json, json);
                AssertJsonEqual(json, exchange.RequestJson);
            }

            foreach (string value in rejected)
            {
                string json = J("{'code':'" + value + "'}");
                ArgumentException request = await Assert.ThrowsAnyAsync<ArgumentException>(
                    () => Echo(execution, json, J("{'code':'" + allowed[0] + "'}")));
                Assert.Contains("request", request.Message);
                Assert.Contains("$.code", request.Message);
                Assert.DoesNotContain(value, request.Message);

                Exception response = await MatrixMode.ThrowsSerializationAsync(
                    () => Echo(execution, J("{'code':'" + allowed[0] + "'}"), json));
                Assert.Contains("$.code", response.Message);
                Assert.DoesNotContain(value, response.Message);
            }
        }

        [Fact]
        public async Task AnInlineEnumNarrowsANamedEnumPropertyOfTheBaseToItsValues()
        {
            string schemas = NarrowedBody(CodePart("{'type':'string','enum':['STORAGE_UNAVAILABLE']}")) + ProblemSchemas;
            Phase4GeneratorExecution execution = Generate(EchoDocument(schemas, "3.0.4"));
            Assembly assembly = Load(execution);

            Assert.Equal(
                assembly.GetType(Ns + "ProblemCode", true),
                assembly.GetType(Ns + "Body", true)!.GetProperty("Code")!.PropertyType);
            Assert.DoesNotContain(assembly.GetTypes(), type => type.Name == "BodyCode");
            Assert.Contains("Must be one of: STORAGE_UNAVAILABLE.", execution.GeneratedSource);

            await AssertNarrowedValues(execution, new[] { "STORAGE_UNAVAILABLE" }, new[] { "NOT_FOUND", "BAD_REQUEST" });
        }

        [Fact]
        public async Task ANamedEnumWrittenFirstAndNarrowedLaterKeepsTheNamedType()
        {
            // The narrowing part comes first, the base (with the named enum) after it.
            string schemas =
                "'Body':{'allOf':[" + CodePart("{'type':'string','enum':['NOT_FOUND','BAD_REQUEST']}") +
                ",{'$ref':'#/components/schemas/ProblemDetails'}]}," + ProblemSchemas;
            Phase4GeneratorExecution execution = Generate(EchoDocument(schemas, "3.0.4"));
            Assembly assembly = Load(execution);

            Assert.Equal(
                assembly.GetType(Ns + "ProblemCode", true),
                assembly.GetType(Ns + "Body", true)!.GetProperty("Code")!.PropertyType);
            await AssertNarrowedValues(execution, new[] { "NOT_FOUND", "BAD_REQUEST" }, new[] { "STORAGE_UNAVAILABLE" });
        }

        [Fact]
        public async Task AStringEnumNarrowsAPlainStringProperty()
        {
            const string Schemas = @"
              'Base':{'type':'object','properties':{'code':{'type':'string'}}},
              'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                {'type':'object','properties':{'code':{'type':'string','enum':['alpha','bravo']}}}]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas, "3.0.3"));

            Assert.Equal(
                typeof(string),
                Load(execution).GetType(Ns + "Body", true)!.GetProperty("Code")!.PropertyType);
            await AssertNarrowedValues(execution, new[] { "alpha", "bravo" }, new[] { "charlie" });
        }

        [Fact]
        public async Task AnIntegerEnumNarrowsAPlainIntegerProperty()
        {
            const string Schemas = @"
              'Base':{'type':'object','properties':{'level':{'type':'integer'}}},
              'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                {'type':'object','properties':{'level':{'type':'integer','enum':[1,2]}}}]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas, "3.0.3"));

            Exchange ok = await Echo(execution, J("{'level':2}"), J("{'level':1}"));
            AssertJsonEqual(J("{'level':2}"), ok.RequestJson);
            ArgumentException request = await Assert.ThrowsAnyAsync<ArgumentException>(
                () => Echo(execution, J("{'level':3}"), J("{'level':1}")));
            Assert.Contains("$.level", request.Message);
            Exception response = await MatrixMode.ThrowsSerializationAsync(
                () => Echo(execution, J("{'level':1}"), J("{'level':3}")));
            Assert.Contains("$.level", response.Message);
        }

        [Fact]
        public async Task TwoInlineEnumsKeepTheWiderOneAndConstrainToTheNarrowerOne()
        {
            const string Schemas = @"
              'Base':{'type':'object','properties':{'code':{'type':'string','enum':['alpha','bravo','charlie']}}},
              'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                {'type':'object','properties':{'code':{'type':'string','enum':['bravo']}}}]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas, "3.0.3"));

            await AssertNarrowedValues(execution, new[] { "bravo" }, new[] { "alpha", "charlie" });
        }

        [Fact]
        public async Task SeveralNarrowingsStackAndEveryOneOfThemHolds()
        {
            string schemas = NarrowedBody(
                CodePart("{'type':'string','enum':['STORAGE_UNAVAILABLE','NOT_FOUND']}"),
                CodePart("{'type':'string','enum':['NOT_FOUND','BAD_REQUEST']}")) + ProblemSchemas;
            Phase4GeneratorExecution execution = Generate(EchoDocument(schemas, "3.0.4"));

            await AssertNarrowedValues(execution, new[] { "NOT_FOUND" }, new[] { "STORAGE_UNAVAILABLE", "BAD_REQUEST" });
        }

        [Fact]
        public async Task TheSameNarrowingDeclaredByTwoPartsIsOneNarrowing()
        {
            string schemas = NarrowedBody(
                CodePart("{'type':'string','enum':['NOT_FOUND']}"),
                CodePart("{'type':'string','enum':['NOT_FOUND']}")) + ProblemSchemas;
            Phase4GeneratorExecution execution = Generate(EchoDocument(schemas, "3.0.4"));

            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(
                execution.GeneratedSource, "Must be one of: NOT_FOUND").Count);
            await AssertNarrowedValues(execution, new[] { "NOT_FOUND" }, new[] { "BAD_REQUEST" });
        }

        [Theory]
        [InlineData("{'type':'string','enum':['STORAGE_UNAVAILABLE','UNKNOWN']}")]
        [InlineData("{'type':'integer','enum':[1]}")]
        [InlineData("{'type':'string','nullable':true,'enum':['NOT_FOUND']}")]
        [InlineData("{'type':'string','enum':['NOT_FOUND'],'maxLength':20}")]
        [InlineData("{'type':'boolean'}")]
        public void ADeclarationThatIsNotANarrowingStillReportsOacg101(string codeSchema)
        {
            string schemas = NarrowedBody(CodePart(codeSchema)) + ProblemSchemas;

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(schemas, "3.0.4")).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("different types", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/allOf/1/properties/code'", diagnostic.GetMessage());
        }

        [Fact]
        public void ARecursiveReferenceThatCannotBeResolvedYetIsNotNarrowed()
        {
            // The property of the base refers back to the schema that is being parsed.
            const string Schemas = @"
              'Base':{'type':'object','properties':{'next':{'$ref':'#/components/schemas/Body'}}},
              'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                {'type':'object','properties':{'next':{'type':'string','enum':['alpha']}}}]}";

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas, "3.0.3")).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("different types", diagnostic.GetMessage());
        }

        [Fact]
        public void ADiscriminatorMappingValueOutsideTheNarrowedValuesReportsOacg101()
        {
            const string Schemas = @"
              'Animal':{'type':'object','required':['kind'],'properties':{'kind':{'type':'string'}},
                'discriminator':{'propertyName':'kind','mapping':{'cat':'#/components/schemas/Cat'}}},
              'Cat':{'allOf':[{'$ref':'#/components/schemas/Animal'},
                {'type':'object','properties':{'kind':{'type':'string','enum':['feline']}}}]},
              'Body':{'$ref':'#/components/schemas/Animal'}";

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas, "3.0.3")).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("discriminator", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Cat/allOf/1/properties/kind'", diagnostic.GetMessage());
            Assert.DoesNotContain("feline", diagnostic.GetMessage());
        }

        [Fact]
        public void ADiscriminatorMappingValueInsideTheNarrowedValuesIsAccepted()
        {
            const string Schemas = @"
              'Animal':{'type':'object','required':['kind'],'properties':{'kind':{'type':'string'}},
                'discriminator':{'propertyName':'kind','mapping':{'cat':'#/components/schemas/Cat'}}},
              'Cat':{'allOf':[{'$ref':'#/components/schemas/Animal'},
                {'type':'object','properties':{'kind':{'type':'string','enum':['cat','lion']}}}]},
              'Body':{'$ref':'#/components/schemas/Animal'}";

            Generate(EchoDocument(Schemas, "3.0.3"));
        }

        [Fact]
        public async Task AnOpenApi30DocumentWithRefSiblingsNarrowedEnumsAndNullMembersGeneratesAndRoundTrips()
        {
            // The three shapes of rebeat-jp/api-docs GameAPI.openapi.yaml in one document: a $ref next to a
            // type (base), the base property narrowed by an allOf part, and "X or null" written with
            // anyOf and { nullable: true, enum: [null] }.
            const string Schemas = @"
              'ProblemCode':{'type':'string','enum':['STORAGE_UNAVAILABLE','NOT_FOUND']},
              'Limit':{'type':'object','additionalProperties':false,'properties':{'n':{'type':'integer'}}},
              'ProblemDetails':{'type':'object','required':['code'],
                'properties':{'code':{'$ref':'#/components/schemas/ProblemCode','type':'string'}}},
              'Body':{'allOf':[{'$ref':'#/components/schemas/ProblemDetails'},
                {'type':'object','properties':{
                  'code':{'type':'string','enum':['STORAGE_UNAVAILABLE']},
                  'limit':{'anyOf':[{'$ref':'#/components/schemas/Limit'},
                                    {'type':'object','nullable':true,'enum':[null]}]}}}]}";
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(EchoDocument(Schemas, "3.0.4"));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Contains("/components/schemas/ProblemDetails/properties/code/type'", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = Load(execution);
            Type body = assembly.GetType(Ns + "Body", true)!;
            Assert.Equal(assembly.GetType(Ns + "ProblemCode", true), body.GetProperty("Code")!.PropertyType);
            Assert.Equal(assembly.GetType(Ns + "Limit", true), body.GetProperty("Limit")!.PropertyType);

            Exchange nullLimit = await Echo(
                execution,
                J("{'code':'STORAGE_UNAVAILABLE','limit':null}"),
                J("{'code':'STORAGE_UNAVAILABLE','limit':null}"));
            Assert.Null(ReadPath(nullLimit.Result!, "Limit"));
            AssertJsonEqual(J("{'code':'STORAGE_UNAVAILABLE','limit':null}"), nullLimit.RequestJson);

            Exchange withLimit = await Echo(
                execution,
                J("{'code':'STORAGE_UNAVAILABLE','limit':{'n':1}}"),
                J("{'code':'STORAGE_UNAVAILABLE','limit':{'n':2}}"));
            Assert.Equal(2, ReadPath(withLimit.Result!, "Limit", "N"));

            await Assert.ThrowsAnyAsync<ArgumentException>(() => Echo(
                execution, J("{'code':'NOT_FOUND'}"), J("{'code':'STORAGE_UNAVAILABLE'}")));
            await MatrixMode.ThrowsSerializationAsync(() => Echo(
                execution, J("{'code':'STORAGE_UNAVAILABLE'}"), J("{'code':'NOT_FOUND'}")));
        }

        // ---- OpenAPI 3.0 "X or null": { nullable: true, enum: [null] } ------------------------------

        private const string LimitSchema =
            "'Limit':{'type':'object','additionalProperties':false,'properties':{'n':{'type':'integer'}}}";

        [Theory]
        [InlineData("anyOf", "{'type':'object','nullable':true,'enum':[null]}")]
        [InlineData("oneOf", "{'type':'object','nullable':true,'enum':[null]}")]
        [InlineData("anyOf", "{'nullable':true,'enum':[null]}")]
        [InlineData("anyOf", "{'description':'No limit','type':'object','nullable':true,'enum':[null],'x-note':1}")]
        public async Task ANullableNullEnumMemberMakesTheOtherMemberNullableInOpenApi30(string keyword, string nullPart)
        {
            string schemas =
                "'Body':{'type':'object','additionalProperties':false,'properties':{'limit':{'" + keyword +
                "':[{'$ref':'#/components/schemas/Limit'}," + nullPart + "]}}}," + LimitSchema;
            Phase4GeneratorExecution execution = Generate(EchoDocument(schemas, "3.0.4"));
            Assembly assembly = Load(execution);

            Assert.Equal(
                assembly.GetType(Ns + "Limit", true),
                assembly.GetType(Ns + "Body", true)!.GetProperty("Limit")!.PropertyType);
            Assert.DoesNotContain(assembly.GetTypes(), type => type.Name.StartsWith("BodyLimit", StringComparison.Ordinal));

            Exchange nullExchange = await Echo(execution, J("{'limit':null}"), J("{'limit':null}"));
            Assert.Null(ReadPath(nullExchange.Result!, "Limit"));
            AssertJsonEqual(J("{'limit':null}"), nullExchange.RequestJson);

            Exchange valueExchange = await Echo(execution, J("{'limit':{'n':3}}"), J("{'limit':{'n':4}}"));
            Assert.Equal(4, ReadPath(valueExchange.Result!, "Limit", "N"));
            AssertJsonEqual(J("{'limit':{'n':3}}"), valueExchange.RequestJson);
        }

        [Fact]
        public async Task ANullableNullEnumMemberNextToTwoSchemasMakesTheUnionNullableInOpenApi30()
        {
            string schemas =
                "'Body':{'type':'object','additionalProperties':false,'properties':{'pet':{'anyOf':[" +
                "{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}," +
                "{'type':'object','nullable':true,'enum':[null]}]}}}," + CatDogUndeclared;
            Phase4GeneratorExecution execution = Generate(EchoDocument(schemas, "3.0.3"));

            Exchange nullExchange = await Echo(execution, J("{'pet':null}"), J("{'pet':null}"));
            Assert.Null(ReadPath(nullExchange.Result!, "Pet"));
            AssertJsonEqual(J("{'pet':null}"), nullExchange.RequestJson);

            Exchange cat = await Echo(execution, J("{'pet':{'meow':true}}"), J("{'pet':{'meow':false}}"));
            Assert.True(TryGet(ReadPath(cat.Result!, "Pet")!, "TryGetCat", out _));
        }

        [Fact]
        public async Task ANullableNullEnumMemberCanBeAddedToADiscriminatedUnionInOpenApi30()
        {
            string schemas =
                "'Pet':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}," +
                "{'type':'object','nullable':true,'enum':[null]}]," +
                "'discriminator':{'propertyName':'petType','mapping':{" +
                "'cat':'#/components/schemas/Cat','dog':'#/components/schemas/Dog'}}}," +
                "'Body':{'$ref':'#/components/schemas/Pet'}," + CatDogUndeclared;
            Phase4GeneratorExecution execution = Generate(EchoDocument(schemas, "3.0.3"));

            Exchange cat = await Echo(execution, J("{'petType':'cat','meow':true}"), J("{'petType':'cat','meow':false}"));
            AssertJsonEqual(J("{'petType':'cat','meow':true}"), cat.RequestJson);
            Assert.True(TryGet(cat.Result!, "TryGetCat", out _));
            Assert.False(TryGet(cat.Result!, "TryGetDog", out _));
        }

        [Fact]
        public void ANullOnlyEnumIsStillRejectedInOpenApi31()
        {
            string schemas =
                "'Body':{'type':'object','properties':{'limit':{'anyOf':[{'$ref':'#/components/schemas/Limit'}," +
                "{'type':'object','enum':[null]}]}}}," + LimitSchema;

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(schemas, "3.1.0")).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("only null", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("{'type':'object','nullable':true,'enum':[null],'properties':{'a':{'type':'string'}}}")]
        [InlineData("{'type':'object','nullable':true,'enum':[null,'x']}")]
        [InlineData("{'type':'object','enum':[null]}")]
        public void OtherShapesOfANullOnlyEnumAreStillRejectedInOpenApi30(string nullPart)
        {
            string schemas =
                "'Body':{'type':'object','properties':{'limit':{'anyOf':[{'$ref':'#/components/schemas/Limit'}," +
                nullPart + "]}}}," + LimitSchema;

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(schemas, "3.0.3")).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
        }

        [Fact]
        public async Task RefSiblingsAreIgnoredWithOacg108InOpenApi30AndNullableIsKept()
        {
            const string Schemas = @"
              'Body':{'type':'object','additionalProperties':false,
                'properties':{'code':{'$ref':'#/components/schemas/Code','type':'string','nullable':true}}},
              'Code':{'type':'string','enum':['a','b']}";
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(EchoDocument(Schemas, "3.0.4"));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("logical path '/components/schemas/Body/properties/code/type'", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = Load(execution);
            Assert.Equal(
                assembly.GetType(Ns + "Code", true),
                Nullable.GetUnderlyingType(assembly.GetType(Ns + "Body", true)!.GetProperty("Code")!.PropertyType) ??
                assembly.GetType(Ns + "Body", true)!.GetProperty("Code")!.PropertyType);

            Exchange exchange = await Echo(execution, J("{'code':null}"), J("{'code':null}"));
            Assert.Null(ReadPath(exchange.Result!, "Code"));
            AssertJsonEqual(J("{'code':null}"), exchange.RequestJson);
        }

        [Fact]
        public void AnAllOfPartWithARefAndATypeIsIgnoredWithOacg108InOpenApi30()
        {
            const string Schemas = @"
              'Body':{'allOf':[{'$ref':'#/components/schemas/Base','type':'string'},
                               {'type':'object','properties':{'extra':{'type':'integer'}}}]},
              'Base':{'type':'object','properties':{'name':{'type':'string'}}}";
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(EchoDocument(Schemas, "3.0.3"));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Contains("logical path '/components/schemas/Body/allOf/0/type'", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
            Type body = Load(execution).GetType(Ns + "Body", true)!;
            Assert.NotNull(body.GetProperty("Name"));
            Assert.NotNull(body.GetProperty("Extra"));
        }

        [Fact]
        public void ASingleElementAllOfWithARefAndATypeIsAReferenceInOpenApi30()
        {
            const string Schemas = @"
              'Body':{'type':'object','additionalProperties':false,
                'properties':{'pet':{'allOf':[{'$ref':'#/components/schemas/Item','type':'string'}]}}},
              'Item':{'type':'object','additionalProperties':false,'properties':{'n':{'type':'integer'}}}";
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(EchoDocument(Schemas, "3.0.3"));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Contains("logical path '/components/schemas/Body/properties/pet/allOf/0/type'", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = Load(execution);
            Assert.Equal(
                assembly.GetType(Ns + "Item", true),
                assembly.GetType(Ns + "Body", true)!.GetProperty("Pet")!.PropertyType);
        }

        [Fact]
        public void ConstAndUnknownKeywordsNextToARefAreIgnoredWithOacg108InOpenApi30()
        {
            const string Schemas = @"
              'Body':{'type':'object','additionalProperties':false,
                'properties':{'item':{'$ref':'#/components/schemas/Item','const':'x','somethingNew':true,'maxLength':1,'x-note':1}}},
              'Item':{'type':'object','additionalProperties':false,'properties':{'n':{'type':'integer'}}}";
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(EchoDocument(Schemas, "3.0.3"));

            Assert.Equal(
                new[] { "OACG108", "OACG108", "OACG108" },
                execution.RunResult.Diagnostics.Select(static item => item.Id).ToArray());
            string messages = string.Join("\n", execution.RunResult.Diagnostics.Select(static item => item.GetMessage()));
            Assert.Contains("/properties/item/const'", messages);
            Assert.Contains("/properties/item/somethingNew'", messages);
            Assert.Contains("/properties/item/maxLength'", messages);
            Assert.DoesNotContain("x-note", messages);
            Assert.Empty(execution.CompilationErrors);
            Assert.DoesNotContain("__OacgConstraint_", execution.GeneratedSource);
        }

        [Fact]
        public void AnUnknownKeywordNextToARefStillFailsInOpenApi31()
        {
            const string Schemas = @"
              'Body':{'type':'object','properties':{'item':{'$ref':'#/components/schemas/Item','somethingNew':true}}},
              'Item':{'type':'object','properties':{'n':{'type':'integer'}}}";

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas, "3.1.0")).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("/properties/item/somethingNew'", diagnostic.GetMessage());
        }

        [Fact]
        public void AllOfPartsWithDifferentPropertyTypesReportOacg101AtTheSecondDeclaration()
        {
            const string Schemas = @"
              'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                               {'type':'object','properties':{'name':{'type':'integer'}}}]},
              'Base':{'type':'object','properties':{'name':{'type':'string'}}}";

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("different types", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/allOf/1/properties/name'", diagnostic.GetMessage());
        }

        [Fact]
        public async Task AllOfPartsThatAgreeOnAPropertyTypeAreMerged()
        {
            const string Schemas = @"
              'Body':{'allOf':[{'$ref':'#/components/schemas/A'},{'$ref':'#/components/schemas/B'}]},
              'A':{'type':'object','required':['id'],'properties':{'id':{'type':'integer'},'a':{'type':'string'}}},
              'B':{'type':'object','properties':{'id':{'type':'integer'},'b':{'type':'string'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, J("{'id':1,'a':'x','b':'y'}"), J("{'id':2}"));

            Assert.Equal(2, ReadPath(exchange.Result!, "Id"));
        }

        [Fact]
        public void AllOfCycleReportsOacg103()
        {
            const string Schemas = @"
              'Body':{'allOf':[{'$ref':'#/components/schemas/Other'},{'properties':{'x':{'type':'string'}}}]},
              'Other':{'allOf':[{'$ref':'#/components/schemas/Body'},{'properties':{'y':{'type':'string'}}}]}";

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG103", diagnostic.Id);
            Assert.Contains("cyclic internal $ref", diagnostic.GetMessage());
        }

        [Fact]
        public void AllOfPartThatIsNotAnObjectReportsOacg101()
        {
            const string Schemas = @"
              'Body':{'allOf':[{'type':'string'},{'properties':{'x':{'type':'string'}}}]}";

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Body/allOf/0/type'", diagnostic.GetMessage());
        }

        [Fact]
        public void AllOfRequiredPropertyThatNoPartDeclaresReportsOacg100()
        {
            const string Schemas = @"
              'Body':{'allOf':[{'properties':{'x':{'type':'string'}}},{'required':['ghost']}]}";

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("'ghost'", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/allOf/1/required/0'", diagnostic.GetMessage());
        }

        // ---- G08 oneOf / anyOf without a discriminator -----------------------------------------------

        [Fact]
        public async Task NullableIdiomNormalizesToANullableType()
        {
            const string Schemas = @"
              'Body':{'type':'object','additionalProperties':false,'properties':{
                  'item':{'oneOf':[{'$ref':'#/components/schemas/Item'},{'type':'null'}]},
                  'count':{'anyOf':[{'type':'integer'},{'type':'null'}]},
                  'label':{'oneOf':[{'type':'string'},{'type':'null'}],'description':'Text'}}},
              'Item':{'type':'object','additionalProperties':false,'properties':{'n':{'type':'integer'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);
            Type body = assembly.GetType(Ns + "Body", true)!;

            Assert.Equal(assembly.GetType(Ns + "Item", true), body.GetProperty("Item")!.PropertyType);
            Assert.Equal(typeof(int?), body.GetProperty("Count")!.PropertyType);
            Assert.Equal(typeof(string), body.GetProperty("Label")!.PropertyType);
            Assert.DoesNotContain(assembly.GetTypes(), type => type.Name.Contains("Option"));
            Exchange exchange = await Echo(
                execution,
                J("{'item':null,'count':null,'label':null}"),
                J("{'item':{'n':1},'count':5,'label':'t'}"));
            Assert.Equal(5, ReadPath(exchange.Result!, "Count"));
            AssertJsonEqual(J("{'item':null,'count':null,'label':null}"), exchange.RequestJson);
            Exchange nulls = await Echo(execution, J("{}"), J("{'item':null,'count':null,'label':null}"));
            Assert.Null(ReadPath(nulls.Result!, "Item"));
        }

        private const string ObjectUnion = @"
          'Body':{'oneOf':[{'$ref':'#/components/schemas/Alpha'},{'$ref':'#/components/schemas/Beta'}]},
          'Alpha':{'type':'object','additionalProperties':false,'required':['a'],'properties':{'a':{'type':'integer'}}},
          'Beta':{'type':'object','additionalProperties':false,'required':['b'],'properties':{'b':{'type':'string'}}}";

        [Fact]
        public async Task OneOfAcceptsExactlyOneMatchingObjectCandidate()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(ObjectUnion));

            Exchange a = await Echo(execution, J("{'a':1}"), J("{'a':7}"));
            Assert.True(TryGet(a.Result!, "TryGetAlpha", out object? aValue));
            Assert.Equal(7, ReadPath(aValue!, "A"));
            Assert.False(TryGet(a.Result!, "TryGetBeta", out _));
            AssertJsonEqual(J("{'a':1}"), a.RequestJson);

            Exchange b = await Echo(execution, J("{'b':'x'}"), J("{'b':'y'}"));
            Assert.True(TryGet(b.Result!, "TryGetBeta", out _));
            Assert.Equal("y", ReadPath(b.Result!.GetType().GetMethod("AsBeta")!.Invoke(b.Result, null)!, "B"));
        }

        [Theory]
        [InlineData("{}", "matched 0")]
        [InlineData("{'a':'not an integer'}", "matched 0")]
        [InlineData("{'a':1,'b':'x'}", "matched 0")]
        [InlineData("[1]", "matched 0")]
        public async Task OneOfRejectsAResponseThatMatchesNoCandidate(string response, string expected)
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(ObjectUnion));

            Exception exception = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("{'a':1}"), J(response)));

            Assert.Contains("exactly one oneOf", exception.Message);
            Assert.Contains(expected, exception.Message);
        }

        [Fact]
        public async Task OneOfRejectsAResponseThatMatchesTwoCandidates()
        {
            const string Schemas = @"
              'Body':{'oneOf':[{'$ref':'#/components/schemas/A'},{'$ref':'#/components/schemas/B'}]},
              'A':{'type':'object','additionalProperties':false,'properties':{'x':{'type':'string'}}},
              'B':{'type':'object','additionalProperties':false,'properties':{'y':{'type':'string'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            // {} satisfies both closed objects, {'x':..} only A.
            Exception both = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("{}"), J("{}")));
            Assert.Contains("matched 2", both.Message);
            Exchange one = await Echo(execution, J("{'x':'s'}"), J("{'x':'s'}"));
            Assert.True(TryGet(one.Result!, "TryGetA", out _));
        }

        [Fact]
        public async Task AnyOfAcceptsOneOrMoreMatchingCandidates()
        {
            const string Schemas = @"
              'Body':{'anyOf':[{'$ref':'#/components/schemas/A'},{'$ref':'#/components/schemas/B'}]},
              'A':{'type':'object','additionalProperties':false,'properties':{'x':{'type':'string'}}},
              'B':{'type':'object','additionalProperties':false,'properties':{'y':{'type':'string'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            // both match: fine for anyOf
            Exchange both = await Echo(execution, J("{}"), J("{}"));
            Assert.True(TryGet(both.Result!, "TryGetA", out _));
            Assert.True(TryGet(both.Result!, "TryGetB", out _));
            Exception none = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("{}"), J("{'z':1}")));
            Assert.Contains("at least one anyOf", none.Message);
        }

        [Fact]
        public async Task PrimitiveCandidatesAreDecidedByTheirJsonType()
        {
            const string Schemas = @"'Body':{'oneOf':[{'type':'string'},{'type':'integer'}]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange text = await Echo(execution, J("'hello'"), J("'abc'"));
            Assert.True(TryGet(text.Result!, "TryGetString", out object? textValue));
            Assert.Equal("abc", textValue);
            Assert.False(TryGet(text.Result!, "TryGetInt32", out _));
            Exchange number = await Echo(execution, J("7"), J("42"));
            Assert.True(TryGet(number.Result!, "TryGetInt32", out object? numberValue));
            Assert.Equal(42, numberValue);
            Assert.Equal("7", number.RequestJson);
            Exception none = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("'x'"), "true"));
            Assert.Contains("matched 0", none.Message);
            await MatrixMode.ThrowsSerializationAsync(() => Echo(execution, J("'x'"), "1.5"));
        }

        [Fact]
        public async Task OneOfOfOverlappingPrimitivesIsRejectedButAnyOfIsNot()
        {
            const string OneOf = @"'Body':{'oneOf':[{'type':'integer'},{'type':'number'}]}";
            const string AnyOf = @"'Body':{'anyOf':[{'type':'integer'},{'type':'number'}]}";

            Phase4GeneratorExecution oneOf = Generate(EchoDocument(OneOf));
            Exception both = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(oneOf, "1", "5"));
            Assert.Contains("matched 2", both.Message);
            Exchange fraction = await Echo(oneOf, "1", "5.5");
            Assert.True(TryGet(fraction.Result!, "TryGetDouble", out _));

            Phase4GeneratorExecution anyOf = Generate(EchoDocument(AnyOf));
            Exchange whole = await Echo(anyOf, "1", "5");
            Assert.True(TryGet(whole.Result!, "TryGetInt32", out _));
            Assert.True(TryGet(whole.Result!, "TryGetDouble", out _));
        }

        [Fact]
        public async Task RequestUnionWritesTheSuppliedCandidateOrRawJson()
        {
            const string Schemas = @"
              'Body':{'oneOf':[{'type':'string'},{'$ref':'#/components/schemas/Alpha'},{'type':'array','items':{'type':'integer'}}]},
              'Alpha':{'type':'object','additionalProperties':false,'required':['a'],'properties':{'a':{'type':'integer'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);
            Type body = assembly.GetType(Ns + "Body", true)!;

            object fromString = body.GetMethod("FromString")!.Invoke(null, new object[] { "hi" })!;
            Assert.Equal("\"hi\"", (await Echo(execution, fromString, "\"x\"")).RequestJson);

            object a = Activator.CreateInstance(assembly.GetType(Ns + "Alpha", true)!)!;
            a.GetType().GetProperty("A")!.SetValue(a, 9);
            object fromA = body.GetMethod("FromAlpha")!.Invoke(null, new[] { a })!;
            AssertJsonEqual(J("{'a':9}"), (await Echo(execution, fromA, J("{'a':1}"))).RequestJson);

            object fromList = body.GetMethod("FromInt32List")!.Invoke(null, new object[] { new List<int> { 1, 2 } })!;
            AssertJsonEqual("[1,2]", (await Echo(execution, fromList, "[3]")).RequestJson);

            object raw = MatrixMode.RawUnion(body, "{\"a\":4}");
            AssertJsonEqual("{\"a\":4}", (await Echo(execution, raw, J("{'a':1}"))).RequestJson);
            Assert.True(TryGet(raw, "TryGetAlpha", out object? parsed));
            Assert.Equal(4, ReadPath(parsed!, "A"));
            Assert.False(TryGet(raw, "TryGetString", out _));
            // a value of another candidate is not readable as this one
            Assert.False(TryGet(fromString, "TryGetAlpha", out _));
            Assert.Throws<TargetInvocationException>(() => fromString.GetType().GetMethod("AsAlpha")!.Invoke(fromString, null));
            // a union round-trips as JSON
            AssertJsonEqual("[1,2]", MatrixMode.RawJson(fromList));
        }

        [Fact]
        public async Task UnionInsideARecursiveStructureRoundTrips()
        {
            const string Schemas = @"
              'Body':{'$ref':'#/components/schemas/Node'},
              'Node':{'oneOf':[{'$ref':'#/components/schemas/Leaf'},{'$ref':'#/components/schemas/Branch'}]},
              'Leaf':{'type':'object','additionalProperties':false,'required':['value'],'properties':{'value':{'type':'integer'}}},
              'Branch':{'type':'object','additionalProperties':false,'required':['children'],
                        'properties':{'children':{'type':'array','items':{'$ref':'#/components/schemas/Node'}}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            const string Tree = "{'children':[{'value':1},{'children':[{'value':2},{'children':[]}]}]}";

            Exchange exchange = await Echo(execution, J(Tree), J(Tree));

            AssertJsonEqual(J(Tree), exchange.RequestJson);
            Assert.True(TryGet(exchange.Result!, "TryGetBranch", out object? branch));
            Assert.Equal(2, ((System.Collections.IList)ReadPath(branch!, "Children")!).Count);
            Exception deep = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J(Tree), J("{'children':[{'children':[{'value':'x'}]}]}")));
            Assert.Contains("matched 0", deep.Message);
        }

        [Fact]
        public async Task UnionThatContainsAnArrayOfItselfIsNotACycleError()
        {
            // The union is a named wrapper, so Node -> List<Node> is a finite type.
            const string Schemas = @"
              'Body':{'$ref':'#/components/schemas/Node'},
              'Node':{'oneOf':[{'type':'integer'},{'type':'array','items':{'$ref':'#/components/schemas/Node'}}]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, "[1,[2,[3]]]", "[4,[5]]");

            AssertJsonEqual("[1,[2,[3]]]", exchange.RequestJson);
            Assert.True(TryGet(exchange.Result!, "TryGetNodeList", out object? list));
            Assert.Equal(2, ((System.Collections.IList)list!).Count);
            await MatrixMode.ThrowsSerializationAsync(() => Echo(execution, "1", "[1,\"x\"]"));
        }

        [Fact]
        public async Task InlineUnionThatRefersBackToItsArrayIsNotACycleError()
        {
            const string Schemas = @"
              'Body':{'type':'array','items':{'oneOf':[{'type':'string'},{'$ref':'#/components/schemas/Body'}]}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, J("['a',['b',['c']]]"), J("['x',[]]"));

            AssertJsonEqual(J("['a',['b',['c']]]"), exchange.RequestJson);
            Assert.Equal(2, ((System.Collections.IList)exchange.Result!).Count);
        }

        [Fact]
        public void UnionAndConverterNamesAvoidCollisionsWithSchemaNames()
        {
            // An object that already owns the name of the converter forces a different converter name.
            const string Schemas = @"
              'Body':{'oneOf':[{'type':'string'},{'type':'integer'}]},
              'BodyJsonConverter':{'type':'object','properties':{'x':{'type':'string'}}}";

            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);

            Assert.True(assembly.GetType(Ns + "BodyJsonConverter", true)!.IsPublic);
            Assert.Contains("class BodyJsonConverter2", execution.GeneratedSource);
        }

        [Fact]
        public void MappingAndSubtypesResolveInsideAMultiDocumentBundle()
        {
            // v2 bundles carry a reference edge per $ref node. Mapping values and allOf subtypes are
            // not $ref nodes, so they are resolved inside the document without an edge.
            const string Schemas = @"
              'Body':{'$ref':'#/components/schemas/Pet'},
              'Pet':{'type':'object','required':['petType'],
                     'discriminator':{'propertyName':'petType','mapping':{'cat':'#/components/schemas/Cat','dog':'Dog'}},
                     'properties':{'petType':{'type':'string'}}},
              'Cat':{'allOf':[{'$ref':'#/components/schemas/Pet'},{'properties':{'meow':{'type':'boolean'}}}]},
              'Dog':{'allOf':[{'$ref':'#/components/schemas/Pet'},{'properties':{'bark':{'type':'string'}}}]},
              'Choice':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                        'discriminator':{'propertyName':'petType','mapping':{'c':'#/components/schemas/Cat'}}}";
            string root = EchoDocument(Schemas);
            string bundle = TestBundleFactory.CreateV2(
                root,
                Array.Empty<TestBundleFactory.V2DocumentSpec>(),
                InternalReferenceEdges(root));

            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(root, bundle: bundle);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("public static Pet FromCat(Cat value)", execution.GeneratedSource);
            Assert.Contains("public static Choice FromDog(Dog value)", execution.GeneratedSource);
        }

        [Fact]
        public async Task NestedUnionPropertyAndArrayOfUnionsAreValidatedPerElement()
        {
            const string Schemas = @"
              'Body':{'type':'object','additionalProperties':false,'properties':{
                  'values':{'type':'array','items':{'oneOf':[{'type':'string'},{'type':'integer'}]}}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange exchange = await Echo(execution, J("{'values':['a',1]}"), J("{'values':['b',2,'c']}"));

            Assert.Equal(3, ((System.Collections.IList)ReadPath(exchange.Result!, "Values")!).Count);
            AssertJsonEqual(J("{'values':['a',1]}"), exchange.RequestJson);
            Exception bad = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("{}"), J("{'values':['a',true]}")));
            Assert.Contains("$.values[1]", bad.Message);
        }

        // ---- G09 3.1 $ref + siblings --------------------------------------------------------------------

        [Fact]
        public async Task RefWithPropertySiblingsIsComposedAsAllOf()
        {
            const string Schemas = @"
              'Body':{'$ref':'#/components/schemas/Base','properties':{'extra':{'type':'string'}},'required':['extra']},
              'Base':{'type':'object','additionalProperties':false,'required':['id'],'properties':{'id':{'type':'integer'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Type body = Load(execution).GetType(Ns + "Body", true)!;
            Assert.NotNull(body.GetProperty("Id"));
            Assert.NotNull(body.GetProperty("Extra"));

            Exchange exchange = await Echo(execution, J("{'id':1,'extra':'e'}"), J("{'id':2,'extra':'x'}"));

            Assert.Equal("x", ReadPath(exchange.Result!, "Extra"));
            Exception missing = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("{'id':1,'extra':'e'}"), J("{'id':2}")));
            Assert.Contains("$.extra", missing.Message);
            // Base is closed, so the composed object is closed
            await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("{'id':1,'extra':'e'}"), J("{'id':2,'extra':'x','zzz':1}")));
        }

        [Fact]
        public void RefWithOnlyAnnotationSiblingsStaysAReference()
        {
            const string Schemas = @"
              'Body':{'$ref':'#/components/schemas/Base','description':'d','type':'object'},
              'Base':{'type':'object','additionalProperties':false,'properties':{'id':{'type':'integer'}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            Assembly assembly = Load(execution);

            MethodInfo echo = assembly.GetType(Ns + "Phase4Api", true)!.GetMethod("echo")!;
            Assert.Equal(
                assembly.GetType(Ns + "Base", true),
                echo.ReturnType.GetGenericArguments()[0]);
            Assert.Null(assembly.GetType(Ns + "Body"));
        }

        // ---- not -------------------------------------------------------------------------------------

        [Fact]
        public async Task NotRejectsAResponseThatMatchesTheNotSchema()
        {
            const string Schemas = @"'Body':{'type':'string','not':{'enum':['bad','worse']}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange ok = await Echo(execution, "\"x\"", "\"fine\"");
            Assert.Equal("fine", ok.Result);
            Exception rejected = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, "\"x\"", "\"worse\""));
            Assert.Contains("must not match", rejected.Message);
            // The generated type is not affected: still a plain string.
            Assert.Equal(
                typeof(string),
                Load(execution).GetType(Ns + "Phase4Api", true)!.GetMethod("echo")!.ReturnType.GetGenericArguments()[0]);
        }

        [Fact]
        public async Task NotOnAnUntypedSchemaRejectsValuesOfTheNotType()
        {
            const string Schemas = @"'Body':{'not':{'type':'string'}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange number = await Echo(execution, "5", "5");
            AssertJsonEqual("5", MatrixMode.AnyJson(number.Result));
            await MatrixMode.ThrowsSerializationAsync(() => Echo(execution, "5", "\"text\""));
        }

        [Fact]
        public async Task NotOnADtoPropertyAndOnAReferenceIsEvaluatedPerValue()
        {
            const string Schemas = @"
              'Body':{'type':'object','additionalProperties':false,'properties':{
                  'a':{'type':'integer','not':{'const':0}},
                  'b':{'$ref':'#/components/schemas/Item','not':{'$ref':'#/components/schemas/Forbidden'}}}},
              'Item':{'type':'object','properties':{'n':{'type':'integer'},'kind':{'type':'string'}}},
              'Forbidden':{'type':'object','required':['kind'],'properties':{'kind':{'enum':['x']}}}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            string request = J("{'a':1}");

            await Echo(execution, request, J("{'a':1,'b':{'n':1,'kind':'y'}}"));
            Exception zero = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, request, J("{'a':0}")));
            Assert.Contains("$.a", zero.Message);
            // matches Forbidden (kind is the declared enum value) -> rejected; kind 'y' does not match it
            Exception forbidden = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, request, J("{'a':1,'b':{'kind':'x'}}")));
            Assert.Contains("$.b", forbidden.Message);
        }

        [Fact]
        public void NotThatCannotBeEvaluatedIsIgnoredWithOacg108()
        {
            // required alone has no shape: as a schema it matches every value, so it must not reject all.
            const string Schemas = @"'Body':{'type':'object','properties':{'x':{'type':'string'}},'not':{'required':['x']}}";

            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(EchoDocument(Schemas));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("logical path '/components/schemas/Body/not'", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void RecursiveNotIsIgnoredWithOacg108()
        {
            const string Schemas = @"'Body':{'type':'object','properties':{'x':{'type':'string'}},
              'not':{'$ref':'#/components/schemas/Body'}}";

            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(EchoDocument(Schemas));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Contains("recursive", warning.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/not'", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
        }

        // ---- diagnostics for out of scope and invalid composition -----------------------------------

        [Fact]
        public void MappingToAnotherFileReportsOacg101AtTheMappingValue()
        {
            const string Schemas = @"
              'Body':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                      'discriminator':{'propertyName':'petType','mapping':{'cat':'other.yaml#/Cat'}}}," + CatDogUndeclared;

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("outside the current document", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/discriminator/mapping/cat'", diagnostic.GetMessage());
        }

        [Fact]
        public void MappingToASchemaThatIsNotAMemberReportsOacg101()
        {
            const string Schemas = @"
              'Body':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                      'discriminator':{'propertyName':'petType','mapping':{'bird':'#/components/schemas/Bird'}}},
              'Bird':{'type':'object','properties':{'wings':{'type':'integer'}}}," + CatDogUndeclared;

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("does not refer to a member", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/discriminator/mapping/bird'", diagnostic.GetMessage());
        }

        [Fact]
        public void DiscriminatedMembersThatAreNotObjectsReportOacg101AtTheMember()
        {
            const string Schemas = @"
              'Body':{'oneOf':[{'$ref':'#/components/schemas/Name'},{'$ref':'#/components/schemas/Dog'}],
                      'discriminator':{'propertyName':'petType'}},
              'Name':{'type':'string'}," + CatDogUndeclared;

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("must be an object schema", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/oneOf/0'", diagnostic.GetMessage());
        }

        [Fact]
        public void DiscriminatedInlineMembersReportOacg101()
        {
            const string Schemas = @"
              'Body':{'oneOf':[{'type':'object','properties':{'a':{'type':'string'}}},{'$ref':'#/components/schemas/Dog'}],
                      'discriminator':{'propertyName':'petType'}}," + CatDogUndeclared;

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("must be $ref schemas", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/oneOf/0'", diagnostic.GetMessage());
        }

        [Fact]
        public void DuplicateDiscriminatorValuesReportOacg100()
        {
            const string Schemas = @"
              'Body':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                      'discriminator':{'propertyName':'petType','mapping':{'same':'Cat'}}}," + CatDogUndeclared;
            // Dog's implicit value is 'Dog' and Cat maps to 'same': no clash here
            Generate(EchoDocument(Schemas));

            const string Clash = @"
              'Body':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                      'discriminator':{'propertyName':'petType','mapping':{'Dog':'Cat'}}}," + CatDogUndeclared;
            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Clash)).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("'Dog' selects more than one schema", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/discriminator'", diagnostic.GetMessage());
        }

        [Fact]
        public void UnionKeywordsThatCannotBeCombinedReportTheirPosition()
        {
            Diagnostic properties = Assert.Single(MatrixMode.GenerateAndCompile(EchoDocument(
                "'Body':{'oneOf':[{'type':'string'},{'type':'integer'}],'properties':{'x':{'type':'string'}}}"))
                .RunResult.Diagnostics);
            Assert.Equal("OACG101", properties.Id);
            Assert.Contains("logical path '/components/schemas/Body/properties'", properties.GetMessage());

            Diagnostic allOf = Assert.Single(MatrixMode.GenerateAndCompile(EchoDocument(
                "'Body':{'oneOf':[{'type':'string'},{'type':'integer'}],'allOf':[{'type':'object'}]}"))
                .RunResult.Diagnostics);
            Assert.Equal("OACG101", allOf.Id);
            Assert.Contains("logical path '/components/schemas/Body/allOf'", allOf.GetMessage());

            Diagnostic both = Assert.Single(MatrixMode.GenerateAndCompile(EchoDocument(
                "'Body':{'oneOf':[{'type':'string'},{'type':'integer'}],'anyOf':[{'type':'string'}]}"))
                .RunResult.Diagnostics);
            Assert.Equal("OACG101", both.Id);
            Assert.Contains("logical path '/components/schemas/Body/anyOf'", both.GetMessage());

            Diagnostic inAllOf = Assert.Single(MatrixMode.GenerateAndCompile(EchoDocument(
                "'Body':{'allOf':[{'oneOf':[{'type':'string'},{'type':'integer'}]},{'properties':{'x':{'type':'string'}}}]}"))
                .RunResult.Diagnostics);
            Assert.Equal("OACG101", inAllOf.Id);
            Assert.Contains("logical path '/components/schemas/Body/allOf/0/oneOf'", inAllOf.GetMessage());
        }

        [Theory]
        [InlineData("'Body':{'oneOf':[]}", "OACG100", "/components/schemas/Body/oneOf")]
        [InlineData("'Body':{'oneOf':[{'type':'null'}]}", "OACG101", "/components/schemas/Body/oneOf")]
        [InlineData("'Body':{'allOf':[]}", "OACG100", "/components/schemas/Body/allOf")]
        [InlineData("'Body':{'oneOf':{}}", "OACG100", "/components/schemas/Body/oneOf")]
        public void InvalidCompositionListsReportTheirPosition(string schemas, string id, string logicalPath)
        {
            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(J(schemas))).RunResult.Diagnostics);

            Assert.Equal(id, diagnostic.Id);
            Assert.Contains("logical path '" + logicalPath + "'", diagnostic.GetMessage());
        }

        [Fact]
        public void UnionParametersRemainUnsupported()
        {
            string document = J(@"{'openapi':'3.1.0','info':{'title':'P','version':'1'},
              'paths':{'/p':{'get':{'operationId':'probe',
                'parameters':[{'name':'q','in':'query','schema':{'oneOf':[{'type':'string'},{'type':'integer'}]}}],
                'responses':{'204':{'description':'none'}}}}}}");

            Diagnostic diagnostic = Assert.Single(
                MatrixMode.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/paths/~1p/get/parameters/0'", diagnostic.GetMessage());
        }

        [Fact]
        public void NullableIdiomWorksForParameters()
        {
            string document = J(@"{'openapi':'3.1.0','info':{'title':'P','version':'1'},
              'paths':{'/p':{'get':{'operationId':'probe',
                'parameters':[{'name':'q','in':'query','schema':{'oneOf':[{'type':'string'},{'type':'null'}]}}],
                'responses':{'204':{'description':'none'}}}}}}");

            Generate(document);
        }

        [Fact]
        public void DiscriminatorOnASchemaWithoutSubtypesIsIgnoredWithOacg108()
        {
            const string Schemas = @"
              'Body':{'type':'object','properties':{'kind':{'type':'string'}},'discriminator':{'propertyName':'kind'}}";

            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(EchoDocument(Schemas));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Contains("logical path '/components/schemas/Body/discriminator'", warning.GetMessage());
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void SuccessResponsesWithDifferentUnionContractsReportOacg105()
        {
            string Document(string created) => J(@"{'openapi':'3.1.0','info':{'title':'R','version':'1'},
              'paths':{'/p':{'get':{'operationId':'probe','responses':{
                '200':{'description':'ok','content':{'application/json':{'schema':{'oneOf':[{'type':'string'},{'type':'integer'}]}}}},
                '201':{'description':'created','content':{'application/json':{'schema':" + created + @"}}}}}}}}");

            Generate(Document("{'oneOf':[{'type':'integer'},{'type':'string'}]}"));

            // P4 (G23): different union contracts get a result wrapper (was OACG105).
            foreach (string created in new[]
            {
                "{'anyOf':[{'type':'string'},{'type':'integer'}]}",
                "{'oneOf':[{'type':'string'},{'type':'boolean'}]}"
            })
            {
                Phase4GeneratorExecution wrapped = MatrixMode.GenerateAndCompile(Document(created));
                Assert.Empty(wrapped.RunResult.Diagnostics);
                Assert.Empty(wrapped.CompilationErrors);
                Assert.Contains("Status201", wrapped.GeneratedSource);
            }
        }

        [Fact]
        public void MapAnyAndFreeObjectResponsesHaveComparableContracts()
        {
            string Document(string ok, string created) => J(@"{'openapi':'3.1.0','info':{'title':'R','version':'1'},
              'paths':{'/p':{'get':{'operationId':'probe','responses':{
                '200':{'description':'ok','content':{'application/json':{'schema':" + ok + @"}}},
                '201':{'description':'created','content':{'application/json':{'schema':" + created + @"}}}}}}}}");

            Generate(Document("{'type':'object'}", "{'type':'object','additionalProperties':true}"));
            Generate(Document("{}", "{'description':'anything'}"));
            Generate(Document(
                "{'type':'object','additionalProperties':{'type':'integer'}}",
                "{'type':'object','additionalProperties':{'type':'integer'}}"));
            // P4 (G23): contracts that differ get a result wrapper (was OACG105).
            foreach (Phase4GeneratorExecution wrapped in new[]
            {
                MatrixMode.GenerateAndCompile(Document("{}", "{'type':'object'}")),
                MatrixMode.GenerateAndCompile(Document(
                    "{'type':'object','additionalProperties':{'type':'integer'}}",
                    "{'type':'object','additionalProperties':{'type':'string'}}"))
            })
            {
                Assert.Empty(wrapped.RunResult.Diagnostics);
                Assert.Empty(wrapped.CompilationErrors);
                Assert.Contains("Status201", wrapped.GeneratedSource);
            }
        }

        // ---- review follow-ups ---------------------------------------------------------------------

        [Fact]
        public async Task MemberUsedOutsideItsUnionDoesNotRequireTheInjectedDiscriminator()
        {
            const string Schemas = @"
              'Body':{'$ref':'#/components/schemas/Cat'},
              'Pet':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                     'discriminator':{'propertyName':'petType'}}," + CatDogUndeclared;
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            // A bare Cat response has no petType; only the union looks at the discriminator.
            Exchange exchange = await Echo(execution, J("{'meow':true}"), J("{'meow':false}"));

            Assert.False((bool)ReadPath(exchange.Result!, "Meow")!);
            Assert.Equal("Cat", ReadPath(exchange.Result!, "PetType"));
            // a wrong value is not validated by the DTO either
            await Echo(execution, J("{'meow':true}"), J("{'meow':false,'petType':'whatever'}"));
        }

        private const string OpenOverlap = @"
          'Body':{'oneOf':[{'$ref':'#/components/schemas/A'},{'$ref':'#/components/schemas/B'}]},
          'A':{'type':'object','properties':{'x':{'type':'string'}}},
          'B':{'type':'object','properties':{'y':{'type':'string'}}}";

        [Fact]
        public async Task OneOfOpenCandidatesAreTieBrokenByDeclaredProperties()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(OpenOverlap));

            // both open candidates read {x}; only A declares every property of it
            Exchange exchange = await Echo(execution, J("{'x':'s'}"), J("{'x':'s'}"));
            Assert.True(TryGet(exchange.Result!, "TryGetA", out _));
            Assert.False(TryGet(exchange.Result!, "TryGetB", out _));
            Exchange other = await Echo(execution, J("{'x':'s'}"), J("{'y':'t'}"));
            Assert.True(TryGet(other.Result!, "TryGetB", out _));
            Assert.False(TryGet(other.Result!, "TryGetA", out _));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{'x':'s','y':'t'}")]
        public async Task OneOfStillFailsWhenTheTieBreakLeavesNotExactlyOne(string response)
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(OpenOverlap));

            Exception exception = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(execution, J("{'x':'s'}"), J(response)));

            Assert.Contains("matched 2", exception.Message);
        }

        [Fact]
        public async Task AnyOfIsNotTieBroken()
        {
            Phase4GeneratorExecution execution = Generate(EchoDocument(OpenOverlap.Replace("oneOf", "anyOf")));

            Exchange exchange = await Echo(execution, J("{'x':'s'}"), J("{'x':'s','y':'t'}"));

            Assert.True(TryGet(exchange.Result!, "TryGetA", out _));
            Assert.True(TryGet(exchange.Result!, "TryGetB", out _));
        }

        [Theory]
        [InlineData("Cat.yaml")]
        [InlineData("./schemas/cat.json#/Cat")]
        [InlineData("cat.YML")]
        public void RelativeFileMappingReportsOacg101(string target)
        {
            string schemas = @"
              'Body':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                      'discriminator':{'propertyName':'petType','mapping':{'cat':'" + target + @"'}}}," + CatDogUndeclared;

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("outside the current document", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Body/discriminator/mapping/cat'", diagnostic.GetMessage());
        }

        [Fact]
        public void RefSiblingRequiredWithoutADeclarationReportsOacg100AtTheRequiredEntry()
        {
            const string Schemas = @"
              'Body':{'$ref':'#/components/schemas/Base','required':['name']},
              'Base':{'type':'object','properties':{'id':{'type':'integer'}}}";

            Diagnostic diagnostic = Assert.Single(MatrixMode.GenerateAndCompile(
                EchoDocument(Schemas)).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Body/required/0'", diagnostic.GetMessage());
        }

        [Fact]
        public async Task UnionAsMapValueAndListElementRoundTrips()
        {
            const string Map = @"'Body':{'type':'object','additionalProperties':{'oneOf':[{'type':'string'},{'type':'integer'}]}}";
            Phase4GeneratorExecution mapExecution = Generate(EchoDocument(Map));
            Exchange map = await Echo(mapExecution, J("{'a':'x','b':1}"), J("{'a':'y','b':2}"));
            AssertJsonEqual(J("{'a':'x','b':1}"), map.RequestJson);
            var dictionary = (System.Collections.IDictionary)map.Result!;
            Assert.True(TryGet(dictionary["b"]!, "TryGetInt32", out object? two));
            Assert.Equal(2, two);
            Exception bad = await MatrixMode.ThrowsSerializationAsync(() =>
                Echo(mapExecution, J("{}"), J("{'a':true}")));
            Assert.Contains("$.a", bad.Message);

            const string List = @"'Body':{'type':'array','items':{'oneOf':[{'type':'string'},{'type':'integer'}]}}";
            Phase4GeneratorExecution listExecution = Generate(EchoDocument(List));
            Exchange list = await Echo(listExecution, J("['a',1]"), J("['b',2,'c']"));
            AssertJsonEqual(J("['a',1]"), list.RequestJson);
            Assert.Equal(3, ((System.Collections.IList)list.Result!).Count);
        }

        [Fact]
        public async Task NullableUnionAcceptsNullAndValues()
        {
            const string Schemas = @"'Body':{'oneOf':[{'type':'string'},{'type':'integer'},{'type':'null'}]}";
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));

            Exchange none = await Echo(execution, (object?)null, "null");
            Assert.Null(none.Result);
            AssertJsonEqual("null", none.RequestJson);
            Exchange text = await Echo(execution, J("'t'"), J("'u'"));
            Assert.True(TryGet(text.Result!, "TryGetString", out _));
            await MatrixMode.ThrowsSerializationAsync(() => Echo(execution, J("'t'"), "true"));
        }

        [Fact]
        public async Task DiscriminatedUnionAsMapValueRoundTrips()
        {
            const string Schemas = @"
              'Body':{'type':'object','additionalProperties':{'$ref':'#/components/schemas/Pet'}},
              'Pet':{'oneOf':[{'$ref':'#/components/schemas/Cat'},{'$ref':'#/components/schemas/Dog'}],
                     'discriminator':{'propertyName':'petType','mapping':{'cat':'Cat','dog':'Dog'}}}," + CatDogUndeclared;
            Phase4GeneratorExecution execution = Generate(EchoDocument(Schemas));
            const string Json = "{'a':{'petType':'cat','meow':true},'b':{'petType':'dog','bark':'x'}}";

            Exchange exchange = await Echo(execution, J(Json), J(Json));

            AssertJsonEqual(J(Json), exchange.RequestJson);
            Assert.Equal(2, ((System.Collections.IDictionary)exchange.Result!).Count);
        }

        // ---- helpers -----------------------------------------------------------------------------

        /// <summary>Single quotes stand for double quotes so JSON in the tests stays readable.</summary>
        private static string J(string value)
        {
            return value.Replace('\'', '"');
        }

        private static string EchoDocument(string schemas, string version = "3.1.0")
        {
            return J("{'openapi':'" + version + "','info':{'title':'Composition','version':'1'}," +
                     "'servers':[{'url':'https://api.example.test'}]," +
                     "'paths':{'/echo':{'post':{'operationId':'echo'," +
                     "'requestBody':{'required':true,'content':{'application/json':{'schema':" +
                     "{'$ref':'#/components/schemas/Body'}}}}," +
                     "'responses':{'200':{'description':'OK','content':{'application/json':{'schema':" +
                     "{'$ref':'#/components/schemas/Body'}}}}}}}}," +
                     "'components':{'schemas':{" + schemas + "}}}");
        }

        private static IReadOnlyList<TestBundleFactory.V2ReferenceSpec> InternalReferenceEdges(string rootJson)
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(rootJson);
            var edges = new List<TestBundleFactory.V2ReferenceSpec>();
            CollectInternalEdges(document.RootElement, string.Empty, edges);
            return edges.OrderBy(static edge => edge.SourcePointer, StringComparer.Ordinal).ToArray();
        }

        private static void CollectInternalEdges(
            System.Text.Json.JsonElement element,
            string pointer,
            List<TestBundleFactory.V2ReferenceSpec> edges)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
                {
                    string child = pointer + "/" + property.Name.Replace("~", "~0").Replace("/", "~1");
                    if (property.Name == "$ref" &&
                        property.Value.ValueKind == System.Text.Json.JsonValueKind.String &&
                        property.Value.GetString()!.StartsWith("#/", StringComparison.Ordinal))
                    {
                        edges.Add(new TestBundleFactory.V2ReferenceSpec(
                            "root",
                            child,
                            "root",
                            property.Value.GetString()!.Substring(1)));
                    }
                    else
                    {
                        CollectInternalEdges(property.Value, child, edges);
                    }
                }
            }
            else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                int index = 0;
                foreach (System.Text.Json.JsonElement item in element.EnumerateArray())
                {
                    CollectInternalEdges(item, pointer + "/" + index++, edges);
                }
            }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Phase4GeneratorExecution, Assembly> Loaded =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Phase4GeneratorExecution, Assembly>();

        private static Assembly Load(Phase4GeneratorExecution execution)
        {
            return Loaded.GetValue(execution, static value => value.EmitAssembly());
        }

        private static Phase4GeneratorExecution Generate(string document)
        {
            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution;
        }

        private static Task<Exchange> Echo(Phase4GeneratorExecution execution, string requestJson, string responseJson)
        {
            return Echo(
                execution,
                (Type type) => MatrixMode.Deserialize(requestJson, type),
                responseJson);
        }

        private static Task<Exchange> Echo(Phase4GeneratorExecution execution, object? body, string responseJson)
        {
            return Echo(execution, (Type _) => body, responseJson);
        }

        private static async Task<Exchange> Echo(
            Phase4GeneratorExecution execution,
            Func<Type, object?> createBody,
            string responseJson)
        {
            Assembly assembly = Load(execution);
            Type clientType = assembly.GetType(Ns + "Phase4Api", true)!;
            MethodInfo method = clientType.GetMethod("echo")!;
            var handler = new StubHandler(responseJson);
            using var httpClient = new HttpClient(handler);
            object client = Activator.CreateInstance(clientType, httpClient)!;
            object? body = createBody(method.GetParameters()[0].ParameterType);

            var task = (Task)method.Invoke(client, new object?[] { body, CancellationToken.None })!;
            await task.ConfigureAwait(false);
            object? result = task.GetType().GetProperty("Result")!.GetValue(task);
            return new Exchange(handler.Content, result);
        }

        private static bool TryGet(object union, string method, out object? value)
        {
            MethodInfo info = union.GetType().GetMethod(method)!;
            object?[] arguments = { null };
            bool result = (bool)info.Invoke(union, arguments)!;
            value = arguments[0];
            return result;
        }

        private static object? ReadPath(object value, params string[] properties)
        {
            object? current = value;
            foreach (string property in properties)
            {
                current = current!.GetType().GetProperty(property)!.GetValue(current);
            }

            return current;
        }

        private static void AssertJsonEqual(string expected, string actual)
        {
            Assert.True(
                JToken.DeepEquals(JToken.Parse(expected), JToken.Parse(actual)),
                "Expected JSON " + expected + " but was " + actual);
        }

        private sealed class Exchange
        {
            internal Exchange(string requestJson, object? result)
            {
                RequestJson = requestJson;
                Result = result;
            }

            internal string RequestJson { get; }

            internal object? Result { get; }
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _response;

            internal StubHandler(string response)
            {
                _response = response;
            }

            internal string Content { get; private set; } = string.Empty;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Content = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_response, Encoding.UTF8, "application/json")
                };
            }
        }
    }

    /// <summary>The System.Text.Json run of every test of <see cref="Phase4CompositionRuntimeTests"/>.</summary>
    public sealed class Phase4CompositionRuntimeTestsSystemTextJson : Phase4CompositionRuntimeTests
    {
        public Phase4CompositionRuntimeTestsSystemTextJson()
            : base(SerializerMatrixHarness.SystemTextJson)
        {
        }

        protected override bool ExpectsSystemTextJson => true;
    }
}
