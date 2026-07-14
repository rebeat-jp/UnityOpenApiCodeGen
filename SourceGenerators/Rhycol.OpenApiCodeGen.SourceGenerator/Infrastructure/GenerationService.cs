using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// API クライアント生成の共通エントリポイント。
    /// Shared entry point for API client generation.
    /// </summary>
    internal sealed class GenerationService
    {
        private readonly ICSharpFileGenerator<OpenApiDocument> _openApiFileGenerator;
        private readonly ICSharpFileGenerator<SwaggerDocument> _swaggerFileGenerator;
        private readonly ICSharpCodeGenerator _codeGenerator;

        public GenerationService()
            : this(new OpenApiCSharpClientFileGenerator(), new SwaggerCSharpClientFileGenerator(), new RoslynCodeGenerator())
        {
        }

        public GenerationService(
            ICSharpFileGenerator<OpenApiDocument> openApiFileGenerator,
            ICSharpFileGenerator<SwaggerDocument> swaggerFileGenerator,
            ICSharpCodeGenerator codeGenerator)
        {
            _openApiFileGenerator = openApiFileGenerator ?? throw new ArgumentNullException(nameof(openApiFileGenerator));
            _swaggerFileGenerator = swaggerFileGenerator ?? throw new ArgumentNullException(nameof(swaggerFileGenerator));
            _codeGenerator = codeGenerator ?? throw new ArgumentNullException(nameof(codeGenerator));
        }

        /// <summary>
        /// 正規化済み OpenAPI node からクライアントコードを生成する。
        /// Generates client code from a normalized OpenAPI node.
        /// </summary>
        public IReadOnlyList<GeneratedFile> GenerateFromOpenApiNode(SpecNode root, ApiClientGenerateOption option)
        {
            if (root is null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (option is null)
            {
                throw new ArgumentNullException(nameof(option));
            }

            OpenApiDocument document = OpenApiDocumentMapper.Parse(root);
            CSharpFile file = _openApiFileGenerator.Generate(document, option);
            return new[] { _codeGenerator.Generate(file) };
        }

        /// <summary>
        /// 正規化済み OpenAPI または Swagger node からコードを生成する。
        /// Generates code from a normalized OpenAPI or Swagger node.
        /// </summary>
        public IReadOnlyList<GeneratedFile> GenerateFromApiDocument(SpecNode root, ApiClientGenerateOption option)
        {
            if (root is null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (option is null)
            {
                throw new ArgumentNullException(nameof(option));
            }

            ApiDocumentFormat format = DetectDocumentFormat(root);
            if (format == ApiDocumentFormat.OpenApi)
            {
                return GenerateFromOpenApiNode(root, option);
            }

            if (format == ApiDocumentFormat.Swagger)
            {
                SwaggerDocument document = new SwaggerDocumentMapper().Parse(root);
                CSharpFile file = _swaggerFileGenerator.Generate(document, option);
                return new[] { _codeGenerator.Generate(file) };
            }

            throw new FormatException("The normalized root is neither an OpenAPI nor a Swagger document.");
        }

        private static ApiDocumentFormat DetectDocumentFormat(SpecNode root)
        {
            if (root.ValueKind != SpecValueKind.Object)
            {
                return ApiDocumentFormat.Unknown;
            }

            if (root.TryGetProperty("openapi", out _))
            {
                return ApiDocumentFormat.OpenApi;
            }

            if (root.TryGetProperty("swagger", out _))
            {
                return ApiDocumentFormat.Swagger;
            }

            return ApiDocumentFormat.Unknown;
        }

        private enum ApiDocumentFormat
        {
            Unknown,
            OpenApi,
            Swagger
        }
    }
}
