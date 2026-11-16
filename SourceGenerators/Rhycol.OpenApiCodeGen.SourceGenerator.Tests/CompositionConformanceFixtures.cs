using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>One JSON instance of a fixture schema and whether JSON Schema (OpenAPI 3.0 dialect) accepts it.</summary>
    internal sealed class ConformanceInstance
    {
        internal ConformanceInstance(string json, bool valid, string reason)
        {
            Json = json;
            Valid = valid;
            Reason = reason;
        }

        /// <summary>The instance, written with single quotes for readability.</summary>
        internal string Json { get; }

        internal bool Valid { get; }

        /// <summary>One line that derives the expectation from the schema.</summary>
        internal string Reason { get; }
    }

    /// <summary>
    /// A minimal OpenAPI 3.0 reproduction of a composition shape that the generator rejects today. The schema
    /// named <c>Body</c> is the response of GET /echo (readBody) and the request body of POST /echo (writeBody).
    /// Expectations follow JSON Schema semantics of the OpenAPI 3.0 dialect: oneOf matches exactly one member,
    /// nullable allows null only next to a type, and a schema of only enum [null] accepts exactly null.
    /// </summary>
    internal sealed class ConformanceFixture
    {
        internal ConformanceFixture(
            string name,
            string category,
            string targetPhase,
            string schemas,
            string currentId,
            string currentLogicalPath,
            params ConformanceInstance[] instances)
        {
            Name = name;
            Category = category;
            TargetPhase = targetPhase;
            Schemas = schemas;
            CurrentId = currentId;
            CurrentLogicalPath = currentLogicalPath;
            Instances = instances;
        }

        internal string Name { get; }

        internal string Category { get; }

        /// <summary>The phase of the composition work that is expected to make this fixture pass.</summary>
        internal string TargetPhase { get; }

        /// <summary>The members of components.schemas, single quoted.</summary>
        internal string Schemas { get; }

        /// <summary>
        /// The diagnostic id that the generator reports today for the first rejected position; empty when the
        /// shape already generates without a diagnostic.
        /// </summary>
        internal string CurrentId { get; }

        /// <summary>The logical path (JSON pointer) of that first diagnostic.</summary>
        internal string CurrentLogicalPath { get; }

        internal IReadOnlyList<ConformanceInstance> Instances { get; }

        internal string Document()
        {
            return (@"{'openapi':'3.0.3','info':{'title':'Conformance','version':'1'},
              'servers':[{'url':'https://api.example.test'}],
              'paths':{'/echo':{
                'get':{'operationId':'readBody','responses':{'200':{'description':'OK','content':{
                  'application/json':{'schema':{'$ref':'#/components/schemas/Body'}}}}}},
                'post':{'operationId':'writeBody',
                  'requestBody':{'required':true,'content':{'application/json':{
                    'schema':{'$ref':'#/components/schemas/Body'}}}},
                  'responses':{'200':{'description':'OK','content':{'application/json':{
                    'schema':{'$ref':'#/components/schemas/Body'}}}}}}}},
              'components':{'schemas':{" + Schemas + "}}}").Replace('\'', '"');
        }

        public override string ToString() => Name;
    }

    internal static class CompositionConformanceFixtures
    {
        private const string Uuid = "00000000-0000-0000-0000-000000000001";

        private static ConformanceInstance Ok(string json, string reason) => new ConformanceInstance(json, true, reason);

        private static ConformanceInstance Bad(string json, string reason) => new ConformanceInstance(json, false, reason);

        internal static readonly IReadOnlyList<ConformanceFixture> All = new[]
        {
            // ---- A: constraint-only oneOf / anyOf next to a parent that has properties ------------------

            new ConformanceFixture(
                "A1_OneOfPinsEnumAndNullable", "A", "P3: constraint-only oneOf/anyOf",
                @"'Body':{'type':'object',
                   'properties':{
                     'kind':{'type':'string','enum':['KIND_A','KIND_B']},
                     'total':{'type':'integer','format':'int64'},
                     'paid':{'type':'integer','format':'int64','nullable':true},
                     'free':{'type':'integer','format':'int64','nullable':true}},
                   'required':['kind','total'],
                   'oneOf':[
                     {'properties':{'kind':{'enum':['KIND_A']},
                        'paid':{'type':'integer','format':'int64'},'free':{'type':'integer','format':'int64'}},
                      'required':['paid','free']},
                     {'properties':{'kind':{'enum':['KIND_B']},
                        'paid':{'type':'integer','format':'int64','nullable':true,'enum':[null]},
                        'free':{'type':'integer','format':'int64','nullable':true,'enum':[null]}}}],
                   'additionalProperties':false}",
                "", "",
                Ok("{'kind':'KIND_A','total':5,'paid':2,'free':3}", "member 1: KIND_A with both amounts present"),
                Ok("{'kind':'KIND_B','total':5}", "member 2: KIND_B, the amounts are absent"),
                Ok("{'kind':'KIND_B','total':5,'paid':null,'free':null}", "member 2: null satisfies enum [null]"),
                Bad("{'kind':'KIND_A','total':5}", "member 1 requires paid and free, member 2 requires KIND_B: no match"),
                Bad("{'kind':'KIND_A','total':5,'paid':null,'free':null}", "member 1: null is not an integer without nullable"),
                Bad("{'kind':'KIND_B','total':5,'paid':1,'free':2}", "member 2 only accepts null for the amounts, member 1 requires KIND_A")),

            new ConformanceFixture(
                "A2_OneOfPinsUndeclaredPropertiesToNull", "A", "P3: constraint-only oneOf/anyOf",
                @"'Body':{'type':'object',
                   'properties':{'total':{'type':'integer','format':'int64','minimum':1}},
                   'required':['total'],
                   'oneOf':[
                     {'properties':{'kind':{'type':'string','enum':['KIND_A']},
                        'code':{'type':'string','enum':['CODE_A','CODE_B'],'nullable':false},
                        'ref':{'enum':[null]}},
                      'required':['kind','code']},
                     {'properties':{'kind':{'type':'string','enum':['KIND_B','KIND_C']},
                        'ref':{'type':'string','format':'uuid','nullable':false},
                        'code':{'enum':[null]}},
                      'required':['kind','ref']}]}",
                "", "",
                Ok("{'total':1,'kind':'KIND_A','code':'CODE_A'}", "member 1: kind and code present, ref absent"),
                Ok("{'total':2,'kind':'KIND_C','ref':'" + Uuid + "'}", "member 2: KIND_C with ref"),
                Ok("{'total':1,'kind':'KIND_A','code':'CODE_B','ref':null}", "member 1: ref pinned to null is null"),
                Bad("{'total':1,'kind':'KIND_A'}", "member 1 requires code; member 2 does not accept KIND_A"),
                Bad("{'total':1,'kind':'KIND_A','code':'CODE_A','ref':'" + Uuid + "'}", "member 1 pins ref to null"),
                Bad("{'total':0,'kind':'KIND_A','code':'CODE_A'}", "the parent minimum of total is 1"),
                Bad("{'total':1,'kind':'KIND_B','ref':'" + Uuid + "','code':'CODE_A'}", "member 2 pins code to null")),

            new ConformanceFixture(
                "A3_AnyOfPinsBooleanAndArrayLength", "A", "P3: constraint-only oneOf/anyOf",
                @"'Body':{'type':'object',
                   'properties':{
                     'flag':{'type':'boolean'},
                     'names':{'type':'array','uniqueItems':true,'items':{'type':'string','enum':['NAME_A','NAME_B']}}},
                   'anyOf':[
                     {'properties':{'flag':{'enum':[true]},'names':{'maxItems':0}}},
                     {'properties':{'flag':{'enum':[false]},'names':{'minItems':1}}}],
                   'required':['flag','names'],'additionalProperties':false}",
                "", "",
                Ok("{'flag':true,'names':[]}", "member 1: true with an empty array"),
                Ok("{'flag':false,'names':['NAME_A']}", "member 2: false with a non-empty array"),
                Bad("{'flag':true,'names':['NAME_A']}", "member 1 allows no items, member 2 requires false"),
                Bad("{'flag':false,'names':[]}", "member 2 requires at least one item, member 1 requires true"),
                Bad("{'flag':true}", "names is required by the parent")),

            new ConformanceFixture(
                "A4_AnyOfRequiredAlternativesWithMinProperties", "A", "P3: constraint-only oneOf/anyOf",
                @"'Body':{'type':'object',
                   'properties':{
                     'alpha':{'type':'string','minLength':1},
                     'beta':{'type':'integer','format':'int64','minimum':1},
                     'gamma':{'type':'boolean'}},
                   'additionalProperties':false,'minProperties':1,
                   'anyOf':[{'required':['alpha']},{'required':['beta']},{'required':['gamma']}]}",
                "", "",
                Ok("{'alpha':'x'}", "alpha is present"),
                Ok("{'beta':1,'gamma':true}", "beta and gamma are present, one match is enough for anyOf"),
                Bad("{}", "minProperties is 1 and no required alternative is met"),
                Bad("{'unknown':1}", "additionalProperties is false"),
                Bad("{'beta':0}", "beta violates minimum 1")),

            new ConformanceFixture(
                "A5_AnyOfNotAndRequiredAlternatives", "A", "P3: constraint-only oneOf/anyOf",
                @"'Body':{'type':'object',
                   'properties':{
                     'name':{'type':'string','minLength':1},
                     'state':{'type':'string','enum':['STATE_A','STATE_B',null],'nullable':true},
                     'reason':{'type':'string','nullable':true}},
                   'anyOf':[
                     {'not':{'required':['state']}},
                     {'required':['state'],'properties':{'state':{'enum':[null]}}},
                     {'required':['state','reason'],'properties':{'reason':{'type':'string','minLength':1}}}],
                   'additionalProperties':false}",
                "", "",
                Ok("{'name':'x'}", "member 1: state is absent"),
                Ok("{'state':null}", "member 2: state is null"),
                Ok("{'state':'STATE_A','reason':'r'}", "member 3: state and a non-empty reason"),
                Bad("{'state':'STATE_A'}", "member 1 forbids state, member 2 needs null, member 3 needs reason"),
                Bad("{'state':'STATE_A','reason':null}", "member 3: null is not a string without nullable"),
                Bad("{'state':'STATE_A','reason':''}", "member 3: reason is shorter than 1")),

            new ConformanceFixture(
                "A6_ArrayWithItemsAndAnyOf", "A", "P3: constraint-only oneOf/anyOf",
                @"'Body':{'type':'object','required':['entries'],'additionalProperties':false,
                   'properties':{
                     'entries':{'type':'array','nullable':true,'maxItems':2,
                       'items':{'$ref':'#/components/schemas/Entry'},
                       'anyOf':[{'maxItems':1},{'$ref':'#/components/schemas/EntryPair'}]}}},
                 'Entry':{'type':'object','required':['kind','total'],
                   'properties':{'kind':{'type':'string','enum':['KIND_A','KIND_B']},'total':{'type':'integer','format':'int64'}}},
                 'EntryPair':{'type':'array','minItems':2,'maxItems':2,'items':{'$ref':'#/components/schemas/Entry'}}",
                "", "",
                Ok("{'entries':null}", "nullable array, the array keywords ignore null"),
                Ok("{'entries':[]}", "maxItems 1 matches"),
                Ok("{'entries':[{'kind':'KIND_A','total':1},{'kind':'KIND_B','total':2}]}", "the pair member matches"),
                Bad("{'entries':[{'kind':'KIND_A','total':1},{'kind':'KIND_A','total':1},{'kind':'KIND_B','total':2}]}", "maxItems 2"),
                Bad("{'entries':[{'kind':'KIND_C','total':1}]}", "item kind is not in the enum"),
                Bad("{'entries':{}}", "entries is not an array")),

            // ---- B: allOf narrowing of a property that the base declares -------------------------------

            new ConformanceFixture(
                "B1_AllOfAddsMinimum", "B", "P4: allOf property narrowing",
                @"'Base':{'type':'object','required':['amount'],
                   'properties':{'amount':{'type':'integer','format':'int64'},'label':{'type':'string'}}},
                 'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                   {'type':'object','properties':{'amount':{'type':'integer','format':'int64','minimum':1}}}]}",
                "", "",
                Ok("{'amount':1}", "1 >= minimum 1"),
                Ok("{'amount':5,'label':'x'}", "both parts accept it"),
                Bad("{'amount':0}", "the second part has minimum 1"),
                Bad("{'label':'x'}", "amount is required by Base"),
                Bad("{'amount':'1'}", "amount is an integer")),

            new ConformanceFixture(
                "B2_AllOfAddsPatternInNestedProperty", "B", "P4: allOf property narrowing",
                @"'Base':{'type':'object','required':['digest'],'properties':{'digest':{'type':'string'}}},
                 'Body':{'type':'object','required':['image'],'additionalProperties':false,
                   'properties':{'image':{'allOf':[{'$ref':'#/components/schemas/Base'},
                     {'type':'object','properties':{'digest':{'type':'string','pattern':'^[0-9a-f]{4}$'}}}]}}}",
                "", "",
                Ok("{'image':{'digest':'ab12'}}", "matches the pattern"),
                Bad("{'image':{'digest':'zz'}}", "does not match the pattern"),
                Bad("{'image':{'digest':'ab123'}}", "five characters do not match ^[0-9a-f]{4}$"),
                Bad("{'image':{'digest':null}}", "null is not a string")),

            new ConformanceFixture(
                "B3_AllOfNarrowsArrayItemsRefAndLength", "B", "P4: allOf property narrowing",
                @"'Row':{'type':'object','required':['id','qty'],
                   'properties':{'id':{'type':'string'},'qty':{'type':'integer','format':'int64'}}},
                 'RowNarrow':{'allOf':[{'$ref':'#/components/schemas/Row'},
                   {'type':'object','properties':{'qty':{'type':'integer','format':'int64','minimum':1}}}]},
                 'Base':{'type':'object','properties':{'rows':{'type':'array','items':{'$ref':'#/components/schemas/Row'}}}},
                 'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                   {'type':'object','properties':{'rows':{'type':'array','minItems':1,'maxItems':2,
                     'items':{'$ref':'#/components/schemas/RowNarrow'}}}}]}",
                "", "",
                Ok("{}", "rows is optional"),
                Ok("{'rows':[{'id':'a','qty':1}]}", "one item with qty >= 1"),
                Ok("{'rows':[{'id':'a','qty':1},{'id':'b','qty':2}]}", "two items"),
                Bad("{'rows':[]}", "minItems 1"),
                Bad("{'rows':[{'id':'a','qty':1},{'id':'b','qty':1},{'id':'c','qty':1}]}", "maxItems 2"),
                Bad("{'rows':[{'id':'a','qty':0}]}", "RowNarrow has minimum 1 for qty")),

            new ConformanceFixture(
                "B4_AllOfNullableDifference", "B", "P4: allOf property narrowing",
                @"'Base':{'type':'object','properties':{
                   'note':{'type':'string','nullable':true},'count':{'type':'integer','format':'int32'}}},
                 'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                   {'type':'object','properties':{
                     'note':{'type':'string'},'count':{'type':'integer','format':'int32','nullable':true}}}]}",
                "", "",
                Ok("{}", "no property is required"),
                Ok("{'note':'x','count':1}", "both parts accept the values"),
                Bad("{'note':null}", "the second part declares string without nullable"),
                Bad("{'count':null}", "Base declares integer without nullable")),

            new ConformanceFixture(
                "B5_AllOfNarrowsToEnumValues", "B", "P2: boolean enum next to the enum narrowing of an allOf part",
                @"'Base':{'type':'object','required':['mode','flag'],
                   'properties':{'mode':{'type':'integer','format':'int32'},'flag':{'type':'boolean'}}},
                 'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                   {'type':'object','properties':{
                     'mode':{'type':'integer','enum':[1,2]},'flag':{'type':'boolean','enum':[false]}}}]}",
                "", "",
                Ok("{'mode':1,'flag':false}", "mode 1 and flag false"),
                Ok("{'mode':2,'flag':false}", "mode 2 and flag false"),
                Bad("{'mode':3,'flag':false}", "mode is not in [1, 2]"),
                Bad("{'mode':1,'flag':true}", "flag is pinned to false")),

            // ---- C: boolean enum -----------------------------------------------------------------------

            new ConformanceFixture(
                "C1_BooleanEnumInAllOfNarrowingPart", "C", "P2: boolean and null enums",
                @"'Base':{'type':'object','required':['status','code'],
                   'properties':{'status':{'type':'integer','format':'int32'},'code':{'type':'string'}}},
                 'Body':{'type':'object','allOf':[{'$ref':'#/components/schemas/Base'},
                   {'type':'object','required':['status','code','flagged'],
                    'properties':{'status':{'type':'integer','enum':[409]},
                      'code':{'type':'string','enum':['CODE_A']},
                      'flagged':{'type':'boolean','enum':[true]}}}]}",
                "", "",
                Ok("{'status':409,'code':'CODE_A','flagged':true}", "all pins are met"),
                Bad("{'status':409,'code':'CODE_A','flagged':false}", "flagged is pinned to true"),
                Bad("{'status':409,'code':'CODE_A'}", "flagged is required by the second part"),
                Bad("{'status':500,'code':'CODE_A','flagged':true}", "status is pinned to 409")),

            new ConformanceFixture(
                "C2_BooleanEnumProperties", "C", "P2: boolean and null enums",
                @"'Body':{'type':'object','required':['on'],'additionalProperties':false,
                   'properties':{'on':{'type':'boolean','enum':[true]},'off':{'type':'boolean','enum':[false]}}}",
                "", "",
                Ok("{'on':true}", "on is true, off is optional"),
                Ok("{'on':true,'off':false}", "both pins are met"),
                Bad("{'on':false}", "on is pinned to true"),
                Bad("{'on':true,'off':true}", "off is pinned to false")),

            // ---- D: enum [null] ------------------------------------------------------------------------

            new ConformanceFixture(
                "D1_NullOnlyEnumWithNullable", "D", "P2: boolean and null enums",
                @"'Body':{'type':'object','required':['other'],'additionalProperties':false,
                   'properties':{'other':{'type':'string'},
                     'pinned':{'type':'string','format':'date-time','nullable':true,'enum':[null]}}}",
                "", "",
                Ok("{'other':'x'}", "pinned is optional"),
                Ok("{'other':'x','pinned':null}", "nullable string whose only enum value is null"),
                Bad("{'other':'x','pinned':'2026-01-01T00:00:00Z'}", "a string is not in enum [null]"),
                Bad("{'other':'x','pinned':1}", "an integer is not in enum [null]")),

            new ConformanceFixture(
                "D2_NullOnlyEnumWithoutType", "D", "P2: boolean and null enums",
                @"'Body':{'type':'object','required':['other'],'additionalProperties':false,
                   'properties':{'other':{'type':'string'},'pinned':{'enum':[null]}}}",
                "", "",
                Ok("{'other':'x'}", "pinned is optional"),
                Ok("{'other':'x','pinned':null}", "enum [null] alone accepts null"),
                Bad("{'other':'x','pinned':'v'}", "a string is not in enum [null]"),
                Bad("{'other':'x','pinned':false}", "false is not in enum [null]")),

            // ---- E: oneOf / anyOf inside allOf parts ---------------------------------------------------

            new ConformanceFixture(
                "E1_AllOfWithOneOfPart", "E", "P5: allOf with oneOf/anyOf parts",
                @"'Body':{'type':'object','required':['kind','total'],'additionalProperties':false,
                   'properties':{
                     'kind':{'type':'string','enum':['KIND_A','KIND_B','KIND_C']},
                     'code':{'type':'string','enum':['CODE_A','CODE_B',null],'nullable':true},
                     'ref':{'type':'string','format':'uuid','nullable':true},
                     'total':{'type':'integer','format':'int64','minimum':1}},
                   'allOf':[{'oneOf':[
                     {'properties':{'kind':{'enum':['KIND_A']},
                        'code':{'type':'string','enum':['CODE_A','CODE_B'],'nullable':false},'ref':{'enum':[null]}},
                      'required':['code']},
                     {'properties':{'kind':{'enum':['KIND_B']},
                        'ref':{'type':'string','format':'uuid','nullable':false},'code':{'enum':[null]}},
                      'required':['ref']},
                     {'properties':{'kind':{'enum':['KIND_C']},
                        'ref':{'type':'string','format':'uuid','nullable':false},'code':{'enum':[null]},
                        'total':{'type':'integer','minimum':1,'maximum':1}},
                      'required':['ref']}]}]}",
                "", "",
                Ok("{'kind':'KIND_A','total':3,'code':'CODE_A'}", "member 1 only"),
                Ok("{'kind':'KIND_B','total':3,'ref':'" + Uuid + "'}", "member 2 only"),
                Ok("{'kind':'KIND_C','total':1,'ref':'" + Uuid + "'}", "member 3: total is 1"),
                Bad("{'kind':'KIND_C','total':2,'ref':'" + Uuid + "'}", "member 3 limits total to 1, no other member accepts KIND_C"),
                Bad("{'kind':'KIND_A','total':3}", "member 1 requires code"),
                Bad("{'kind':'KIND_B','total':3,'ref':'" + Uuid + "','code':'CODE_A'}", "member 2 pins code to null")),

            new ConformanceFixture(
                "E2_AllOfWithTwoAnyOfParts", "E", "P5: allOf with oneOf/anyOf parts",
                @"'Body':{'type':'object','required':['state','items','note'],'additionalProperties':false,
                   'properties':{
                     'state':{'type':'string','enum':['STATE_A','STATE_B']},
                     'items':{'type':'array','items':{'type':'string'}},
                     'note':{'type':'string','nullable':true}},
                   'allOf':[
                     {'anyOf':[{'properties':{'state':{'enum':['STATE_A']}}},
                               {'properties':{'items':{'type':'array','minItems':1,'items':{'type':'string'}}}}]},
                     {'anyOf':[{'properties':{'state':{'enum':['STATE_B']}}},
                               {'properties':{'note':{'type':'string','nullable':false}}}]}]}",
                "", "",
                Ok("{'state':'STATE_A','items':[],'note':'x'}", "part 1: STATE_A, part 2: note is not null"),
                Ok("{'state':'STATE_B','items':['a'],'note':null}", "part 1: items is non-empty, part 2: STATE_B"),
                Bad("{'state':'STATE_A','items':[],'note':null}", "part 2: not STATE_B and note is null"),
                Bad("{'state':'STATE_B','items':[],'note':null}", "part 1: not STATE_A and items is empty"),
                Bad("{'state':'STATE_A','items':[]}", "note is required")),

            // ---- F: allOf next to oneOf / anyOf in one schema ------------------------------------------

            new ConformanceFixture(
                "F1_AllOfAndOneOfInOneSchema", "F", "P5: allOf with oneOf/anyOf parts",
                @"'Base':{'type':'object','required':['id','tier','progress','next'],
                   'properties':{'id':{'type':'string'},
                     'tier':{'type':'integer','format':'int64'},
                     'progress':{'type':'integer','format':'int64','minimum':0},
                     'next':{'type':'integer','format':'int64','minimum':1,'nullable':true}}},
                 'Body':{'allOf':[{'$ref':'#/components/schemas/Base'},
                   {'type':'object','properties':{'tier':{'type':'integer','format':'int64','minimum':1,'maximum':100}}}],
                   'oneOf':[
                     {'properties':{'tier':{'enum':[100]},'progress':{'enum':[0]},'next':{'enum':[null]}}},
                     {'properties':{'tier':{'maximum':99},'next':{'type':'integer','format':'int64','minimum':1}}}]}",
                "", "",
                Ok("{'id':'a','tier':100,'progress':0,'next':null}", "member 1: top tier, no progress, no next"),
                Ok("{'id':'a','tier':50,'progress':7,'next':3}", "member 2: lower tier with a next value"),
                Bad("{'id':'a','tier':100,'progress':1,'next':null}", "member 1 pins progress to 0, member 2 limits tier to 99"),
                Bad("{'id':'a','tier':50,'progress':7,'next':null}", "member 2 requires an integer next, member 1 requires tier 100"),
                Bad("{'id':'a','tier':101,'progress':0,'next':null}", "the second allOf part limits tier to 100")),

            new ConformanceFixture(
                "F2_AllOfAndAnyOfInOneSchema", "F", "P5: allOf with oneOf/anyOf parts",
                @"'Base':{'type':'object','required':['status','ref','code','order'],
                   'properties':{
                     'status':{'type':'string','enum':['STATE_A','STATE_B']},
                     'ref':{'type':'string','nullable':true},
                     'code':{'type':'string','nullable':true},
                     'order':{'type':'integer','format':'int32','minimum':0}}},
                 'Body':{'allOf':[{'$ref':'#/components/schemas/Base'}],
                   'anyOf':[
                     {'properties':{'status':{'enum':['STATE_B']}}},
                     {'properties':{'ref':{'type':'string','minLength':1}}},
                     {'properties':{'code':{'type':'string','minLength':1}}}]}",
                "", "",
                Ok("{'status':'STATE_B','ref':null,'code':null,'order':0}", "member 1: STATE_B"),
                Ok("{'status':'STATE_A','ref':'r','code':null,'order':0}", "member 2: ref is non-empty"),
                Bad("{'status':'STATE_A','ref':null,'code':null,'order':0}", "no anyOf member matches"),
                Bad("{'status':'STATE_A','ref':'','code':null,'order':0}", "member 2: ref is empty, member 3: code is null"),
                Bad("{'status':'STATE_B','ref':null,'code':null}", "order is required by Base")),

            // ---- N: not --------------------------------------------------------------------------------

            new ConformanceFixture(
                "N1_NotRequired", "N", "P6: not",
                @"'Body':{'type':'object','additionalProperties':false,
                   'properties':{'alpha':{'type':'string'},'beta':{'type':'string'}},
                   'not':{'required':['beta']}}",
                "", "",
                Ok("{}", "beta is absent"),
                Ok("{'alpha':'x'}", "beta is absent"),
                Bad("{'beta':'x'}", "not required [beta] rejects an instance that has beta"),
                Bad("{'alpha':'x','beta':'y'}", "beta is present")),

            new ConformanceFixture(
                "N2_ArrayWithAllOfOfNots", "N", "P0: already supported (not is evaluated on arrays today)",
                @"'Entry':{'type':'object','required':['kind','total'],
                   'properties':{'kind':{'type':'string','enum':['KIND_A','KIND_B']},'total':{'type':'integer','format':'int64'}}},
                 'Body':{'type':'array','minItems':2,'maxItems':2,'items':{'$ref':'#/components/schemas/Entry'},
                   'allOf':[
                     {'not':{'items':{'properties':{'kind':{'enum':['KIND_A']}}}}},
                     {'not':{'items':{'properties':{'kind':{'enum':['KIND_B']}}}}}]}",
                "", "",
                Ok("[{'kind':'KIND_A','total':0},{'kind':'KIND_B','total':0}]", "not all items are A and not all are B"),
                Ok("[{'kind':'KIND_B','total':0},{'kind':'KIND_A','total':0}]", "the order does not matter"),
                Bad("[{'kind':'KIND_A','total':0},{'kind':'KIND_A','total':1}]", "every item is KIND_A, the first not fails"),
                Bad("[{'kind':'KIND_B','total':0},{'kind':'KIND_B','total':1}]", "every item is KIND_B, the second not fails"),
                Bad("[{'kind':'KIND_A','total':0}]", "minItems 2"),
                Bad("[{'kind':'KIND_A','total':0},{'kind':'KIND_C','total':0}]", "kind is not in the enum")),
        };

        internal static ConformanceFixture Get(string name)
        {
            return All.Single(fixture => fixture.Name == name);
        }

        internal static IEnumerable<object[]> Names()
        {
            return All.Select(static fixture => new object[] { fixture.Name });
        }

        /// <summary>The fixture names whose target phase starts with the prefix ("P3" for "P3: ...").</summary>
        internal static IEnumerable<object[]> NamesOfPhase(string phase)
        {
            return All
                .Where(fixture => fixture.TargetPhase.StartsWith(phase + ":", StringComparison.Ordinal))
                .Select(static fixture => new object[] { fixture.Name });
        }
    }
}
