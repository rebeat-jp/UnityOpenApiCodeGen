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
    public sealed partial class ApiClientCodeGenerator : IIncrementalGenerator
    {
        internal const string AdditionalFileSuffix = ".Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile";
        internal const string AttributeMetadataName =
            "Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute";

        private const int JsonDocumentFormat = 0;
        private const int YamlDocumentFormat = 1;

        private static readonly HashSet<string> ReservedApiNames = new(StringComparer.Ordinal)
        {
            "_httpClient", "_baseUrl", "DateOnlyJsonConverterInstance",
            "CreateRequestUri", "CombineAbsoluteUri", "CombinePaths", "CombineQueries",
            "AppendQuery", "SplitPathAndQuery", "ValidatePathSegments",
            "CreateJsonContent", "ConvertToString", "DateOnlyJsonConverter",
            "ReadJsonResponseBodyAsync", "HasPreamble", "Servers", "BuildServerUrl",
            "ExpandServerUrl", "OperationServerUrls", "ResolveOperationServerUrl",
            "OperationNames", "GetOperationServers", "BuildOperationServerUrl"
        };

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

        private static readonly DiagnosticDescriptor UnsupportedLibrarySelection = new(
            "OACG010",
            "Unsupported library selection",
            "OpenAPI client definition for Spec ID '{0}' uses an unknown or unsupported library selection: {1}.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor JsonLibraryNotReferenced = new(
            "OACG011",
            "JSON library is not referenced",
            "OpenAPI client definition for Spec ID '{0}' selects JSON serializer '{1}', but the compilation does not reference {2} " +
            "(public type '{3}' was not found). Add the package that provides it.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor HttpImplementationNotReferenced = new(
            "OACG012",
            "HTTP implementation is not referenced",
            "OpenAPI client definition for Spec ID '{0}' selects HTTP transport '{1}', but the compilation does not reference " +
            "the required public type '{2}'.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor LibraryVersionTooLow = new(
            "OACG013",
            "Referenced library version is too low",
            "OpenAPI client definition for Spec ID '{0}' selects JSON serializer '{1}', which requires {2} version {3} or later, " +
            "but the compilation references version {4}.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor HttpClientOnWebGl = new(
            "OACG014",
            "HttpClient is not supported on WebGL",
            "OpenAPI client definition for Spec ID '{0}' uses the HttpClient transport while UNITY_WEBGL is defined. " +
            "HttpClient does not work in WebGL player builds; select the UnityWebRequest transport instead.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Warning,
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

        private static readonly DiagnosticDescriptor TypeNameCollisionDiagnostic = new(
            "OACG107",
            "Generated type name collision",
            "OpenAPI schema type '{0}' was generated as '{1}' because an earlier emitted declaration already owns the requested name in root-first, documentId/pointer order.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor IgnoredOpenApiElementDiagnostic = new(
            "OACG108",
            "Ignored OpenAPI element",
            "OpenAPI element ignored for '{0}' at logical path '{1}': {2}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Warning,
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

            IncrementalValueProvider<LibraryAvailability> libraries =
                context.CompilationProvider
                    .Combine(context.ParseOptionsProvider)
                    .Select(static (input, _) => LibraryAvailability.Create(input.Left, input.Right))
                    .WithTrackingName("OpenApiLibraryAvailability");

            context.RegisterSourceOutput(
                definitions
                    .Where(static definition =>
                        definition.Status == OpenApiClientDefinitionInput.DefinitionStatus.Valid)
                    .Combine(libraries),
                static (productionContext, input) =>
                    ReportLibrarySelectionDiagnostics(productionContext, input.Left, input.Right));

            IncrementalValuesProvider<OpenApiGenerationWorkItem> workItems =
                definitions
                    .Where(static definition =>
                        definition.Status == OpenApiClientDefinitionInput.DefinitionStatus.Valid)
                    .Collect()
                    .Combine(bundles.Collect())
                    .SelectMany(static (input, _) => CreateWorkItems(input.Left, input.Right))
                    .WithTrackingName("OpenApiMatchDefinitionToBundle");

            context.RegisterSourceOutput(
                workItems.Combine(libraries),
                (productionContext, input) =>
                    ExecuteWorkItem(productionContext, input.Left, input.Right, _testWarningInjector));
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

            if (namedType.Arity != 0)
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "the attribute target must be a non-generic class",
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

            if (ReservedApiNames.Contains(apiName))
            {
                return OpenApiClientDefinitionInput.CreateInvalid(
                    targetDisplayName,
                    "apiName '" + apiName + "' conflicts with a generated client member",
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

            if (!TryReadLibraryOptions(
                    context.Attributes[0],
                    out HttpTransportKind httpTransport,
                    out JsonSerializerKind jsonSerializer,
                    out string libraryOptionDetail))
            {
                return OpenApiClientDefinitionInput.CreateUnsupportedLibraryOption(
                    specId,
                    apiName,
                    generatedNamespace,
                    documentFormat,
                    targetDisplayName,
                    libraryOptionDetail,
                    diagnosticLocation);
            }

            return OpenApiClientDefinitionInput.CreateValid(
                specId,
                apiName,
                generatedNamespace,
                documentFormat,
                targetDisplayName,
                diagnosticLocation,
                httpTransport,
                jsonSerializer);
        }

        /// <summary>
        /// 名前付き引数 HttpTransport / JsonSerializer を読む。未指定は既定値（0）。
        /// Reads the named arguments HttpTransport / JsonSerializer. Unspecified means the default (0).
        /// </summary>
        private static bool TryReadLibraryOptions(
            AttributeData attribute,
            out HttpTransportKind httpTransport,
            out JsonSerializerKind jsonSerializer,
            out string detail)
        {
            httpTransport = HttpTransportKind.HttpClient;
            jsonSerializer = JsonSerializerKind.NewtonsoftJson;
            detail = string.Empty;
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, "HttpTransport", StringComparison.Ordinal))
                {
                    if (argument.Value.Kind == TypedConstantKind.Error ||
                        argument.Value.Value is not int transportValue ||
                        !LibrarySelection.IsDefined((HttpTransportKind)transportValue))
                    {
                        detail = "HttpTransport value '" + DescribeConstant(argument.Value) +
                                 "' is unknown; supported values are HttpClient (0) and UnityWebRequest (1)";
                        return false;
                    }

                    httpTransport = (HttpTransportKind)transportValue;
                }
                else if (string.Equals(argument.Key, "JsonSerializer", StringComparison.Ordinal))
                {
                    if (argument.Value.Kind == TypedConstantKind.Error ||
                        argument.Value.Value is not int serializerValue ||
                        !LibrarySelection.IsDefined((JsonSerializerKind)serializerValue))
                    {
                        detail = "JsonSerializer value '" + DescribeConstant(argument.Value) +
                                 "' is unknown; supported values are NewtonsoftJson (0) and SystemTextJson (1)";
                        return false;
                    }

                    jsonSerializer = (JsonSerializerKind)serializerValue;
                }
            }

            return true;
        }

        private static string DescribeConstant(TypedConstant constant)
        {
            return constant.Kind == TypedConstantKind.Error
                ? "<error>"
                : constant.Value?.ToString() ?? "null";
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
                return;
            }

            if (definition.Status ==
                OpenApiClientDefinitionInput.DefinitionStatus.UnsupportedLibraryOption)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedLibrarySelection,
                    definition.Location.ToLocation(),
                    definition.SpecId,
                    definition.ValidationMessage));
            }
        }

        private static void ReportLibrarySelectionDiagnostics(
            SourceProductionContext context,
            OpenApiClientDefinitionInput definition,
            LibraryAvailability availability)
        {
            foreach (LibrarySelectionIssue issue in LibrarySelection.Evaluate(
                         definition.Options.HttpTransport,
                         definition.Options.JsonSerializer,
                         availability))
            {
                context.ReportDiagnostic(CreateLibrarySelectionDiagnostic(definition, issue));
            }
        }

        private static Diagnostic CreateLibrarySelectionDiagnostic(
            OpenApiClientDefinitionInput definition,
            LibrarySelectionIssue issue)
        {
            Location location = definition.Location.ToLocation();
            string[] arguments = issue.Arguments;
            switch (issue.Kind)
            {
                case LibrarySelectionIssueKind.NotImplemented:
                    return Diagnostic.Create(
                        UnsupportedLibrarySelection, location, definition.SpecId, arguments[0]);
                case LibrarySelectionIssueKind.JsonLibraryMissing:
                    return Diagnostic.Create(
                        JsonLibraryNotReferenced,
                        location,
                        definition.SpecId,
                        arguments[0],
                        arguments[1],
                        arguments[2]);
                case LibrarySelectionIssueKind.HttpImplementationMissing:
                    return Diagnostic.Create(
                        HttpImplementationNotReferenced,
                        location,
                        definition.SpecId,
                        arguments[0],
                        arguments[1]);
                case LibrarySelectionIssueKind.VersionTooLow:
                    return Diagnostic.Create(
                        LibraryVersionTooLow,
                        location,
                        definition.SpecId,
                        arguments[0],
                        arguments[1],
                        arguments[2],
                        arguments[3]);
                case LibrarySelectionIssueKind.WebGlHttpClient:
                    return Diagnostic.Create(HttpClientOnWebGl, location, definition.SpecId);
                default:
                    throw new InvalidOperationException("Unknown library selection issue.");
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
            OpenApiGenerationWorkItem workItem,
            LibraryAvailability availability,
            Action<OpenApiSemanticParser, SpecNode>? injectWarnings)
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
                    // The selection diagnostics (OACG010-014) are reported by their own output.
                    // Here a blocking issue only suppresses AddSource.
                    if (LibrarySelection.HasBlockingIssue(LibrarySelection.Evaluate(
                            workItem.Definition.Options.HttpTransport,
                            workItem.Definition.Options.JsonSerializer,
                            availability)))
                    {
                        return;
                    }

                    Generate(context, workItem, injectWarnings);
                    return;
                default:
                    throw new InvalidOperationException("Unknown OpenAPI generator work item.");
            }
        }

        private static void Generate(
            SourceProductionContext context,
            OpenApiGenerationWorkItem workItem,
            Action<OpenApiSemanticParser, SpecNode>? injectWarnings)
        {
            NormalizedSpecBundle bundle = workItem.Bundle.Bundle!;
            try
            {
                var service = new GenerationService();
                OpenApiGenerationModel model = bundle.IsMultiDocument
                    ? service.BuildMvpModelFromOpenApiBundle(bundle, workItem.Definition.Options, injectWarnings)
                    : service.BuildMvpModelFromOpenApiNode(bundle.Root, workItem.Definition.Options, injectWarnings);
                foreach (GeneratedTypeNameCollision collision in model.TypeNameCollisions)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        TypeNameCollisionDiagnostic,
                        CreateExternalLocation(
                            GetCollisionSourcePath(collision.Location, bundle.SourcePath),
                            collision.Location.Line,
                            collision.Location.Column),
                        new[]
                        {
                            CreateExternalLocation(
                                GetCollisionSourcePath(collision.ExistingLocation, bundle.SourcePath),
                                collision.ExistingLocation.Line,
                                collision.ExistingLocation.Column)
                        },
                        collision.RequestedName,
                        collision.GeneratedName));
                }
                foreach (Diagnostic warning in CreateIgnoredElementDiagnostics(
                             model.Warnings,
                             bundle.SourcePath))
                {
                    context.ReportDiagnostic(warning);
                }

                IReadOnlyList<GeneratedFile> files = service.EmitMvpModel(
                    model,
                    workItem.Definition.Options.HttpTransport,
                    workItem.Definition.Options.JsonSerializer);
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

        private static string GetCollisionSourcePath(OpenApiSourceLocation location, string bundleSourcePath)
        {
            return string.IsNullOrEmpty(location.SourcePath)
                ? bundleSourcePath
                : location.SourcePath;
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
