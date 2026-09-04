using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// インクリメンタル Source Generator の入口。
    /// Entry point for the incremental source generator.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class ApiClientCodeGenerator : IIncrementalGenerator
    {
        internal const string AdditionalFileSuffix = ".Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile";
        internal const string AttributeMetadataName =
            "Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute";

        private const int JsonDocumentFormat = 0;
        private const int YamlDocumentFormat = 1;

        private static readonly DiagnosticDescriptor MalformedBundle = new(
            "OACG001",
            "Malformed normalized spec bundle",
            "Normalized spec bundle '{0}' is malformed: {1}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnsupportedBundleVersion = new(
            "OACG002",
            "Unsupported normalized spec bundle version",
            "Normalized spec bundle '{0}' uses unsupported format version '{1}'.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor GenerationFailed = new(
            "OACG003",
            "OpenAPI generation failed",
            "OpenAPI generation failed for '{0}' at logical path '{1}': {2}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DuplicateBundleSpecId = new(
            "OACG004",
            "Duplicate normalized spec bundle ID",
            "Spec ID '{0}' is declared by multiple normalized spec bundles: {1}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor InvalidClientDefinition = new(
            "OACG005",
            "Invalid OpenAPI client definition",
            "OpenAPI client definition on '{0}' is invalid: {1}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingBundle = new(
            "OACG006",
            "Missing normalized spec bundle",
            "No normalized spec bundle was found for Spec ID '{0}'.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DuplicateDefinitionSpecId = new(
            "OACG007",
            "Duplicate OpenAPI client definition ID",
            "Spec ID '{0}' is used by multiple OpenAPI client definitions.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnsupportedDocumentFormat = new(
            "OACG008",
            "Unsupported OpenAPI document format",
            "OpenAPI client definition for Spec ID '{0}' uses unsupported document format value '{1}'. " +
            "The Source Generator supports Json (0) and Yaml (1).",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor BundleSpecIdMismatch = new(
            "OACG009",
            "Normalized spec bundle ID mismatch",
            "Normalized spec bundle '{0}' declares Spec ID '{1}', but the AdditionalFile name declares '{2}'.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor InvalidOpenApiDocument = new(
            "OACG100",
            "Invalid OpenAPI document",
            "OpenAPI validation failed for '{0}' at logical path '{1}': {2}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnsupportedOpenApiElement = new(
            "OACG101",
            "Unsupported OpenAPI element",
            "OpenAPI validation failed for '{0}' at logical path '{1}': {2}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnresolvedOpenApiReference = new(
            "OACG102",
            "Unresolved OpenAPI reference",
            "OpenAPI validation failed for '{0}' at logical path '{1}': {2}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor CyclicOpenApiReference = new(
            "OACG103",
            "Cyclic OpenAPI reference",
            "OpenAPI validation failed for '{0}' at logical path '{1}': {2}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ExternalOpenApiReference = new(
            "OACG104",
            "External OpenAPI reference is unsupported",
            "OpenAPI validation failed for '{0}' at logical path '{1}': {2}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor InconsistentOpenApiResponse = new(
            "OACG105",
            "Inconsistent OpenAPI response contract",
            "OpenAPI validation failed for '{0}' at logical path '{1}': {2}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor InvalidGeneratedIdentifier = new(
            "OACG106",
            "Invalid generated API identifier",
            "OpenAPI validation failed for '{0}' at logical path '{1}': {2}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// ジェネレータの初期化。
        /// Initializes the generator.
        /// </summary>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<OpenApiClientDefinitionInput> definitions =
                context.SyntaxProvider.ForAttributeWithMetadataName(
                        AttributeMetadataName,
                        static (node, _) => node is TypeDeclarationSyntax,
                        static (attributeContext, cancellationToken) =>
                            CreateDefinitionInput(attributeContext, cancellationToken))
                    .WithTrackingName("OpenApiClientDefinitions");

            context.RegisterSourceOutput(
                definitions.Where(static definition =>
                    definition.Status != OpenApiClientDefinitionInput.DefinitionStatus.Valid),
                static (productionContext, definition) =>
                    ReportDefinitionDiagnostic(productionContext, definition));

            IncrementalValuesProvider<NormalizedSpecCandidate> bundles =
                context.AdditionalTextsProvider
                    .Where(static text =>
                        text.Path.EndsWith(AdditionalFileSuffix, StringComparison.Ordinal))
                    .Select(static (text, cancellationToken) => new NormalizedSpecInput(
                        text.Path,
                        text.GetText(cancellationToken)?.ToString() ?? string.Empty))
                    .WithTrackingName("OpenApiReadNormalizedBundle")
                    .Select(static (input, _) => ParseBundle(input))
                    .WithTrackingName("OpenApiParseNormalizedBundle");

            context.RegisterSourceOutput(
                bundles.Where(static bundle =>
                    bundle.Status != NormalizedSpecCandidate.CandidateStatus.Valid),
                static (productionContext, bundle) =>
                    ReportBundleDiagnostic(productionContext, bundle));

            IncrementalValuesProvider<OpenApiGenerationWorkItem> workItems =
                definitions
                    .Where(static definition =>
                        definition.Status == OpenApiClientDefinitionInput.DefinitionStatus.Valid)
                    .Collect()
                    .Combine(bundles.Collect())
                    .SelectMany(static (input, _) => CreateWorkItems(input.Left, input.Right))
                    .WithTrackingName("OpenApiMatchDefinitionToBundle");

            context.RegisterSourceOutput(
                workItems,
                static (productionContext, workItem) =>
                    ExecuteWorkItem(productionContext, workItem));
        }

        private static OpenApiClientDefinitionInput CreateDefinitionInput(
            GeneratorAttributeSyntaxContext context,
            CancellationToken cancellationToken)
        {
            string targetDisplayName = context.TargetSymbol.ToDisplayString(
                SymbolDisplayFormat.CSharpErrorMessageFormat);
            Location location = context.TargetNode.GetLocation();
            if (context.Attributes.Length > 0)
            {
                SyntaxReference? syntaxReference = context.Attributes[0].ApplicationSyntaxReference;
                if (syntaxReference != null)
                {
                    location = syntaxReference.GetSyntax(cancellationToken).GetLocation();
                }
            }

            DiagnosticLocation diagnosticLocation = DiagnosticLocation.FromLocation(location);
            if (context.TargetSymbol is not INamedTypeSymbol namedType ||
                namedType.TypeKind != TypeKind.Class ||
                context.TargetNode is not ClassDeclarationSyntax classDeclaration ||
                namedType.ContainingType != null)
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "the attribute target must be a top-level class",
                    diagnosticLocation);
            }

            if (!classDeclaration.Modifiers.Any(static modifier =>
                    modifier.IsKind(SyntaxKind.PartialKeyword)))
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "the attribute target must be a partial class",
                    diagnosticLocation);
            }

            if (context.Attributes.Length != 1)
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "exactly one OpenApiClientDefinitionAttribute is required per class",
                    diagnosticLocation);
            }

            ImmutableArray<TypedConstant> arguments = context.Attributes[0].ConstructorArguments;
            if (arguments.Length != 4 ||
                !TryReadString(arguments[0], out string specId) ||
                !TryReadString(arguments[1], out string apiName) ||
                !TryReadString(arguments[2], out string generatedNamespace) ||
                arguments[3].Kind == TypedConstantKind.Error ||
                arguments[3].Value is not int documentFormat)
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "constructor arguments must be (string specId, string apiName, " +
                    "string generatedNamespace, OpenApiDocumentFormat documentFormat)",
                    diagnosticLocation);
            }

            if (!IsLowerHex(specId, 32))
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "specId must be a lower-case Guid N value",
                    diagnosticLocation);
            }

            if (!SyntaxFacts.IsValidIdentifier(apiName))
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "apiName must be a valid C# identifier",
                    diagnosticLocation);
            }

            if (!IsValidNamespace(generatedNamespace))
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "generatedNamespace must be a non-empty dot-separated C# namespace",
                    diagnosticLocation);
            }

            if (!string.Equals(namedType.Name, apiName, StringComparison.Ordinal))
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "apiName must match the attributed partial class name",
                    diagnosticLocation);
            }

            string targetNamespace = namedType.ContainingNamespace.ToDisplayString();
            if (!string.Equals(targetNamespace, generatedNamespace, StringComparison.Ordinal))
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "generatedNamespace must match the attributed partial class namespace",
                    diagnosticLocation);
            }

            if (documentFormat != JsonDocumentFormat && documentFormat != YamlDocumentFormat)
            {
                return OpenApiClientDefinitionInput.CreateUnsupportedDocumentFormat(
                    specId,
                    apiName,
                    generatedNamespace,
                    documentFormat,
                    targetDisplayName,
                    diagnosticLocation);
            }

            return OpenApiClientDefinitionInput.CreateValid(
                specId,
                apiName,
                generatedNamespace,
                documentFormat,
                targetDisplayName,
                diagnosticLocation);
        }

        private static bool TryReadString(TypedConstant argument, out string value)
        {
            if (argument.Kind != TypedConstantKind.Error && argument.Value is string stringValue)
            {
                value = stringValue;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static bool IsValidNamespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] segments = value.Split(new[] { '.' }, StringSplitOptions.None);
            for (int index = 0; index < segments.Length; index++)
            {
                if (!SyntaxFacts.IsValidIdentifier(segments[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsLowerHex(string value, int length)
        {
            if (value.Length != length)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        private static NormalizedSpecCandidate ParseBundle(NormalizedSpecInput input)
        {
            try
            {
                NormalizedSpecBundle bundle = NormalizedSpecBundleReader.Read(input.Content);
                string fileSpecId = GetFileSpecId(input.Path);
                return string.Equals(fileSpecId, bundle.SpecId, StringComparison.Ordinal)
                    ? NormalizedSpecCandidate.CreateValid(input, bundle, fileSpecId)
                    : NormalizedSpecCandidate.CreateSpecIdMismatch(input, bundle, fileSpecId);
            }
            catch (UnsupportedNormalizedSpecVersionException exception)
            {
                return NormalizedSpecCandidate.CreateUnsupportedVersion(
                    input,
                    exception.Version,
                    exception.Line,
                    exception.Column);
            }
            catch (NormalizedSpecBundleFormatException exception)
            {
                return NormalizedSpecCandidate.CreateMalformed(
                    input,
                    exception.Message,
                    exception.Line,
                    exception.Column);
            }
            catch (Exception exception)
            {
                return NormalizedSpecCandidate.CreateMalformed(input, exception.Message, 1, 1);
            }
        }

        private static string GetFileSpecId(string path)
        {
            int slashIndex = path.LastIndexOf('/');
            int backslashIndex = path.LastIndexOf('\\');
            int fileNameIndex = Math.Max(slashIndex, backslashIndex) + 1;
            int specIdLength = path.Length - fileNameIndex - AdditionalFileSuffix.Length;
            return specIdLength <= 0
                ? string.Empty
                : path.Substring(fileNameIndex, specIdLength);
        }

        private static ImmutableArray<OpenApiGenerationWorkItem> CreateWorkItems(
            ImmutableArray<OpenApiClientDefinitionInput> definitions,
            ImmutableArray<NormalizedSpecCandidate> bundles)
        {
            var builder = ImmutableArray.CreateBuilder<OpenApiGenerationWorkItem>();
            var definitionsBySpecId = new Dictionary<string, List<OpenApiClientDefinitionInput>>(
                StringComparer.Ordinal);
            foreach (OpenApiClientDefinitionInput definition in definitions)
            {
                if (!definitionsBySpecId.TryGetValue(
                        definition.SpecId,
                        out List<OpenApiClientDefinitionInput>? matchingDefinitions))
                {
                    matchingDefinitions = new List<OpenApiClientDefinitionInput>();
                    definitionsBySpecId.Add(definition.SpecId, matchingDefinitions);
                }

                matchingDefinitions.Add(definition);
            }

            var bundlesBySpecId = new Dictionary<string, List<NormalizedSpecCandidate>>(
                StringComparer.Ordinal);
            var unavailableBundleFileSpecIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (NormalizedSpecCandidate bundle in bundles)
            {
                if (!bundle.HasParsedBundle)
                {
                    unavailableBundleFileSpecIds.Add(GetFileSpecId(bundle.Input.Path));
                    continue;
                }

                if (bundle.Status == NormalizedSpecCandidate.CandidateStatus.SpecIdMismatch)
                {
                    // The definition normally refers to the ID encoded in the file name.
                    // OACG009 already explains why this input cannot be used for that ID.
                    unavailableBundleFileSpecIds.Add(bundle.FileSpecId);
                }

                string specId = bundle.Bundle!.SpecId;
                if (!bundlesBySpecId.TryGetValue(
                        specId,
                        out List<NormalizedSpecCandidate>? matchingBundles))
                {
                    matchingBundles = new List<NormalizedSpecCandidate>();
                    bundlesBySpecId.Add(specId, matchingBundles);
                }

                matchingBundles.Add(bundle);
            }

            foreach (string specId in bundlesBySpecId.Keys.OrderBy(
                         static value => value,
                         StringComparer.Ordinal))
            {
                List<NormalizedSpecCandidate> matchingBundles = bundlesBySpecId[specId];
                if (matchingBundles.Count <= 1)
                {
                    continue;
                }

                string paths = string.Join(
                    ", ",
                    matchingBundles.Select(static bundle => bundle.Input.Path)
                        .OrderBy(static path => path, StringComparer.Ordinal));
                builder.Add(OpenApiGenerationWorkItem.CreateDuplicateBundle(specId, paths));
            }

            foreach (string specId in definitionsBySpecId.Keys.OrderBy(
                         static value => value,
                         StringComparer.Ordinal))
            {
                List<OpenApiClientDefinitionInput> matchingDefinitions = definitionsBySpecId[specId];
                if (matchingDefinitions.Count > 1)
                {
                    foreach (OpenApiClientDefinitionInput definition in matchingDefinitions.OrderBy(
                                 static value => value.TargetDisplayName,
                                 StringComparer.Ordinal))
                    {
                        builder.Add(OpenApiGenerationWorkItem.CreateDuplicateDefinition(definition));
                    }

                    continue;
                }

                OpenApiClientDefinitionInput singleDefinition = matchingDefinitions[0];
                if (!bundlesBySpecId.TryGetValue(
                        specId,
                        out List<NormalizedSpecCandidate>? matchingBundles))
                {
                    // The file name is the only available ID when parsing fails. Suppress a
                    // cascading missing-input error only for the matching definition.
                    if (!unavailableBundleFileSpecIds.Contains(specId))
                    {
                        builder.Add(OpenApiGenerationWorkItem.CreateMissingBundle(singleDefinition));
                    }

                    continue;
                }

                if (matchingBundles.Count != 1)
                {
                    continue;
                }

                NormalizedSpecCandidate singleBundle = matchingBundles[0];
                if (singleBundle.Status == NormalizedSpecCandidate.CandidateStatus.SpecIdMismatch)
                {
                    continue;
                }

                builder.Add(OpenApiGenerationWorkItem.CreateGeneration(singleDefinition, singleBundle));
            }

            return builder.ToImmutable();
        }

        private static void ReportDefinitionDiagnostic(
            SourceProductionContext context,
            OpenApiClientDefinitionInput definition)
        {
            if (definition.Status == OpenApiClientDefinitionInput.DefinitionStatus.Invalid)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidClientDefinition,
                    definition.Location.ToLocation(),
                    definition.TargetDisplayName,
                    definition.ValidationMessage));
                return;
            }

            if (definition.Status ==
                OpenApiClientDefinitionInput.DefinitionStatus.UnsupportedDocumentFormat)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedDocumentFormat,
                    definition.Location.ToLocation(),
                    definition.SpecId,
                    definition.DocumentFormat));
            }
        }

        private static void ReportBundleDiagnostic(
            SourceProductionContext context,
            NormalizedSpecCandidate bundle)
        {
            if (bundle.Status == NormalizedSpecCandidate.CandidateStatus.UnsupportedVersion)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedBundleVersion,
                    CreateExternalLocation(bundle.Input.Path, bundle.Line, bundle.Column),
                    bundle.Input.Path,
                    bundle.Version));
                return;
            }

            if (bundle.Status == NormalizedSpecCandidate.CandidateStatus.Malformed)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MalformedBundle,
                    CreateExternalLocation(bundle.Input.Path, bundle.Line, bundle.Column),
                    bundle.Input.Path,
                    bundle.Detail));
                return;
            }

            if (bundle.Status == NormalizedSpecCandidate.CandidateStatus.SpecIdMismatch)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    BundleSpecIdMismatch,
                    CreateExternalLocation(bundle.Input.Path, 1, 1),
                    bundle.Input.Path,
                    bundle.Bundle!.SpecId,
                    bundle.FileSpecId));
            }
        }

        private static void ExecuteWorkItem(
            SourceProductionContext context,
            OpenApiGenerationWorkItem workItem)
        {
            switch (workItem.Kind)
            {
                case OpenApiGenerationWorkItem.WorkItemKind.MissingBundle:
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingBundle,
                        workItem.Definition.Location.ToLocation(),
                        workItem.SpecId));
                    return;
                case OpenApiGenerationWorkItem.WorkItemKind.DuplicateBundle:
                    context.ReportDiagnostic(Diagnostic.Create(
                        DuplicateBundleSpecId,
                        Location.None,
                        workItem.SpecId,
                        workItem.Detail));
                    return;
                case OpenApiGenerationWorkItem.WorkItemKind.DuplicateDefinition:
                    context.ReportDiagnostic(Diagnostic.Create(
                        DuplicateDefinitionSpecId,
                        workItem.Definition.Location.ToLocation(),
                        workItem.SpecId));
                    return;
                case OpenApiGenerationWorkItem.WorkItemKind.Generate:
                    Generate(context, workItem);
                    return;
                default:
                    throw new InvalidOperationException("Unknown OpenAPI generator work item.");
            }
        }

        private static void Generate(
            SourceProductionContext context,
            OpenApiGenerationWorkItem workItem)
        {
            NormalizedSpecBundle bundle = workItem.Bundle.Bundle!;
            try
            {
                var service = new GenerationService();
                IReadOnlyList<GeneratedFile> files = bundle.IsMultiDocument
                    ? service.GenerateMvpFromOpenApiBundle(bundle, workItem.Definition.Options)
                    : service.GenerateMvpFromOpenApiNode(bundle.Root, workItem.Definition.Options);
                for (int index = 0; index < files.Count; index++)
                {
                    GeneratedFile file = files[index];
                    context.AddSource(
                        CreateHintName(workItem.SpecId, file.FileName, index),
                        SourceText.From(file.Content, Encoding.UTF8));
                }
            }
            catch (OpenApiSemanticException exception)
            {
                string sourcePath = string.IsNullOrEmpty(exception.Location.SourcePath)
                    ? bundle.SourcePath
                    : exception.Location.SourcePath;
                IEnumerable<Location> additionalLocations = exception.AdditionalLocations
                    .Select(location => CreateExternalLocation(
                        string.IsNullOrEmpty(location.SourcePath) ? bundle.SourcePath : location.SourcePath,
                        location.Line,
                        location.Column))
                    .ToArray();
                context.ReportDiagnostic(Diagnostic.Create(
                    GetSemanticDescriptor(exception.Kind),
                    CreateExternalLocation(sourcePath, exception.Location.Line, exception.Location.Column),
                    additionalLocations,
                    sourcePath,
                    exception.Location.LogicalPath,
                    exception.Message));
            }
            catch (Exception exception)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GenerationFailed,
                    CreateExternalLocation(bundle.SourcePath, bundle.Root.Line, bundle.Root.Column),
                    bundle.SourcePath,
                    bundle.Root.LogicalPath,
                    exception.Message));
            }
        }

        private static string CreateHintName(string specId, string fileName, int index)
        {
            var builder = new StringBuilder();
            builder.Append("Rhycol.OpenApiCodeGen.");
            builder.Append(specId);
            builder.Append('.');
            builder.Append(index);
            builder.Append('.');

            if (string.IsNullOrEmpty(fileName))
            {
                builder.Append("Generated.g.cs");
                return builder.ToString();
            }

            for (int characterIndex = 0; characterIndex < fileName.Length; characterIndex++)
            {
                char character = fileName[characterIndex];
                builder.Append(char.IsLetterOrDigit(character) ||
                               character == '.' ||
                               character == '_' ||
                               character == '-'
                    ? character
                    : '_');
            }

            return builder.ToString();
        }

        private static Location CreateExternalLocation(string path, int line, int column)
        {
            var position = new LinePosition(Math.Max(0, line - 1), Math.Max(0, column - 1));
            return Location.Create(
                path,
                new TextSpan(0, 0),
                new LinePositionSpan(position, position));
        }

        private static DiagnosticDescriptor GetSemanticDescriptor(OpenApiSemanticErrorKind kind)
        {
            switch (kind)
            {
                case OpenApiSemanticErrorKind.InvalidDocument:
                    return InvalidOpenApiDocument;
                case OpenApiSemanticErrorKind.UnsupportedElement:
                    return UnsupportedOpenApiElement;
                case OpenApiSemanticErrorKind.UnresolvedReference:
                    return UnresolvedOpenApiReference;
                case OpenApiSemanticErrorKind.CyclicReference:
                    return CyclicOpenApiReference;
                case OpenApiSemanticErrorKind.ExternalReference:
                    return ExternalOpenApiReference;
                case OpenApiSemanticErrorKind.InconsistentResponse:
                    return InconsistentOpenApiResponse;
                case OpenApiSemanticErrorKind.InvalidIdentifier:
                    return InvalidGeneratedIdentifier;
                default:
                    return InvalidOpenApiDocument;
            }
        }
    }
}
