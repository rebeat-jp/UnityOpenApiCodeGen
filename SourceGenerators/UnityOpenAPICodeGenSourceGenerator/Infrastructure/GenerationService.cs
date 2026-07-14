using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
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
        /// OpenAPI JSON からクライアントコードを生成する。
        /// Generates client code from OpenAPI JSON.
        /// </summary>
        public IReadOnlyList<GeneratedFile> GenerateFromOpenApiJson(string json, ApiClientGenerateOption option)
        {
            if (option is null)
            {
                throw new ArgumentNullException(nameof(option));
            }

            var document = JsonApiDocumentParser.Parse(json);
            var file = _openApiFileGenerator.Generate(document, option);
            return new[] { _codeGenerator.Generate(file) };
        }

        /// <summary>
        /// OpenAPI または Swagger を判別してコード生成し、指定フォルダへ保存する。
        /// Generates code from OpenAPI/Swagger and saves to the specified folder.
        /// </summary>
        public IReadOnlyList<GeneratedFile> GenerateToFolder(string json, ApiClientGenerateOption option, string outputDirectory)
        {
            if (option is null)
            {
                throw new ArgumentNullException(nameof(option));
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("Output directory is empty.", nameof(outputDirectory));
            }

            var files = GenerateFromApiDocument(json, option);
            Directory.CreateDirectory(outputDirectory);

            foreach (var file in files)
            {
                var path = Path.Combine(outputDirectory, file.FileName);
                File.WriteAllText(path, file.Content);
            }

            return files;
        }

        /// <summary>
        /// OpenAPI または Swagger を判別してコード生成する。
        /// Generates code from OpenAPI or Swagger document.
        /// </summary>
        public IReadOnlyList<GeneratedFile> GenerateFromApiDocument(string json, ApiClientGenerateOption option)
        {
            if (option is null)
            {
                throw new ArgumentNullException(nameof(option));
            }

            var format = DetectDocumentFormat(json);
            if (format == ApiDocumentFormat.OpenApi)
            {
                var document = JsonApiDocumentParser.Parse(json);
                var file = _openApiFileGenerator.Generate(document, option);
                return new[] { _codeGenerator.Generate(file) };
            }

            if (format == ApiDocumentFormat.Swagger)
            {
                var document = new SwaggerApiDocumentParser().Parse(json);
                var file = _swaggerFileGenerator.Generate(document, option);
                return new[] { _codeGenerator.Generate(file) };
            }

            throw new FormatException("Unknown API document format.");
        }

        /// <summary>
        /// JSON から OpenAPI/Swagger を判別する。
        /// Detects document format from JSON.
        /// </summary>
        private static ApiDocumentFormat DetectDocumentFormat(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("API document is empty.", nameof(json));
            }

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("API document root must be a JSON object.");
            }

            var root = document.RootElement;
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
