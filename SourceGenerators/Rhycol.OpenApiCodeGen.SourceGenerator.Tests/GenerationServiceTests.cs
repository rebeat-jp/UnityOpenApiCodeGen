using System.Linq;
using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public class GenerationServiceTests
    {
        [Fact]
        public void GenerateFromOpenApiNode_EmitsMethodsWithDocsAndTypes()
        {
            var json = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Sample"", ""version"": ""1.0.0"" },
  ""paths"": {
    ""/items"": {
      ""get"": {
        ""operationId"": ""getItems"",
        ""summary"": ""Get items"",
        ""parameters"": [
          { ""name"": ""limit"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""integer"", ""format"": ""int32"" } },
          { ""name"": ""cursor"", ""in"": ""query"", ""required"": false, ""schema"": { ""type"": ""string"" } }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""OK"",
            ""content"": {
              ""application/json"": {
                ""schema"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
              }
            }
          }
        }
      },
      ""post"": {
        ""operationId"": ""createItem"",
        ""summary"": ""Create item"",
        ""requestBody"": {
          ""required"": true,
          ""content"": {
            ""application/json"": {
              ""schema"": { ""type"": ""string"", ""format"": ""date-time"" }
            }
          }
        },
        ""responses"": {
          ""201"": {
            ""description"": ""Created"",
            ""content"": {
              ""application/json"": {
                ""schema"": { ""type"": ""string"", ""format"": ""uuid"" }
              }
            }
          }
        }
      }
    }
  },
  ""components"": { ""schemas"": {} }
}";

            var option = new ApiClientGenerateOption
            {
                ApiName = "SampleApi",
                Namespace = "Rhycol.OpenApiCodeGen.Generated",
                UseNullableReferenceTypes = true
            };

            var service = new GenerationService();
            var files = service.GenerateFromOpenApiNode(TestBundleFactory.ReadRoot(json), option);

            Assert.Single(files);
            var content = files.Single().Content;

            Assert.Contains("namespace Rhycol.OpenApiCodeGen.Generated", content);
            Assert.Contains("class SampleApi", content);
            Assert.Contains("/// <summary>Get items</summary>", content);
            Assert.Contains("/// <remarks>operationId: getItems</remarks>", content);
            Assert.Contains("public System.Collections.Generic.IReadOnlyList<string> getItems(int limit, string? cursor)", content);
            Assert.Contains("/// <summary>Create item</summary>", content);
            Assert.Contains("public System.Guid createItem(System.DateTimeOffset body)", content);
        }
    }
}
