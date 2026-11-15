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
    /// decimal typed values (format: decimal) and uniqueItems over numbers, judged by both serializers.
    /// A typed decimal is checked with its exact value (more than 15 digits count); the number of a response
    /// is a double that is rounded to 15 digits. uniqueItems must find equal numbers whatever their text.
    /// </summary>
    public sealed class SerializerDecimalParityTests
    {
        private static string Operation(string name, string schema)
        {
            return "\"/send/" + name + "\":{\"post\":{\"operationId\":\"send" + name + "\",\"requestBody\":{\"required\":true," +
                   "\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/" + schema + "\"}}}}," +
                   "\"responses\":{\"204\":{\"description\":\"ok\"}}}}," +
                   "\"/read/" + name + "\":{\"get\":{\"operationId\":\"read" + name + "\",\"responses\":{\"200\":{\"description\":\"ok\"," +
                   "\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/" + schema + "\"}}}}}}}";
        }

        private static readonly string Spec =
            "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"D\",\"version\":\"1\"},\"servers\":[{\"url\":\"https://api.example.com\"}],\"paths\":{" +
            Operation("Dec", "Dec") + "," + Operation("DecStep", "DecStep") + "," + Operation("DecList", "DecList") + "," +
            Operation("AnyList", "AnyList") + "," + Operation("DoubleList", "DoubleList") + "," + Operation("FloatList", "FloatList") +
            "},\"components\":{\"schemas\":{" +
            "\"Dec\":{\"type\":\"number\",\"format\":\"decimal\",\"maximum\":0.1234567890123454,\"minimum\":-0.1234567890123454}," +
            "\"DecStep\":{\"type\":\"number\",\"format\":\"decimal\",\"multipleOf\":0.0000000000000001}," +
            "\"DecList\":{\"type\":\"array\",\"items\":{\"type\":\"number\",\"format\":\"decimal\"},\"uniqueItems\":true}," +
            "\"DoubleList\":{\"type\":\"array\",\"items\":{\"type\":\"number\"},\"uniqueItems\":true}," +
            "\"FloatList\":{\"type\":\"array\",\"items\":{\"type\":\"number\",\"format\":\"float\"},\"uniqueItems\":true}," +
            "\"AnyList\":{\"type\":\"array\",\"items\":{},\"uniqueItems\":true}}}}";

        private static readonly Lazy<Assembly> Newtonsoft = new Lazy<Assembly>(
            () => SerializerMatrixHarness.CompileText("decimal-parity", Spec, SerializerMatrixHarness.NewtonsoftJson));

        private static readonly Lazy<Assembly> SystemTextJson = new Lazy<Assembly>(
            () => SerializerMatrixHarness.CompileText("decimal-parity", Spec, SerializerMatrixHarness.SystemTextJson));

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
            catch (ArgumentException exception)
            {
                return "request-violates:" + Keyword(exception.Message);
            }
            catch (Exception exception) when (
                exception is Newtonsoft.Json.JsonException || exception is System.Text.Json.JsonException)
            {
                return exception.Message.Contains(" violates ") ? "violates:" + Keyword(exception.Message) : "rejected";
            }
        }

        private static string Keyword(string message)
        {
            int start = message.IndexOf(" violates ", StringComparison.Ordinal) + " violates ".Length;
            return message.Substring(start, message.IndexOf(':', start) - start);
        }

        [Fact]
        public async Task TypedDecimalIsCheckedWithAllItsDigits()
        {
            var differences = new List<string>();
            foreach (decimal value in new[]
            {
                0.12345678901234546m, 0.1234567890123454m, 0.12345678901234540m, 0.1234567890123453m, 0.1234567890123455m,
                0.12345678901234541m, -0.12345678901234546m, -0.1234567890123454m, 0m, 0.1m, 79228162514264337593543950335m,
                -0.12345678901234541m, 0.123456789012345400000000001m
            })
            {
                string left = await Decide(Newtonsoft.Value, "sendDec", null, value);
                string right = await Decide(SystemTextJson.Value, "sendDec", null, value);
                if (left != right)
                {
                    differences.Add("sendDec " + value + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
            // The case that a double based comparison lets pass.
            Assert.Equal("request-violates:maximum", await Decide(SystemTextJson.Value, "sendDec", null, 0.12345678901234546m));
        }

        [Fact]
        public async Task TypedDecimalMultipleOfAndListsAgree()
        {
            var differences = new List<string>();
            foreach (decimal value in new[] { 0.0000000000000001m, 0.0000000000000003m, 0.00000000000000015m, 1m, 0.12345678901234567m })
            {
                string left = await Decide(Newtonsoft.Value, "sendDecStep", null, value);
                string right = await Decide(SystemTextJson.Value, "sendDecStep", null, value);
                if (left != right)
                {
                    differences.Add("sendDecStep " + value + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            foreach (decimal[] value in new[]
            {
                new[] { 0.12345678901234567m, 0.12345678901234568m },
                new[] { 0.5m, 0.50m },
                new[] { 1m, 2m, 1.0m },
                new[] { 0.1234567890123456m, 0.12345678901234561m }
            })
            {
                string left = await Decide(Newtonsoft.Value, "sendDecList", null, new List<decimal>(value));
                string right = await Decide(SystemTextJson.Value, "sendDecList", null, new List<decimal>(value));
                if (left != right)
                {
                    differences.Add("sendDecList " + string.Join(",", value) + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }

        [Fact]
        public async Task ResponseNumbersKeepTheDoubleRoundingOfBothSerializers()
        {
            var differences = new List<string>();
            foreach (string body in new[]
            {
                "0.12345678901234546", "0.1234567890123454", "0.12345678901234", "-0.12345678901234546", "1e-1", "0.1234567890123454000001"
            })
            {
                string left = await Decide(Newtonsoft.Value, "readDec", body);
                string right = await Decide(SystemTextJson.Value, "readDec", body);
                if (left != right)
                {
                    differences.Add("readDec " + body + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }

        [Fact]
        public async Task UniqueItemsFindsEqualNumbersWhateverTheirText()
        {
            string[] duplicates =
            {
                "[1,1.0]", "[1,1e0,5]",
                "[0.1,1e-1]", "[100,1e2]", "[1.00000000000000011,1]",
                "[1e300,1e300]", "[1e-30,1e-30]", "[1e-30,1.0e-30]", "[5e-324,5e-324]",
                "[-0,0]", "[-0.0,0]"
            };
            var differences = new List<string>();
            foreach (string body in duplicates)
            {
                string left = await Decide(Newtonsoft.Value, "readAnyList", body);
                string right = await Decide(SystemTextJson.Value, "readAnyList", body);
                if (right != "violates:uniqueItems" || left != right)
                {
                    differences.Add(body + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }

        [Fact]
        public async Task UniqueItemsFindsTheSameHugeNumberWrittenAsAnIntegerAndAsAnExponent()
        {
            // 79000000000000000000000000000 fits a decimal and 7.9e28 (a double) does not; the hash of the numbers must not
            // depend on that, in either order, for negatives, and inside arrays and objects.
            var differences = new List<string>();
            foreach (string body in new[]
            {
                "[79000000000000000000000000000,7.9e28]", "[7.9e28,79000000000000000000000000000]",
                "[-79000000000000000000000000000,-7.9e28]", "[-7.9e28,-79000000000000000000000000000]",
                "[[79000000000000000000000000000],[7.9e28]]", "[{\"a\":79000000000000000000000000000},{\"a\":7.9e28}]",
                "[{\"a\":[7.9e28]},{\"a\":[79000000000000000000000000000]}]"
            })
            {
                string left = await Decide(Newtonsoft.Value, "readAnyList", body);
                string right = await Decide(SystemTextJson.Value, "readAnyList", body);
                if (left != "violates:uniqueItems" || right != "violates:uniqueItems")
                {
                    differences.Add(body + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }

        [Fact]
        public async Task UniqueItemsTreatsDecimalsThatDifferOnlyInScaleAsDuplicates()
        {
            // The same value with a different number of trailing zeros must hash the same.
            var differences = new List<string>();
            foreach (decimal[] value in new[]
            {
                new[] { 2.868980833353654999m, 2.8689808333536549990000000000m },
                new[] { 2.8689808333536549990000000000m, 2.868980833353654999m },
                new[] { 1m, 1.000000000000000000000000000m }
            })
            {
                string left = await Decide(Newtonsoft.Value, "sendDecList", null, new List<decimal>(value));
                string right = await Decide(SystemTextJson.Value, "sendDecList", null, new List<decimal>(value));
                if (left != "request-violates:uniqueItems" || right != "request-violates:uniqueItems")
                {
                    differences.Add("sendDecList " + string.Join(",", value) + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }

        [Fact]
        public async Task UniqueItemsFindsZeroAndNegativeZeroAndAnIntegerAndItsDecimalSpelling()
        {
            var differences = new List<string>();
            foreach (string body in new[] { "[0,-0.0]", "[-0.0,0]", "[1,1.0]", "[1.0,1]", "[0.0,-0]" })
            {
                string left = await Decide(Newtonsoft.Value, "readAnyList", body);
                string right = await Decide(SystemTextJson.Value, "readAnyList", body);
                if (left != "violates:uniqueItems" || right != "violates:uniqueItems")
                {
                    differences.Add("readAnyList " + body + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }

                string[] elements = body.Trim('[', ']').Split(',');
                string typedLeft = await Decide(
                    Newtonsoft.Value, "sendAnyList", null, AnyList(Newtonsoft.Value, SerializerMatrixHarness.NewtonsoftJson, elements));
                string typedRight = await Decide(
                    SystemTextJson.Value, "sendAnyList", null, AnyList(SystemTextJson.Value, SerializerMatrixHarness.SystemTextJson, elements));
                if (typedLeft != "request-violates:uniqueItems" || typedRight != "request-violates:uniqueItems")
                {
                    differences.Add("sendAnyList " + body + ": Newtonsoft=" + typedLeft + " SystemTextJson=" + typedRight);
                }
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }

        [Fact]
        public async Task UniqueItemsFindsAFloatAndTheDoubleThatHasTheSameValue()
        {
            // A typed float is a float inside the Newtonsoft.Json token (JValue) and a plain number in
            // System.Text.Json; both must agree with the double of the same text.
            var differences = new List<string>();
            foreach (float value in new[] { 0.1f, 0.5f, 1.1f, 3.25f })
            {
                string text = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                var newtonsoftMixed = new List<Newtonsoft.Json.Linq.JToken>
                {
                    new Newtonsoft.Json.Linq.JValue(value),
                    new Newtonsoft.Json.Linq.JValue(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture))
                };
                string mixed = await Decide(Newtonsoft.Value, "sendAnyList", null, newtonsoftMixed);
                string mixedReverse = await Decide(
                    Newtonsoft.Value, "sendAnyList", null, Enumerable.Reverse(newtonsoftMixed).ToList());
                string stj = await Decide(
                    SystemTextJson.Value, "sendAnyList", null, AnyList(SystemTextJson.Value, SerializerMatrixHarness.SystemTextJson, text, text));
                string response = await Decide(SystemTextJson.Value, "readAnyList", "[" + text + "," + text + "]");
                foreach (string decision in new[] { mixed, mixedReverse, stj })
                {
                    if (decision != "request-violates:uniqueItems")
                    {
                        differences.Add("float " + text + ": " + mixed + " / " + mixedReverse + " / " + stj);
                    }
                }

                if (response != "violates:uniqueItems")
                {
                    differences.Add("readAnyList " + text + ": " + response);
                }

                // The typed list of floats.
                string typedLeft = await Decide(Newtonsoft.Value, "sendFloatList", null, new List<float> { value, value });
                string typedRight = await Decide(SystemTextJson.Value, "sendFloatList", null, new List<float> { value, value });
                string distinctLeft = await Decide(Newtonsoft.Value, "sendFloatList", null, new List<float> { value, value + 1f });
                string distinctRight = await Decide(SystemTextJson.Value, "sendFloatList", null, new List<float> { value, value + 1f });
                if (typedLeft != "request-violates:uniqueItems" || typedRight != "request-violates:uniqueItems" ||
                    distinctLeft != "ok" || distinctRight != "ok")
                {
                    differences.Add("sendFloatList " + text + ": " + typedLeft + " / " + typedRight + " / " + distinctLeft + " / " + distinctRight);
                }
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }

        // The List of JToken / JsonElement that the sendAnyList parameter declares.
        private static System.Collections.IList AnyList(Assembly api, string serializer, params string[] jsons)
        {
            MethodInfo method = Phase4WireHarness.ClientType(api).GetMethod("sendAnyList")!;
            var list = (System.Collections.IList)Activator.CreateInstance(method.GetParameters()[0].ParameterType)!;
            foreach (string json in jsons)
            {
                list.Add(SerializerMatrixHarness.MakeAny(serializer, json));
            }

            return list;
        }

        [Fact]
        public async Task UniqueItemsKeepsDistinctNumbersAndAgreesOnTypedDoubles()
        {
            var differences = new List<string>();
            foreach (string body in new[] { "[1,2]", "[0.1,0.2]", "[1e300,1e301]", "[79000000000000000000000000000,7.8e28]", "[1,\"1\"]" })
            {
                string left = await Decide(Newtonsoft.Value, "readAnyList", body);
                string right = await Decide(SystemTextJson.Value, "readAnyList", body);
                if (left != "ok" || left != right)
                {
                    differences.Add(body + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            foreach (double[] value in new[] { new[] { 0.1, 0.1 }, new[] { 0.1, 0.2 }, new[] { 1e300, 1e300 }, new[] { 0.1 + 0.2, 0.3 } })
            {
                string left = await Decide(Newtonsoft.Value, "sendDoubleList", null, new List<double>(value));
                string right = await Decide(SystemTextJson.Value, "sendDoubleList", null, new List<double>(value));
                if (left != right)
                {
                    differences.Add("sendDoubleList " + string.Join(",", value) + ": Newtonsoft=" + left + " SystemTextJson=" + right);
                }
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }
    }
}
