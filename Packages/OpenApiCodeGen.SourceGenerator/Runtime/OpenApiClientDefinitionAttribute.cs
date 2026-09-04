using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Declares the OpenAPI specification used to generate a client for a partial class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OpenApiClientDefinitionAttribute : Attribute
    {
        public OpenApiClientDefinitionAttribute(
            string specId,
            string apiName,
            string generatedNamespace,
            OpenApiDocumentFormat documentFormat)
        {
            SpecId = specId;
            ApiName = apiName;
            GeneratedNamespace = generatedNamespace;
            DocumentFormat = documentFormat;
        }

        public string SpecId { get; }

        public string ApiName { get; }

        public string GeneratedNamespace { get; }

        public OpenApiDocumentFormat DocumentFormat { get; }
    }
}
