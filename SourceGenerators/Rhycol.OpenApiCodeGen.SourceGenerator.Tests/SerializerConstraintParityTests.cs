using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Differential tests: the same payloads and typed values go through the client generated for
    /// Newtonsoft.Json and the client generated for System.Text.Json (the constraint spec of
    /// Phase6ConstraintTests), and the decisions must be the same: accepted, or rejected for the same
    /// keyword. This pins the number lexeme rules (1.0, 1e2, 0.30000000000000004, huge integers), the code
    /// point counting, the JSON value equality of uniqueItems and the sub-schema keywords, which are the
    /// parts that differ most between a JToken and a JsonElement implementation.
    /// </summary>
    public sealed class SerializerConstraintParityTests
    {
        private static readonly Lazy<Assembly> Newtonsoft = new Lazy<Assembly>(
            () => Build(SerializerMatrixHarness.NewtonsoftJson));

        private static readonly Lazy<Assembly> SystemTextJson = new Lazy<Assembly>(
            () => Build(SerializerMatrixHarness.SystemTextJson));

        private static Assembly Build(string serializer)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                Phase6ConstraintTests.Spec31(),
                options: new Phase4CompilationOptions
                {
                    JsonSerializer = serializer,
                    Newtonsoft = serializer == SerializerMatrixHarness.NewtonsoftJson
                });
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        // The decision of a call: accepted, rejected for a keyword of a constraint ("violates X"), or
        // rejected for another reason (syntax, type, contract).
        private static async Task<string> Decide(Assembly api, string method, string? body, params object?[] arguments)
        {
            try
            {
                await Phase4BodyWireHarness.Call(
                    api,
                    method,
                    _ => body is null
                        ? Phase4BodyWireHarness.Respond(HttpStatusCode.NoContent)
                        : Phase4BodyWireHarness.Respond(HttpStatusCode.OK, body),
                    arguments).ConfigureAwait(false);
                return "ok";
            }
            catch (Exception exception) when (exception is ArgumentException)
            {
                return "request-violates:" + Keyword(exception.Message);
            }
            catch (Exception exception) when (
                exception is Newtonsoft.Json.JsonException || exception is System.Text.Json.JsonException)
            {
                int index = exception.Message.IndexOf(" violates ", StringComparison.Ordinal);
                return index < 0 ? "rejected" : "violates:" + Keyword(exception.Message);
            }
        }

        private static string Keyword(string message)
        {
            int start = message.IndexOf(" violates ", StringComparison.Ordinal);
            if (start < 0)
            {
                return "?";
            }

            start += " violates ".Length;
            return message.Substring(start, message.IndexOf(':', start) - start);
        }

        private static async Task<List<string>> Compare(IEnumerable<(string Method, string Body)> cases)
        {
            var differences = new List<string>();
            foreach ((string method, string body) in cases)
            {
                string left = await Decide(Newtonsoft.Value, method, body);
                string right = await Decide(SystemTextJson.Value, method, body);
                if (left != right)
                {
                    differences.Add(method + " " + body + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            return differences;
        }

        private static IEnumerable<(string, string)> Cases(string method, params string[] bodies)
        {
            return bodies.Select(body => (method, body));
        }

        [Fact]
        public async Task IntegerBoundsAgreeForEveryLexemeOfTheSameNumber()
        {
            List<string> differences = await Compare(Cases(
                "readCount",
                "1", "10", "0", "11", "1.0", "1e0", "1E0", "10.0", "1E1", "1e1", "10.5", "0.5", "-0", "-1", "1e2",
                "9.99e0", "1.0e1", "100e-1", "0.1e2", "\"5\"", "true", "null", "[]", "{}", "9007199254740993",
                "92233720368547758070", "1e400", "-1e400", "123456789012345678901234567890"));

            Assert.Empty(differences);
        }

        [Fact]
        public async Task DecimalBoundsAndMultipleOfAgreeForFloatingPointNoise()
        {
            List<string> differences = await Compare(
                Cases(
                    "readPrice",
                    "0.01", "99.99", "100", "0", "0.001", "1e-2", "99.99000000000001", "0.30000000000000004",
                    "50.5", "1e1", "1E+1", "0.1", "0.07", "1.005", "33.33", "99.995", "1e-5", "5e-324", "1e308",
                    "-0.01", "12345678901234567890123456789")
                .Concat(Cases(
                    "readStep",
                    "0.3", "0.30000000000000004", "0.1", "0", "1", "0.15", "1e-1", "3e-1", "0.7", "0.6000000000000001",
                    "123456789012345678901234567890", "1.23456789012345678901234567890", "1e300", "0.35", "-0.3",
                    "1e-30", "7E-1", "12345678.9", "0.1000000000000000055511151231257827")));

            Assert.Empty(differences);
        }

        [Fact]
        public async Task StringLengthPatternAndEnumAgreeForUnicode()
        {
            List<string> differences = await Compare(
                Cases(
                    "readName",
                    "\"\"", "\"ab\"", "\"abc\"", "\"abcde\"", "\"abcdef\"", "\"\\ud83d\\ude00\\ud83d\\ude00\\ud83d\\ude00\"",
                    "\"\\ud83d\\ude00\\ud83d\\ude00\"", "\"\\u00e9\\u00e9\\u00e9\"", "\"a\\u0000b\"",
                    "\"\\u3042\\u3044\\u3046\\u3048\\u304a\"", "\"\\u3042\\u3044\\u3046\\u3048\\u304a\\u304b\"", "5", "null", "[]")
                .Concat(Cases("readCode", "\"abc\"", "\"ABC\"", "\"ab1\"", "\"\"", "\"abc\\n\"", "\"\\u00e9\""))
                .Concat(Cases("readPartial", "\"xxabxx\"", "\"xyz\"", "\"\""))
                .Concat(Cases("readDigits", "\"123\"", "\"\\u0661\\u0662\\u0663\"", "\"\"", "\"12a\"", "\"123\\n\""))
                .Concat(Cases("readLetters", "\"\\u00e9a\"", "\"1\"", "\"\""))
                .Concat(Cases("readLevel", "\"low\"", "\"high\"", "\"medium\"", "\"other\"", "\"\"", "1", "\"LOW\"")));

            Assert.Empty(differences);
        }

        [Fact]
        public async Task AnUnpairedSurrogateIsTheOneDifferenceInTheAcceptedText()
        {
            // Json.NET reads "\ud83d" as a one character string; System.Text.Json has no representation for it
            // (it cannot deserialize such text either), so the response is rejected as malformed JSON.
            Assert.Equal("violates:minLength", await Decide(Newtonsoft.Value, "readName", "\"\\ud83d\""));
            Assert.Equal("rejected", await Decide(SystemTextJson.Value, "readName", "\"\\ud83d\""));
            Assert.Equal("ok", await Decide(Newtonsoft.Value, "readName", "\"\\ude00abc\""));
            Assert.Equal("rejected", await Decide(SystemTextJson.Value, "readName", "\"\\ude00abc\""));
            Assert.Equal("rejected", await Decide(SystemTextJson.Value, "readBag", "{\"\\ud83d\":1}"));
            Assert.Equal("rejected", await Decide(SystemTextJson.Value, "readAnyList", "[\"\\ud83d\"]"));

            // The scan of the escapes: a reversed pair, a high surrogate before another character, and an escaped
            // backslash that only looks like the start of an escape are told apart.
            Assert.Equal("rejected", await Decide(SystemTextJson.Value, "readName", "\"\\ude00\\ud83d\""));
            Assert.Equal("rejected", await Decide(SystemTextJson.Value, "readName", "\"\\ud83dxyz\""));
            Assert.Equal("rejected", await Decide(SystemTextJson.Value, "readName", "\"\\ud83d\\u0041\""));
            Assert.Equal("ok", await Decide(SystemTextJson.Value, "readName", "\"\\\\ud8\""));
            Assert.Equal("ok", await Decide(SystemTextJson.Value, "readName", "\"\\ud83d\\ude00x\\ud83d\\ude00\""));
            Assert.Equal("ok", await Decide(Newtonsoft.Value, "readName", "\"\\\\ud8\""));
            Assert.Equal("ok", await Decide(Newtonsoft.Value, "readName", "\"\\ud83d\\ude00x\\ud83d\\ude00\""));

            // In a request the encoder of System.Text.Json writes the unpaired surrogate as one replacement
            // character, so the length rule still sees one character.
            Assert.Equal("request-violates:minLength", await Decide(Newtonsoft.Value, "sendName", null, "\ud83d"));
            Assert.Equal("request-violates:minLength", await Decide(SystemTextJson.Value, "sendName", null, "\ud83d"));
        }

        [Fact]
        public async Task ArraysAgreeForSizeUniquenessContainsAndPrefixItems()
        {
            List<string> differences = await Compare(
                Cases(
                    "readTags",
                    "[]", "[\"a\"]", "[\"a\",\"a\"]", "[\"a\",\"b\",\"c\"]", "[\"a\",\"b\",\"c\",\"d\"]", "[1]", "[null]", "{}", "null")
                .Concat(Cases(
                    "readAnyList",
                    "[1,1.0]", "[1,\"1\"]", "[1,1e0]", "[0.1,0.10]", "[{\"a\":1,\"b\":2},{\"b\":2,\"a\":1}]", "[[1],[1.0]]",
                    "[null,null]", "[true,true]", "[true,false]", "[1e2,100]", "[\"a\",\"a\"]", "[-0,0]", "[1.5,1.50]", "[]",
                    "[{\"a\":1},{\"a\":2},[1],[1,2],null,true,false]", "[[1,2],[1,2]]",
                    "[{\"a\":[1,{\"b\":null}]},{\"a\":[1.0,{\"b\":null}]}]",
                    "[\"\",\"\"]", "[0,false]", "[12345678901234567890,12345678901234567890.0]", "[1e400,1e400]", "[{},{}]"))
                .Concat(Cases(
                    "readContaining",
                    "[6,7]", "[1,6]", "[6,7,8,9]", "[6.0,7.5]", "[5,5]", "[\"x\",6,7]", "[]", "[5.0,5e0]",
                    "[4.999999999999999,6]", "[1e2,2e2]"))
                .Concat(Cases(
                    "readTuple",
                    "[\"a\",1]", "[1,\"a\"]", "[\"a\"]", "[\"a\",1,2]", "[]", "[\"a\",1.0]", "[\"a\",1.5]"))
                .Concat(Cases("readTuple2", "[\"a\",1,2]", "[\"a\",\"b\"]", "[]", "[\"a\",1.0,2.5]", "[1]")));

            Assert.Empty(differences);
        }

        [Fact]
        public async Task ObjectsAgreeForPropertyCountsNamesPatternsAndDependencies()
        {
            List<string> differences = await Compare(
                Cases("readBag", "{}", "{\"a\":1}", "{\"a\":1,\"b\":2}", "{\"a\":1,\"b\":2,\"c\":3}", "[]", "null", "{\"a\":null}")
                .Concat(Cases("readKeyNames", "{\"abc\":1}", "{\"ABC\":1}", "{\"\":1}", "{\"abc\":1,\"X\":2}", "{}", "[]"))
                .Concat(Cases(
                    "readPat",
                    "{\"x-a\":1}", "{\"x-a\":\"s\"}", "{\"x-a\":1.0}", "{\"x-a\":1.5}", "{\"other\":\"s\"}", "{}", "{\"x-\":1e0}"))
                .Concat(Cases(
                    "readDep", "{\"a\":1,\"b\":2}", "{\"a\":1}", "{\"b\":1}", "{}", "{\"a\":null}", "{\"a\":1,\"b\":null}"))
                .Concat(Cases(
                    "readRec",
                    "{\"title\":\"abc\"}", "{\"title\":\"abc\",\"k\":1}", "{\"title\":\"ab\",\"k\":1}",
                    "{\"title\":\"abc\",\"alias\":\"abcde\",\"k\":1}", "{\"k\":1}", "{\"title\":null,\"k\":1}",
                    "{\"title\":\"abc\",\"alias\":\"abcd\"}")));

            Assert.Empty(differences);
        }

        [Fact]
        public async Task OneOfNotAndReferenceSiblingsAgree()
        {
            List<string> differences = await Compare(
                Cases("readPick", "\"abc\"", "\"ab\"", "\"a\"", "\"\"", "\"abcd\"", "\"x\"", "5", "null")
                .Concat(Cases("readNotShort", "\"abc\"", "\"ab\"", "\"\"", "\"a\"", "5", "null"))
                .Concat(Cases("readShort", "\"abc\"", "\"abcd\"", "\"abcde\"", "\"ab\""))
                .Concat(Cases("readTwoTags", "[\"a\"]", "[\"a\",\"b\"]", "[\"a\",\"a\"]", "[]"))
                .Concat(Cases(
                    "readRW",
                    "{\"wo\":\"abc\"}", "{\"wo\":\"ab\"}", "{\"wo\":\"abc\",\"ro\":\"x\"}", "{\"ro\":\"abc\"}",
                    "{\"wo\":\"abc\",\"plain\":1}")));

            Assert.Empty(differences);
        }

        [Fact]
        public async Task TypedRequestValuesAreCheckedTheSameWay()
        {
            var differences = new List<string>();
            async Task Check(string method, params object?[] arguments)
            {
                string left = await Decide(Newtonsoft.Value, method, null, arguments);
                string right = await Decide(SystemTextJson.Value, method, null, arguments);
                if (left != right)
                {
                    differences.Add(method + " " + string.Join(",", arguments) + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            foreach (int value in new[] { 0, 1, 10, 11, -1, int.MaxValue })
            {
                await Check("sendCount", value);
            }

            foreach (double value in new[] { 0.01, 99.99, 100d, 0d, 0.001, 0.1 + 0.2, 33.33, 50.5, 1e-5, 1e308, -0.01, 99.995 })
            {
                await Check("sendPrice", value);
            }

            foreach (double value in new[] { 0.3, 0.1 + 0.2, 0.1, 0d, 1d, 0.15, 0.7, 0.6000000000000001, 1e300, 0.35, -0.3, 1e-30 })
            {
                await Check("sendStep", value);
            }

            foreach (string value in new[]
            {
                "", "ab", "abc", "abcde", "abcdef", "\U0001F600\U0001F600\U0001F600", "\U0001F600\U0001F600", "\u3042\u3044\u3046"
            })
            {
                await Check("sendName", value);
            }

            foreach (string value in new[] { "abc", "ABC", "", "ab1" })
            {
                await Check("sendCode", value);
            }

            foreach (string[] value in new[]
            {
                Array.Empty<string>(), new[] { "a" }, new[] { "a", "a" }, new[] { "a", "b", "c" }, new[] { "a", "b", "c", "d" }
            })
            {
                await Check("sendTags", new List<string>(value));
            }

            Assert.Empty(differences);
        }

        [Fact]
        public async Task ConstraintsOfParametersHeadersAndTextBodiesAgree()
        {
            var differences = new List<string>();
            foreach (string value in new[] { "", "ab", "abc", "abcde", "abcdef" })
            {
                string left = await Decide(Newtonsoft.Value, "queryName", null, value);
                string right = await Decide(SystemTextJson.Value, "queryName", null, value);
                if (left != right)
                {
                    differences.Add("queryName " + value + ": " + left + " / " + right);
                }

                left = await Decide(Newtonsoft.Value, "sendText", null, value);
                right = await Decide(SystemTextJson.Value, "sendText", null, value);
                if (left != right)
                {
                    differences.Add("sendText " + value + ": " + left + " / " + right);
                }
            }

            foreach (string value in new[] { "", "ab", "abc", "abcde", "abcdef", "1" })
            {
                string left = await Decide(Newtonsoft.Value, "readText", value);
                string right = await Decide(SystemTextJson.Value, "readText", value);
                if (left != right)
                {
                    differences.Add("readText " + value + ": " + left + " / " + right);
                }
            }

            Assert.Empty(differences);
        }

        [Fact]
        public async Task TheDifferentialHarnessSeesDecisionsAtAll()
        {
            // A guard against a harness that compares "ok" with "ok" everywhere.
            Assert.Equal("ok", await Decide(SystemTextJson.Value, "readCount", "5"));
            Assert.Equal("violates:minimum", await Decide(SystemTextJson.Value, "readCount", "0"));
            Assert.Equal("violates:maximum", await Decide(Newtonsoft.Value, "readCount", "11"));
            Assert.Equal("request-violates:minLength", await Decide(SystemTextJson.Value, "sendName", null, "ab"));
            Assert.Equal("violates:uniqueItems", await Decide(SystemTextJson.Value, "readAnyList", "[1,1.0]"));
        }
    }
}
