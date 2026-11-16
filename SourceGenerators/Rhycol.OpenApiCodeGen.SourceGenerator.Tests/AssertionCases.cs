using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal enum AssertionDirection
    {
        Both,
        Request,
        Response
    }

    /// <summary>One JSON instance (single quotes for readability) and whether the schema accepts it.</summary>
    internal sealed class AssertionInstance
    {
        internal AssertionInstance(string json, bool valid, AssertionDirection direction)
        {
            Json = json.Replace('\'', '"');
            Valid = valid;
            Direction = direction;
        }

        internal string Json { get; }

        internal bool Valid { get; }

        internal AssertionDirection Direction { get; }
    }

    /// <summary>
    /// A schema fragment (any subtree, written with single quotes) with the schemas it refers to, and the
    /// instances that JSON Schema accepts or rejects. Dialect "3.0" or "3.1".
    /// </summary>
    internal sealed class AssertionCase
    {
        internal AssertionCase(string name, string dialect, string schema, string components, AssertionInstance[] instances)
        {
            Name = name;
            Dialect = dialect;
            Schema = schema;
            Components = components;
            Instances = instances;
        }

        internal string Name { get; }

        internal string Dialect { get; }

        internal string Schema { get; }

        /// <summary>The members of components.schemas that the fragment refers to.</summary>
        internal string Components { get; }

        internal IReadOnlyList<AssertionInstance> Instances { get; }

        internal string Document()
        {
            return ("{'openapi':'" + (Dialect == "3.1" ? "3.1.0" : "3.0.3") +
                    "','info':{'title':'t','version':'1'},'paths':{},'components':{'schemas':{" + Components + "}}}")
                .Replace('\'', '"');
        }

        public override string ToString() => Name;
    }

    internal static class AssertionCases
    {
        private static AssertionInstance Ok(string json) => new AssertionInstance(json, true, AssertionDirection.Both);

        private static AssertionInstance Bad(string json) => new AssertionInstance(json, false, AssertionDirection.Both);

        private static AssertionInstance OkRequest(string json) => new AssertionInstance(json, true, AssertionDirection.Request);

        private static AssertionInstance BadRequest(string json) => new AssertionInstance(json, false, AssertionDirection.Request);

        private static AssertionInstance OkResponse(string json) => new AssertionInstance(json, true, AssertionDirection.Response);

        private static AssertionInstance BadResponse(string json) => new AssertionInstance(json, false, AssertionDirection.Response);

        private static AssertionCase Case(string name, string dialect, string schema, params AssertionInstance[] instances) =>
            new AssertionCase(name, dialect, schema, string.Empty, instances);

        private static AssertionCase WithComponents(string name, string dialect, string schema, string components, params AssertionInstance[] instances) =>
            new AssertionCase(name, dialect, schema, components, instances);

        internal static readonly IReadOnlyList<AssertionCase> All = new[]
        {
            // ---- type ------------------------------------------------------------------------------------
            Case("Type_String", "3.0", "{'type':'string'}",
                Ok("'a'"), Ok("''"), Bad("1"), Bad("null"), Bad("['a']"), Bad("{}"), Bad("true")),
            Case("Type_NullableString", "3.0", "{'type':'string','nullable':true}",
                Ok("null"), Ok("'a'"), Bad("1"), Bad("false")),
            Case("Type_NullableWithoutTypeDoesNotRestrict", "3.0", "{'nullable':true}",
                Ok("null"), Ok("1"), Ok("'a'"), Ok("{}"), Ok("[]")),
            Case("Type_NoTypeAcceptsNull", "3.0", "{'minLength':2}",
                Ok("null"), Ok("5"), Ok("'ab'"), Ok("{}"), Bad("'a'")),
            Case("Type_NullableDoesNotWidenEnum", "3.0", "{'type':'string','nullable':true,'enum':['A']}",
                Ok("'A'"), Bad("null"), Bad("'B'")),
            Case("Type_NullableEnumWithNull", "3.0", "{'type':'string','nullable':true,'enum':['A',null]}",
                Ok("'A'"), Ok("null"), Bad("'B'"), Bad("1")),
            Case("Type_NonNullableTypeRejectsNullEvenInEnum", "3.0", "{'type':'string','enum':['A',null]}",
                Ok("'A'"), Bad("null")),
            Case("Type_Integer", "3.0", "{'type':'integer'}",
                Ok("1"), Ok("-3"), Ok("1.0"), Ok("1e2"), Ok("0"), Bad("1.5"), Bad("'1'"), Bad("true"), Bad("null")),
            Case("Type_Number", "3.0", "{'type':'number'}",
                Ok("1"), Ok("1.5"), Ok("-0.25"), Bad("'1'"), Bad("null"), Bad("[1]")),
            Case("Type_Boolean", "3.0", "{'type':'boolean'}",
                Ok("true"), Ok("false"), Bad("0"), Bad("'true'"), Bad("null")),
            Case("Type_Object", "3.0", "{'type':'object'}",
                Ok("{}"), Ok("{'a':1}"), Bad("[]"), Bad("null"), Bad("'x'")),
            Case("Type_Array", "3.0", "{'type':'array'}",
                Ok("[]"), Ok("[1,'a']"), Bad("{}"), Bad("null"), Bad("'x'")),
            Case("Type_Union", "3.1", "{'type':['string','null']}",
                Ok("null"), Ok("'a'"), Bad("1"), Bad("{}")),
            Case("Type_NullOnly", "3.1", "{'type':'null'}",
                Ok("null"), Bad("0"), Bad("''"), Bad("false")),
            Case("Type_IntegerOrString", "3.1", "{'type':['integer','string']}",
                Ok("1"), Ok("'a'"), Ok("2.0"), Bad("2.5"), Bad("null"), Bad("true")),
            Case("Type_NullableKeywordIsIgnoredIn31", "3.1", "{'type':'string','nullable':true}",
                Ok("'a'"), Bad("null")),

            // ---- enum / const ----------------------------------------------------------------------------
            Case("Enum_Boolean", "3.0", "{'type':'boolean','enum':[true]}",
                Ok("true"), Bad("false"), Bad("1"), Bad("'true'"), Bad("null")),
            Case("Enum_NullOnly", "3.0", "{'enum':[null]}",
                Ok("null"), Bad("'v'"), Bad("false"), Bad("0"), Bad("{}")),
            Case("Enum_Numbers", "3.0", "{'enum':[1,2.5]}",
                Ok("1"), Ok("1.0"), Ok("2.5"), Ok("25e-1"), Bad("3"), Bad("'1'"), Bad("true")),
            Case("Enum_Strings", "3.0", "{'type':'string','enum':['A','B']}",
                Ok("'A'"), Ok("'B'"), Bad("'C'"), Bad("'a'"), Bad("1")),
            Case("Enum_Object", "3.0", "{'enum':[{'a':1,'b':[true,null]}]}",
                Ok("{'b':[true,null],'a':1.0}"), Bad("{'a':1}"), Bad("{'a':1,'b':[true,null],'c':0}"), Bad("{'a':2,'b':[true,null]}")),
            Case("Enum_Array", "3.0", "{'enum':[[1,2]]}",
                Ok("[1,2]"), Ok("[1.0,2]"), Bad("[2,1]"), Bad("[1]"), Bad("1")),
            Case("Enum_Mixed", "3.0", "{'enum':['a',1,null,false]}",
                Ok("'a'"), Ok("1"), Ok("null"), Ok("false"), Bad("true"), Bad("0"), Bad("'1'")),
            Case("Const_Number", "3.1", "{'const':5}",
                Ok("5"), Ok("5.0"), Bad("6"), Bad("'5'"), Bad("null")),
            Case("Const_Object", "3.1", "{'const':{'k':'v'}}",
                Ok("{'k':'v'}"), Bad("{'k':'w'}"), Bad("{}")),
            Case("Const_Null", "3.1", "{'const':null}",
                Ok("null"), Bad("0"), Bad("false")),
            Case("Const_IsNotAKeywordIn30", "3.0", "{'const':5}",
                Ok("5"), Ok("6"), Ok("null")),

            // ---- properties / required / additionalProperties ----------------------------------------------
            Case("Properties_OnlyPresentKeysAreChecked", "3.0",
                "{'type':'object','properties':{'x':{'type':'integer'},'y':{'type':'string'}}}",
                Ok("{}"), Ok("{'x':1}"), Ok("{'x':1,'y':'a','z':null}"), Bad("{'x':'a'}"), Bad("{'y':1}"), Bad("[]")),
            Case("Properties_PartialSchemaPassesNonObjects", "3.0", "{'properties':{'x':{'maximum':99}}}",
                Ok("5"), Ok("null"), Ok("'s'"), Ok("[1]"), Ok("{}"), Ok("{'x':99}"), Ok("{'x':'a'}"), Ok("{'x':null}"), Bad("{'x':100}")),
            Case("Required_OnlyForObjects", "3.0", "{'required':['a']}",
                Ok("{'a':null}"), Ok("'str'"), Ok("[1]"), Ok("null"), Bad("{}"), Bad("{'b':1}")),
            Case("Required_Several", "3.0", "{'type':'object','required':['a','b']}",
                Ok("{'a':1,'b':2}"), Bad("{'a':1}"), Bad("{'b':1}"), Bad("{}")),
            Case("Additional_False", "3.0", "{'properties':{'a':{}},'additionalProperties':false}",
                Ok("{'a':1}"), Ok("{}"), Ok("5"), Bad("{'b':1}"), Bad("{'a':1,'b':1}")),
            Case("Additional_Schema", "3.0",
                "{'properties':{'a':{'type':'string'}},'additionalProperties':{'type':'integer'}}",
                Ok("{'a':'x','b':1}"), Ok("{'a':'x'}"), Bad("{'a':'x','b':'y'}"), Bad("{'a':1}")),
            Case("Additional_True", "3.0", "{'properties':{'a':{}},'additionalProperties':true}",
                Ok("{'a':1,'b':{}}"), Ok("{}")),
            Case("Additional_FalseWithoutProperties", "3.0", "{'additionalProperties':false}",
                Ok("{}"), Bad("{'a':1}"), Ok("[1]")),
            Case("Properties_Nested", "3.0",
                "{'type':'object','properties':{'a':{'type':'object','properties':{'b':{'type':'array','items':{'type':'integer','minimum':1}}},'required':['b']}}}",
                Ok("{'a':{'b':[1,2]}}"), Ok("{}"), Bad("{'a':{}}"), Bad("{'a':{'b':[1,0]}}"), Bad("{'a':{'b':'x'}}")),

            // ---- items -----------------------------------------------------------------------------------
            Case("Items_Schema", "3.0", "{'items':{'type':'integer'}}",
                Ok("[]"), Ok("[1,2]"), Ok("'x'"), Ok("{}"), Ok("null"), Bad("[1,'a']"), Bad("['a']")),
            Case("Items_ObjectWithRequired", "3.0",
                "{'type':'array','items':{'type':'object','required':['k'],'properties':{'k':{'type':'string'}}}}",
                Ok("[{'k':'a'},{'k':'b'}]"), Bad("[{'k':'a'},{}]"), Bad("[{'k':1}]")),

            // ---- keywords --------------------------------------------------------------------------------
            Case("Constraint_StringLength", "3.0", "{'type':'string','minLength':2,'maxLength':3}",
                Ok("'ab'"), Ok("'abc'"), Bad("'a'"), Bad("'abcd'")),
            Case("Constraint_LengthCountsCodePoints", "3.0", "{'maxLength':1}",
                Ok("'\\ud83d\\ude00'"), Bad("'ab'"), Ok("5")),
            Case("Constraint_Pattern", "3.0", "{'pattern':'^[a-c]+$'}",
                Ok("'abc'"), Ok("5"), Bad("'abd'"), Bad("''")),
            Case("Constraint_NumberBounds30", "3.0", "{'minimum':1,'maximum':10}",
                Ok("1"), Ok("10"), Ok("5.5"), Ok("'x'"), Ok("null"), Bad("0"), Bad("10.01")),
            Case("Constraint_ExclusiveBounds30", "3.0",
                "{'minimum':1,'exclusiveMinimum':true,'maximum':10,'exclusiveMaximum':true}",
                Ok("2"), Ok("9.99"), Bad("1"), Bad("10")),
            Case("Constraint_ExclusiveBounds31", "3.1", "{'exclusiveMinimum':1,'exclusiveMaximum':10}",
                Ok("2"), Ok("9.99"), Bad("1"), Bad("10")),
            Case("Constraint_MultipleOf", "3.0", "{'multipleOf':0.1}",
                Ok("0.3"), Ok("0.5"), Ok("3"), Ok("'x'"), Bad("0.35"), Bad("0.05")),
            Case("Constraint_MultipleOfInteger", "3.0", "{'type':'integer','multipleOf':5}",
                Ok("10"), Ok("0"), Bad("7")),
            Case("Constraint_ArrayLength", "3.0", "{'minItems':1,'maxItems':2}",
                Ok("[1]"), Ok("[1,2]"), Ok("'abc'"), Bad("[]"), Bad("[1,2,3]")),
            Case("Constraint_UniqueItems", "3.0", "{'uniqueItems':true}",
                Ok("[1,2]"), Ok("[]"), Ok("[{'a':1},{'a':2}]"), Bad("[1,1]"), Bad("[1,1.0]"), Bad("[{'a':1,'b':2},{'b':2,'a':1}]")),
            Case("Constraint_ObjectSize", "3.0", "{'minProperties':1,'maxProperties':2}",
                Ok("{'a':1}"), Ok("[]"), Bad("{}"), Bad("{'a':1,'b':2,'c':3}")),
            Case("Format_Uuid", "3.0", "{'type':'string','format':'uuid'}",
                Ok("'00000000-0000-0000-0000-000000000001'"), Bad("'zz'"), Bad("'00000000-0000-0000-0000-00000000000'")),
            Case("Format_Date", "3.0", "{'type':'string','format':'date'}",
                Ok("'2026-01-31'"), Bad("'2026-02-30'"), Bad("'2026-1-3'"), Bad("1")),
            Case("Format_DateTime", "3.0", "{'type':'string','format':'date-time'}",
                Ok("'2026-01-01T00:00:00Z'"), Ok("'2026-01-01T10:20:30.5+09:00'"), Bad("'2026-01-01'"), Bad("'2026-01-01T25:00:00Z'")),
            Case("Format_Byte", "3.0", "{'type':'string','format':'byte'}",
                Ok("'aGVsbG8='"), Ok("''"), Bad("'aGVsbG8'"), Bad("'a$Vs'")),
            Case("Format_Int32", "3.0", "{'type':'integer','format':'int32'}",
                Ok("2147483647"), Ok("-2147483648"), Bad("2147483648"), Bad("1.5")),
            Case("Format_Int64", "3.0", "{'type':'integer','format':'int64'}",
                Ok("9223372036854775807"), Bad("9223372036854775808")),
            Case("Format_UnknownIsIgnored", "3.0", "{'type':'string','format':'email'}",
                Ok("'not an email'")),
            Case("Format_UuidOnlyChecksStrings", "3.0", "{'format':'uuid'}",
                Ok("5"), Ok("null"), Bad("'x'")),

            // ---- allOf / anyOf / oneOf / not -------------------------------------------------------------
            Case("AllOf_AllParts", "3.0",
                "{'allOf':[{'type':'object','required':['a']},{'properties':{'a':{'type':'integer'}}},{'minProperties':2}]}",
                Ok("{'a':1,'b':1}"), Bad("{'a':1}"), Bad("{'a':'x','b':1}"), Bad("{'b':1,'c':1}")),
            Case("AnyOf_AtLeastOne", "3.0", "{'anyOf':[{'type':'string'},{'type':'integer','minimum':5}]}",
                Ok("'a'"), Ok("5"), Ok("9"), Bad("4"), Bad("null"), Bad("true")),
            Case("AnyOf_AlwaysTrueMember", "3.0", "{'anyOf':[{},{'type':'string'}]}",
                Ok("1"), Ok("null"), Ok("'a'")),
            Case("OneOf_ExactlyOne", "3.0",
                "{'oneOf':[{'type':'integer'},{'type':'number','minimum':10},{'type':'string'}]}",
                Ok("1"), Ok("'a'"), Ok("10.5"), Bad("10"), Bad("null"), Bad("true"), Bad("{}")),
            Case("OneOf_AlwaysTrueMemberCountsAsMatch", "3.0", "{'oneOf':[{},{'type':'string'}]}",
                Ok("1"), Ok("null"), Bad("'a'")),
            Case("OneOf_Discriminating", "3.0",
                "{'oneOf':[{'required':['a']},{'required':['b']}]}",
                Ok("{'a':1}"), Ok("{'b':1}"), Bad("{'a':1,'b':1}"), Bad("{}"), Bad("5")),
            Case("Not_Type", "3.0", "{'not':{'type':'string'}}",
                Ok("1"), Ok("null"), Ok("{}"), Bad("'a'")),
            Case("Not_Required", "3.0", "{'type':'object','not':{'required':['x']}}",
                Ok("{}"), Ok("{'y':1}"), Bad("{'x':1}"), Bad("{'x':null,'y':1}"), Bad("5")),
            Case("Not_Empty", "3.0", "{'not':{}}",
                Bad("1"), Bad("null"), Bad("{}")),
            Case("Composition_OneOfNextToProperties", "3.0",
                "{'type':'object','properties':{'kind':{'type':'string','enum':['A','B']},'n':{'type':'integer','nullable':true}},'required':['kind']," +
                "'oneOf':[{'properties':{'kind':{'enum':['A']},'n':{'type':'integer'}},'required':['n']},{'properties':{'kind':{'enum':['B']},'n':{'enum':[null]}}}]}",
                Ok("{'kind':'A','n':1}"), Ok("{'kind':'B'}"), Ok("{'kind':'B','n':null}"),
                Bad("{'kind':'A'}"), Bad("{'kind':'A','n':null}"), Bad("{'kind':'B','n':1}"), Bad("{'n':1}")),
            Case("Composition_AllOfWithAnyOfParts", "3.0",
                "{'allOf':[{'anyOf':[{'required':['a']},{'required':['b']}]},{'anyOf':[{'required':['c']},{'required':['d']}]}]}",
                Ok("{'a':1,'c':1}"), Ok("{'b':1,'d':1}"), Bad("{'a':1}"), Bad("{'c':1,'d':1}")),
            Case("Composition_NotWithinAllOf", "3.0",
                "{'allOf':[{'type':'array','minItems':2},{'not':{'items':{'type':'string'}}}]}",
                Ok("[1,'a']"), Ok("[1,2]"), Bad("['a','b']"), Bad("[1]")),

            // ---- boolean schemas (3.1) -------------------------------------------------------------------
            Case("BooleanSchema_False", "3.1", "{'properties':{'a':false,'b':true}}",
                Ok("{}"), Ok("{'b':1}"), Bad("{'a':1}"), Bad("{'a':null}"), Ok("5")),
            Case("BooleanSchema_ItemsFalse", "3.1", "{'items':false}",
                Ok("[]"), Bad("[1]")),

            // ---- $ref ---------------------------------------------------------------------------------------
            WithComponents("Ref_Basic", "3.0", "{'type':'object','properties':{'s':{'$ref':'#/components/schemas/Str'}}}",
                "'Str':{'type':'string','minLength':2}",
                Ok("{'s':'ab'}"), Ok("{}"), Bad("{'s':'a'}"), Bad("{'s':1}")),
            WithComponents("Ref_SiblingsAreIgnoredIn30", "3.0", "{'$ref':'#/components/schemas/Str','minLength':10}",
                "'Str':{'type':'string'}",
                Ok("'a'"), Bad("1")),
            WithComponents("Ref_SiblingsApplyIn31", "3.1", "{'$ref':'#/components/schemas/Str','minLength':3,'description':'x'}",
                "'Str':{'type':'string'}",
                Ok("'abc'"), Bad("'a'"), Bad("1")),
            WithComponents("Ref_RecursiveTree", "3.0", "{'$ref':'#/components/schemas/Tree'}",
                "'Tree':{'type':'object','required':['value'],'properties':{'value':{'type':'integer'}," +
                "'children':{'type':'array','items':{'$ref':'#/components/schemas/Tree'}}}}",
                Ok("{'value':1}"), Ok("{'value':1,'children':[{'value':2,'children':[{'value':3}]}]}"),
                Bad("{'value':1,'children':[{'value':2,'children':[{}]}]}"), Bad("{'children':[]}"), Bad("{'value':'x'}")),
            WithComponents("Ref_MutualRecursion", "3.0", "{'$ref':'#/components/schemas/A'}",
                "'A':{'type':'object','properties':{'b':{'$ref':'#/components/schemas/B'}}}," +
                "'B':{'type':'object','required':['a'],'properties':{'a':{'$ref':'#/components/schemas/A'}}}",
                Ok("{}"), Ok("{'b':{'a':{}}}"), Bad("{'b':{}}"), Bad("{'b':{'a':{'b':{}}}}")),
            WithComponents("Ref_Alias", "3.0", "{'$ref':'#/components/schemas/Alias'}",
                "'Alias':{'$ref':'#/components/schemas/Str'},'Str':{'type':'string','maxLength':2}",
                Ok("'ab'"), Bad("'abc'"), Bad("1")),
            WithComponents("Ref_InComposition", "3.0",
                "{'oneOf':[{'$ref':'#/components/schemas/Str'},{'$ref':'#/components/schemas/Int'}]}",
                "'Str':{'type':'string'},'Int':{'type':'integer'}",
                Ok("'a'"), Ok("1"), Bad("null"), Bad("1.5")),

            // ---- readOnly / writeOnly --------------------------------------------------------------------
            Case("Direction_ReadOnlyAndWriteOnly", "3.0",
                "{'type':'object','required':['id','secret','name'],'properties':{'id':{'type':'string','readOnly':true}," +
                "'secret':{'type':'string','writeOnly':true},'name':{'type':'string'}}}",
                OkRequest("{'name':'n','secret':'s'}"), OkRequest("{'name':'n','secret':'s','id':'i'}"),
                BadRequest("{'name':'n','id':'i'}"), BadRequest("{'secret':'s'}"),
                OkResponse("{'name':'n','id':'i'}"), OkResponse("{'name':'n','id':'i','secret':'s'}"),
                BadResponse("{'name':'n','secret':'s'}"), BadResponse("{'id':'i'}")),
            WithComponents("Direction_ReadOnlyThroughRef", "3.0",
                "{'type':'object','required':['id'],'properties':{'id':{'$ref':'#/components/schemas/Id'}}}",
                "'Id':{'type':'string','readOnly':true}",
                OkRequest("{}"), BadRequest("{'id':1}"), BadResponse("{}"), OkResponse("{'id':'i'}")),

            // ---- OpenAPI 3.1 applicators -------------------------------------------------------------------
            Case("Applicator_PatternProperties", "3.1",
                "{'patternProperties':{'^x_':{'type':'integer'}},'properties':{'a':{}},'additionalProperties':false}",
                Ok("{'x_1':1}"), Ok("{'a':'s','x_2':2}"), Bad("{'x_1':'s'}"), Bad("{'y':1}")),
            Case("Applicator_PropertyNames", "3.1", "{'propertyNames':{'maxLength':3}}",
                Ok("{'ab':1}"), Ok("{}"), Bad("{'abcd':1}"), Ok("[1]")),
            Case("Applicator_Contains", "3.1", "{'contains':{'type':'integer'}}",
                Ok("['a',1]"), Bad("['a']"), Bad("[]"), Ok("'x'")),
            Case("Applicator_MinMaxContains", "3.1", "{'contains':{'type':'integer'},'minContains':2,'maxContains':3}",
                Ok("[1,'a',2]"), Ok("[1,2,3]"), Bad("[1,'a']"), Bad("[1,2,3,4]")),
            Case("Applicator_ZeroMinContains", "3.1", "{'contains':{'type':'integer'},'minContains':0}",
                Ok("[]"), Ok("['a']")),
            Case("Applicator_PrefixItems", "3.1",
                "{'prefixItems':[{'type':'integer'},{'type':'string'}],'items':{'type':'boolean'}}",
                Ok("[1,'a']"), Ok("[1,'a',true]"), Ok("[]"), Ok("[1]"), Bad("['a']"), Bad("[1,'a',1]"), Ok("{}")),
            Case("Applicator_PrefixItemsClosed", "3.1", "{'prefixItems':[{'type':'integer'}],'items':false}",
                Ok("[1]"), Ok("[]"), Bad("[1,2]")),
            Case("Applicator_DependentRequired", "3.1", "{'dependentRequired':{'a':['b','c']}}",
                Ok("{}"), Ok("{'b':1}"), Ok("{'a':1,'b':1,'c':1}"), Bad("{'a':1}"), Bad("{'a':1,'b':1}")),
        };

        internal static AssertionCase Get(string name) => All.Single(item => item.Name == name);

        internal static IEnumerable<object[]> Names() => All.Select(static item => new object[] { item.Name });
    }
}
