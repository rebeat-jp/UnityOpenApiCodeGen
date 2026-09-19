#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rhycol.OpenApiCodeGen.SourceGenerator;
using Rhycol.OpenApiCodeGen.Editor.Generation;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    /// <summary>
    /// Fetches and parses the complete document graph for one generation request.
    /// Network and filesystem access intentionally stops at this Editor boundary; the analyzer receives
    /// only the canonical Bundle v2 produced by this class.
    /// </summary>
    internal sealed class ExternalSpecGraphLoader
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly string projectRoot;
        private readonly RawJsonNormalizer jsonNormalizer;
        private readonly RawYamlNormalizer yamlNormalizer;
        private readonly HttpClient httpClient;

        internal ExternalSpecGraphLoader(
            string projectRoot,
            RawJsonNormalizer jsonNormalizer,
            RawYamlNormalizer yamlNormalizer)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A project root is required.", nameof(projectRoot));
            }

            this.projectRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            this.jsonNormalizer = jsonNormalizer ?? throw new ArgumentNullException(nameof(jsonNormalizer));
            this.yamlNormalizer = yamlNormalizer ?? throw new ArgumentNullException(nameof(yamlNormalizer));
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json, application/yaml, text/yaml, */*;q=0.1");
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("UnityOpenApiCodeGen/0.5.0");
        }

        internal async Task<NormalizedSpecGraph> LoadAsync(
            string source,
            string specId,
            CancellationToken cancellationToken,
            IProgress<GenerationProgress>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("An OpenAPI document path or URL is required.", nameof(source));
            }

            cancellationToken.ThrowIfCancellationRequested();
            using (var graphCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                graphCancellation.CancelAfter(
                    TimeSpan.FromSeconds(NormalizedSpecBundleConstants.GraphTimeoutSeconds));
                var graph = new GraphBuilder(this, specId, graphCancellation.Token, progress);
                try
                {
                    return await graph.LoadAsync(source).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("The OpenAPI document graph timed out.");
                }
            }
        }

        private sealed class TrustedRemoteFetchException : SafeGenerationException
        {
            internal TrustedRemoteFetchException(string message)
                : base(message)
            {
            }
        }

        private sealed class GraphBuilder
        {
            private static readonly string[] HttpMethods =
                { "delete", "get", "head", "options", "patch", "post", "put", "trace" };
            private static readonly string[] ReferenceObjectComponentMaps =
                { "securitySchemes", "links" };
            private static readonly string[] SchemaMapProperties =
                { "properties", "patternProperties", "dependentSchemas", "$defs", "definitions" };
            private static readonly string[] SchemaProperties =
            {
                "additionalProperties", "unevaluatedItems", "unevaluatedProperties", "items",
                "additionalItems", "contains", "propertyNames", "not", "if", "then", "else",
                "contentSchema"
            };
            private static readonly string[] SchemaArrayProperties =
                { "prefixItems", "allOf", "anyOf", "oneOf" };
            private static readonly ReferenceCollector PathItemCollector = CollectPathItem;
            private static readonly ReferenceCollector SchemaCollector = CollectSchema;
            private static readonly ReferenceCollector ResponseCollector = CollectResponse;
            private static readonly ReferenceCollector ParameterCollector = CollectParameter;
            private static readonly ReferenceCollector ReferenceObjectCollector = CollectReferenceObject;
            private static readonly ReferenceCollector RequestBodyCollector = CollectRequestBody;
            private static readonly ReferenceCollector CallbackCollector = CollectCallback;

            private readonly ExternalSpecGraphLoader owner;
            private readonly string specId;
            private readonly CancellationToken cancellationToken;
            private readonly IProgress<GenerationProgress>? progress;
            private readonly Dictionary<string, GraphDocument> documentsByKey =
                new Dictionary<string, GraphDocument>(StringComparer.Ordinal);
            private readonly List<GraphDocument> documents = new List<GraphDocument>();
            private readonly Dictionary<GraphDocument, IReadOnlyList<ReferenceValue>> referencesByDocument =
                new Dictionary<GraphDocument, IReadOnlyList<ReferenceValue>>();
            private readonly List<ReferenceEdge> edges = new List<ReferenceEdge>();
            private long totalRawBytes;
            private int httpRequestCount;
            private int remoteFetchCount;
            private readonly Dictionary<string, string> remoteFetchKeysByPersistentIdentity =
                new Dictionary<string, string>(StringComparer.Ordinal);
            private bool rootWasUrl;
            private int rootStatusCode;
            private int rootRedirectCount;
            private string rootRequestedDisplay = string.Empty;
            private string rootEffectiveDisplay = string.Empty;
            private string rootFetchKey = string.Empty;
            private bool rootHadQuery;

            internal GraphBuilder(
                ExternalSpecGraphLoader owner,
                string specId,
                CancellationToken cancellationToken,
                IProgress<GenerationProgress>? progress)
            {
                this.owner = owner;
                this.specId = specId;
                this.cancellationToken = cancellationToken;
                this.progress = progress;
            }

            internal async Task<NormalizedSpecGraph> LoadAsync(string source)
            {
                ValidateSpecId(specId);
                SourceRequest root = CreateRootRequest(source);
                rootWasUrl = root.IsRemote;
                rootRequestedDisplay = root.DisplayUri;
                rootFetchKey = root.FetchKey;
                GraphDocument rootDocument = await LoadDocumentAsync(root, true, 0).ConfigureAwait(false);
                progress?.Report(new GenerationProgress(0.35, "Root document was fetched."));

                ValidateEdges();
                progress?.Report(new GenerationProgress(0.75, "References were validated."));
                for (int index = 0; index < documents.Count; index++)
                {
                    GraphDocument document = documents[index];
                    documents[index].Root = SanitizeReferences(
                        document.Root,
                        GetDocumentReferences(document));
                }

                documents.Remove(rootDocument);
                documents.Sort(CompareDocuments);
                documents.Insert(0, rootDocument);
                edges.Sort(CompareEdges);
                return new NormalizedSpecGraph(
                    specId,
                    rootDocument.RawSha256,
                    rootDocument.SourcePath,
                    rootDocument.Format,
                    documents,
                    edges,
                    rootRequestedDisplay,
                    rootEffectiveDisplay,
                    Sha256Hex(owner.GetDocumentIdentity(rootFetchKey, rootWasUrl)),
                    rootWasUrl ? "http" : "local",
                    rootStatusCode,
                    rootRedirectCount,
                    rootHadQuery);
            }

            private SourceRequest CreateRootRequest(string source)
            {
                Uri absolute;
                if (!Path.IsPathRooted(source) &&
                    Uri.TryCreate(source, UriKind.Absolute, out absolute))
                {
                    if (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
                    {
                        if (ContainsWhitespace(source))
                        {
                            throw new InvalidOperationException("OpenAPI URLs must not contain whitespace.");
                        }

                        if (!string.IsNullOrEmpty(absolute.Fragment))
                        {
                            throw new InvalidOperationException("A root URL must not contain a fragment.");
                        }

                        ValidateRemoteUri(absolute);
                        string remoteKey = RemoveFragment(absolute).AbsoluteUri;
                        rootHadQuery = !string.IsNullOrEmpty(absolute.Query);
                        return SourceRequest.Remote(
                            remoteKey,
                            RedactUri(absolute),
                            OpenApiDocumentFormat.Json,
                            true);
                    }

                    throw new NotSupportedException(
                        "Source Generator generation supports local paths and HTTP(S) URLs only.");
                }

                string path = owner.ResolveLocalPath(source, false);
                OpenApiDocumentFormat format = DetectLocalFormat(path);
                Uri fileUri = new Uri(path, UriKind.Absolute);
                string key = RemoveFragment(fileUri).AbsoluteUri;
                return SourceRequest.Local(path, key, format, true);
            }

            private async Task<GraphDocument> LoadDocumentAsync(
                SourceRequest request,
                bool isRoot,
                int depth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key = request.FetchKey;
                GraphDocument existing;
                if (documentsByKey.TryGetValue(key, out existing))
                {
                    return existing;
                }

                if (depth > NormalizedSpecBundleConstants.MaximumDepth)
                {
                    throw new SafeGenerationException(
                        "The external OpenAPI reference graph exceeds the maximum depth of " +
                        NormalizedSpecBundleConstants.MaximumDepth + ".");
                }

                if (documents.Count >= NormalizedSpecBundleConstants.MaximumDocumentCount)
                {
                    throw new SafeGenerationException(
                        "The external OpenAPI reference graph exceeds the maximum document count of " +
                        NormalizedSpecBundleConstants.MaximumDocumentCount + ".");
                }

                if (request.IsRemote && !string.IsNullOrEmpty(new Uri(request.FetchKey, UriKind.Absolute).Query))
                {
                    rootHadQuery = true;
                }
                DocumentPayload payload = request.IsRemote
                    ? await FetchRemoteWithLimitAsync(request).ConfigureAwait(false)
                    : ReadLocal(request);
                if (payload.Bytes.Length > NormalizedSpecBundleConstants.MaximumDocumentBytes)
                {
                    throw new InvalidOperationException(
                        "The OpenAPI document exceeds the maximum size of 4 MiB: " +
                        request.DisplayUri);
                }

                totalRawBytes += payload.Bytes.Length;
                if (totalRawBytes > NormalizedSpecBundleConstants.MaximumGraphBytes)
                {
                    throw new SafeGenerationException(
                        "The external OpenAPI reference graph exceeds the maximum size of 32 MiB.");
                }

                SpecNode root = Parse(payload.Bytes, payload.Format, request.DisplayUri);
                string effectiveKey = payload.EffectiveUri;
                string persistentIdentity = request.IsRemote
                    ? owner.GetDocumentIdentity(effectiveKey, true)
                    : string.Empty;
                GraphDocument effectiveDocument;
                if (!isRoot &&
                    documentsByKey.TryGetValue(effectiveKey, out effectiveDocument))
                {
                    string firstRequestedKey;
                    if (request.IsRemote &&
                        remoteFetchKeysByPersistentIdentity.TryGetValue(persistentIdentity, out firstRequestedKey) &&
                        !string.Equals(firstRequestedKey, request.FetchKey, StringComparison.Ordinal) &&
                        (HasQuery(firstRequestedKey) || HasQuery(request.FetchKey)))
                    {
                        throw new InvalidOperationException(
                            "Multiple remote OpenAPI documents resolve to the same persisted URL identity.");
                    }
                    if (request.IsRemote) remoteFetchKeysByPersistentIdentity[persistentIdentity] = request.FetchKey;
                    documentsByKey[key] = effectiveDocument;
                    return effectiveDocument;
                }

                string documentIdentity = owner.GetDocumentIdentity(
                    effectiveKey,
                    request.IsRemote);
                if (request.IsRemote)
                {
                    string existingFetchKey;
                    if (remoteFetchKeysByPersistentIdentity.TryGetValue(documentIdentity, out existingFetchKey) &&
                        !string.Equals(existingFetchKey, request.FetchKey, StringComparison.Ordinal) &&
                        (HasQuery(existingFetchKey) || HasQuery(request.FetchKey)))
                    {
                        throw new InvalidOperationException(
                            "Multiple remote OpenAPI documents resolve to the same persisted URL identity.");
                    }

                    remoteFetchKeysByPersistentIdentity[documentIdentity] = request.FetchKey;
                }
                string documentId = isRoot
                    ? NormalizedSpecBundleConstants.RootDocumentId
                    : request.IsRemote
                        ? "doc-" + Sha256Hex(documentIdentity)
                        : documentIdentity;
                string sourcePath = owner.GetDisplayPath(payload.EffectiveUri, request.IsRemote);
                string requestedSource = request.IsRemote
                    ? request.DisplayUri
                    : owner.GetDisplayPath(request.FetchKey, false);
                string effectiveSource = sourcePath;
                var document = new GraphDocument(
                    documentId,
                    sourcePath,
                    payload.Format == OpenApiDocumentFormat.Yaml ? "yaml" : "json",
                    payload.RawSha256,
                    root,
                    effectiveKey,
                    payload.EffectiveUri,
                    request.IsRemote,
                    requestedSource,
                    effectiveSource,
                    Sha256Hex(documentIdentity),
                    request.IsRemote ? "http" : "local",
                    payload.StatusCode,
                    payload.RedirectCount);
                documentsByKey.Add(key, document);
                if (!documentsByKey.ContainsKey(effectiveKey))
                {
                    documentsByKey.Add(effectiveKey, document);
                }
                documents.Add(document);
                referencesByDocument.Add(document, EnumerateDocumentReferences(document));
                progress?.Report(new GenerationProgress(
                    Math.Min(0.7, 0.35 + documents.Count * 0.35 / NormalizedSpecBundleConstants.MaximumDocumentCount),
                    "Loaded " + documents.Count.ToString(CultureInfo.InvariantCulture) + " document(s)."));

                if (isRoot)
                {
                    rootEffectiveDisplay = sourcePath;
                    rootStatusCode = payload.StatusCode;
                    rootRedirectCount = payload.RedirectCount;
                }

                // Resolve recursively before returning so every edge is validated and the result is
                // publishable as one complete generation unit.
                await ResolveDocumentReferencesAsync(document, depth).ConfigureAwait(false);
                return document;
            }

            private Task<DocumentPayload> FetchRemoteWithLimitAsync(SourceRequest request)
            {
                if (++remoteFetchCount > NormalizedSpecBundleConstants.MaximumDocumentCount)
                {
                    throw new SafeGenerationException("The external OpenAPI reference graph exceeds the maximum remote fetch count of 64.");
                }
                return FetchRemoteAsync(request);
            }

            private async Task ResolveDocumentReferencesAsync(GraphDocument document, int depth)
            {
                foreach (ReferenceValue reference in GetDocumentReferences(document))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SourceRequest target = ResolveReference(document, reference.Value, out string targetPointer);
                    GraphDocument targetDocument = await LoadDocumentAsync(
                        target,
                        false,
                        depth + 1).ConfigureAwait(false);
                    edges.Add(new ReferenceEdge(
                        document.DocumentId,
                        reference.Pointer,
                        targetDocument.DocumentId,
                        targetPointer));
                }
            }

            private SourceRequest ResolveReference(
                GraphDocument sourceDocument,
                string reference,
                out string targetPointer)
            {
                Uri explicitUri;
                if (Uri.TryCreate(reference, UriKind.Absolute, out explicitUri) &&
                    string.Equals(explicitUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        "Explicit file URI references are not supported: " + sourceDocument.SourcePath);
                }

                Uri resolved;
                if (sourceDocument.IsRemote)
                {
                    Uri baseUri = new Uri(sourceDocument.FetchKey, UriKind.Absolute);
                    if (!Uri.TryCreate(baseUri, reference, out resolved))
                    {
                        throw new InvalidOperationException(
                            "The external reference is not a valid URI: " + sourceDocument.SourcePath);
                    }
                }
                else
                {
                    Uri baseUri = new Uri(sourceDocument.FetchKey, UriKind.Absolute);
                    if (!Uri.TryCreate(baseUri, reference, out resolved))
                    {
                        throw new InvalidOperationException(
                            "The external reference is not a valid URI: " + sourceDocument.SourcePath);
                    }
                }

                targetPointer = DecodePointer(resolved.Fragment, sourceDocument.SourcePath);
                Uri fetchUri = RemoveFragment(resolved);
                if (fetchUri.Scheme == Uri.UriSchemeHttp || fetchUri.Scheme == Uri.UriSchemeHttps)
                {
                    if (ContainsWhitespace(reference))
                    {
                        throw new InvalidOperationException(
                            "The external reference contains whitespace: " + sourceDocument.SourcePath);
                    }

                    ValidateRemoteUri(fetchUri);
                    Uri sourceUri = new Uri(sourceDocument.FetchKey, UriKind.Absolute);
                    if (sourceDocument.IsRemote && !SameOrigin(sourceUri, fetchUri))
                    {
                        throw new SafeGenerationException(
                            "Remote OpenAPI documents may reference only the same origin: " +
                            sourceDocument.SourcePath);
                    }
                    if (sourceDocument.IsRemote &&
                        string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(fetchUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "HTTPS documents must not reference HTTP documents: " + sourceDocument.SourcePath);
                    }

                    string key = fetchUri.AbsoluteUri;
                    return SourceRequest.Remote(
                        key,
                        RedactUri(fetchUri),
                        DetectFormatFromPath(fetchUri.AbsolutePath),
                        false);
                }

                if (!fetchUri.IsFile)
                {
                    throw new NotSupportedException(
                        "Only local files and HTTP(S) external references are supported: " +
                        sourceDocument.SourcePath);
                }

                bool sameDocument = string.Equals(
                    fetchUri.AbsoluteUri,
                    sourceDocument.FetchKey,
                    StringComparison.Ordinal);
                if (sourceDocument.IsRemote)
                {
                    throw new NotSupportedException(
                        "A remote document must not reference a local file: " + sourceDocument.SourcePath);
                }
                string localPath = owner.ResolveLocalPath(fetchUri.LocalPath, !sameDocument);

                OpenApiDocumentFormat format = DetectLocalFormat(localPath);
                return SourceRequest.Local(
                    localPath,
                    new Uri(localPath, UriKind.Absolute).AbsoluteUri,
                    sameDocument
                        ? (string.Equals(sourceDocument.Format, "yaml", StringComparison.Ordinal)
                            ? OpenApiDocumentFormat.Yaml
                            : OpenApiDocumentFormat.Json)
                        : format,
                    false);
            }

            private DocumentPayload ReadLocal(SourceRequest request)
            {
                byte[] bytes;
                try
                {
                    var fileInfo = new FileInfo(request.LocalPath);
                    if (fileInfo.Length > NormalizedSpecBundleConstants.MaximumDocumentBytes)
                    {
                        throw new InvalidOperationException(
                            "The OpenAPI document exceeds the maximum size of 4 MiB: " +
                            request.DisplayUri);
                    }

                    bytes = File.ReadAllBytes(request.LocalPath);
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException ||
                    exception is ArgumentException)
                {
                    throw new InvalidOperationException(
                        "The local OpenAPI document could not be read: " + request.DisplayUri,
                        exception);
                }

                return new DocumentPayload(
                    bytes,
                    request.Format,
                    Sha256Hex(bytes),
                    request.FetchKey,
                    0,
                    0);
            }

            private async Task<DocumentPayload> FetchRemoteAsync(SourceRequest request)
            {
                try
                {
                    return await FetchRemoteCoreAsync(request).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TrustedRemoteFetchException)
                {
                    // Only messages created by this loader are allowed to cross the remote
                    // boundary. HttpClient/content/stream exceptions are redacted below.
                    throw;
                }
                catch (Exception)
                {
                    // HttpClient and stream exceptions can include the complete request URI in
                    // their message or inner exception. Remote failures must never retain that
                    // data, because query values are treated as user-provided secrets.
                    throw new SafeGenerationException(
                        "The remote OpenAPI document could not be fetched.");
                }
            }

            private async Task<DocumentPayload> FetchRemoteCoreAsync(SourceRequest request)
            {
                {
                    Uri current = new Uri(request.FetchKey, UriKind.Absolute);
                    int redirects = 0;
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, current))
                        using (CancellationTokenSource requestCancellation = CreateRequestCancellation())
                        {
                            HttpResponseMessage response;
                            try
                            {
                                if (++httpRequestCount > NormalizedSpecBundleConstants.MaximumHttpRequestCount)
                                {
                                    throw new TrustedRemoteFetchException(
                                        "The external OpenAPI reference graph exceeds the maximum HTTP request count of " +
                                        NormalizedSpecBundleConstants.MaximumHttpRequestCount + ".");
                                }

                                response = await owner.httpClient.SendAsync(
                                    requestMessage,
                                    HttpCompletionOption.ResponseHeadersRead,
                                    requestCancellation.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception exception) when (
                                exception is HttpRequestException ||
                                exception is IOException)
                            {
                                throw new TrustedRemoteFetchException(
                                    "The OpenAPI URL request failed: " + RedactUri(current));
                            }

                            using (response)
                            {
                            if (IsRedirect(response.StatusCode))
                            {
                                if (redirects >= NormalizedSpecBundleConstants.MaximumRedirects)
                                {
                                    throw new TrustedRemoteFetchException(
                                        "The OpenAPI URL exceeded the maximum redirect count: " + RedactUri(current));
                                }

                                Uri? location = response.Headers.Location;
                                if (location == null)
                                {
                                    throw new TrustedRemoteFetchException(
                                        "The OpenAPI URL returned a redirect without a Location header: " +
                                        RedactUri(current));
                                }

                                if (!location.IsAbsoluteUri)
                                {
                                    location = new Uri(current, location);
                                }

                                ValidateRemoteUri(location);
                                if (!request.IsRoot && !SameOrigin(current, location))
                                {
                                    throw new TrustedRemoteFetchException(
                                        "Remote OpenAPI redirects must remain on the same origin.");
                                }
                                if (string.Equals(current.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(location.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                                {
                                    throw new TrustedRemoteFetchException(
                                        "HTTPS to HTTP redirects are not allowed for OpenAPI sources.");
                                }

                                current = RemoveFragment(location);
                                redirects++;
                                continue;
                            }

                            if (!response.IsSuccessStatusCode)
                            {
                                throw new TrustedRemoteFetchException(
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "The OpenAPI URL returned HTTP {0}: {1}",
                                        (int)response.StatusCode,
                                        RedactUri(current)));
                            }

                            byte[] bytes;
                            try
                            {
                                bytes = await ReadResponseBytesAsync(response, requestCancellation.Token)
                                    .ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception exception) when (
                                exception is IOException ||
                                exception is HttpRequestException)
                            {
                                throw new TrustedRemoteFetchException(
                                    "The OpenAPI URL response could not be read: " + RedactUri(current));
                            }
                            string contentType = GetMediaType(response.Content.Headers.ContentType);
                            OpenApiDocumentFormat format = ResolveRemoteFormat(
                                current,
                                contentType);
                            return new DocumentPayload(
                                bytes,
                                format,
                                Sha256Hex(bytes),
                                current.AbsoluteUri,
                                (int)response.StatusCode,
                                redirects);
                            }
                        }
                    }
                }
            }

            private CancellationTokenSource CreateRequestCancellation()
            {
                var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestCancellation.CancelAfter(
                    TimeSpan.FromSeconds(NormalizedSpecBundleConstants.RequestTimeoutSeconds));
                return requestCancellation;
            }

            private static bool SameOrigin(Uri left, Uri right)
            {
                return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
                       left.Port == right.Port;
            }

            private static bool HasQuery(string uri)
            {
                Uri parsed;
                return Uri.TryCreate(uri, UriKind.Absolute, out parsed) && !string.IsNullOrEmpty(parsed.Query);
            }

            private IReadOnlyList<ReferenceValue> EnumerateDocumentReferences(GraphDocument document)
            {
                var references = new List<ReferenceValue>();
                CollectReferences(document.Root, references);
                for (int index = 0; index < references.Count; index++)
                {
                    ReferenceValue reference = references[index];
                    if (reference.Value.Length == 0)
                    {
                        throw new InvalidOperationException(
                            "An external reference must not be empty: " + document.SourcePath);
                    }
                }

                return references;
            }

            private IReadOnlyList<ReferenceValue> GetDocumentReferences(GraphDocument document)
            {
                IReadOnlyList<ReferenceValue> references;
                if (!referencesByDocument.TryGetValue(document, out references))
                {
                    throw new InvalidOperationException(
                        "The external reference graph is missing its parsed reference index.");
                }

                return references;
            }

            private static SpecNode SanitizeReferences(
                SpecNode node,
                IReadOnlyList<ReferenceValue> references)
            {
                var replacements = new Dictionary<SpecStringNode, string>();
                for (int index = 0; index < references.Count; index++)
                {
                    SpecStringNode reference = references[index].Node;
                    string sanitized = RedactReference(reference.Value);
                    if (!string.Equals(sanitized, reference.Value, StringComparison.Ordinal))
                    {
                        replacements.Add(reference, sanitized);
                    }
                }

                return replacements.Count == 0
                    ? node
                    : SanitizeReferences(node, replacements);
            }

            private static SpecNode SanitizeReferences(
                SpecNode node,
                Dictionary<SpecStringNode, string> replacements)
            {
                SpecStringNode? stringNode = node as SpecStringNode;
                if (stringNode != null && replacements.TryGetValue(stringNode, out string? sanitized))
                {
                    return new SpecStringNode(stringNode.Line, stringNode.Column, sanitized);
                }

                SpecObjectNode? objectNode = node as SpecObjectNode;
                if (objectNode != null)
                {
                    List<SpecProperty>? properties = null;
                    for (int index = 0; index < objectNode.Properties.Count; index++)
                    {
                        SpecProperty property = objectNode.Properties[index];
                        SpecNode value = SanitizeReferences(property.Value, replacements);
                        if (ReferenceEquals(value, property.Value))
                        {
                            if (properties != null)
                            {
                                properties.Add(property);
                            }

                            continue;
                        }

                        if (properties == null)
                        {
                            properties = new List<SpecProperty>(objectNode.Properties.Count);
                            for (int previous = 0; previous < index; previous++)
                            {
                                properties.Add(objectNode.Properties[previous]);
                            }
                        }

                        properties.Add(new SpecProperty(
                            property.Name,
                            property.Line,
                            property.Column,
                            value));
                    }

                    return properties == null
                        ? node
                        : new SpecObjectNode(objectNode.Line, objectNode.Column, properties);
                }

                SpecArrayNode? arrayNode = node as SpecArrayNode;
                if (arrayNode != null)
                {
                    List<SpecNode>? items = null;
                    for (int index = 0; index < arrayNode.Items.Count; index++)
                    {
                        SpecNode item = SanitizeReferences(arrayNode.Items[index], replacements);
                        if (ReferenceEquals(item, arrayNode.Items[index]))
                        {
                            if (items != null)
                            {
                                items.Add(item);
                            }

                            continue;
                        }

                        if (items == null)
                        {
                            items = new List<SpecNode>(arrayNode.Items.Count);
                            for (int previous = 0; previous < index; previous++)
                            {
                                items.Add(arrayNode.Items[previous]);
                            }
                        }

                        items.Add(item);
                    }

                    return items == null
                        ? node
                        : new SpecArrayNode(arrayNode.Line, arrayNode.Column, items);
                }

                return node;
            }

            private static string RedactReference(string reference)
            {
                Uri absolute;
                if (Uri.TryCreate(reference, UriKind.Absolute, out absolute) &&
                    (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
                {
                    var builder = new UriBuilder(absolute) { Query = string.Empty };
                    return builder.Uri.AbsoluteUri;
                }

                int fragmentIndex = reference.IndexOf('#');
                string beforeFragment = fragmentIndex >= 0
                    ? reference.Substring(0, fragmentIndex)
                    : reference;
                int queryIndex = beforeFragment.IndexOf('?');
                if (queryIndex >= 0)
                {
                    beforeFragment = beforeFragment.Substring(0, queryIndex);
                }

                return fragmentIndex >= 0
                    ? beforeFragment + reference.Substring(fragmentIndex)
                    : beforeFragment;
            }

            private static async Task<byte[]> ReadResponseBytesAsync(
                HttpResponseMessage response,
                CancellationToken cancellationToken)
            {
                HttpContent content = response.Content;
                if (content == null)
                {
                    throw new TrustedRemoteFetchException("The OpenAPI URL returned no response body.");
                }

                long? contentLength = content.Headers.ContentLength;
                if (contentLength.HasValue &&
                    contentLength.Value > NormalizedSpecBundleConstants.MaximumDocumentBytes)
                {
                    throw new TrustedRemoteFetchException(
                        "The OpenAPI document exceeds the maximum size of 4 MiB.");
                }

                using (Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        if (output.Length + read > NormalizedSpecBundleConstants.MaximumDocumentBytes)
                        {
                            throw new TrustedRemoteFetchException(
                                "The OpenAPI document exceeds the maximum size of 4 MiB.");
                        }

                        output.Write(buffer, 0, read);
                    }

                    return output.ToArray();
                }
            }

            private SpecNode Parse(
                byte[] bytes,
                OpenApiDocumentFormat format,
                string sourcePath)
            {
                string source;
                try
                {
                    source = StrictUtf8.GetString(bytes);
                    if (source.Length > 0 && source[0] == '\uFEFF')
                    {
                        source = source.Substring(1);
                    }
                }
                catch (DecoderFallbackException exception)
                {
                    throw new NormalizedSpecException(
                        "The raw document is not valid UTF-8.",
                        sourcePath,
                        1,
                        1,
                        string.Empty,
                        exception);
                }

                if (format == OpenApiDocumentFormat.Yaml)
                {
                    try
                    {
                        return YamlParser.Parse(source, sourcePath);
                    }
                    catch (YamlParseException exception)
                    {
                        throw new NormalizedSpecException(
                            exception.DiagnosticMessage,
                            sourcePath,
                            exception.Line,
                            exception.Column,
                            string.Empty,
                            exception.DiagnosticId,
                            exception);
                    }
                }

                return StrictJsonSpecParser.Parse(source, sourcePath);
            }

            private void ValidateEdges()
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < edges.Count; index++)
                {
                    ReferenceEdge edge = edges[index];
                    string key = edge.SourceDocumentId + "\n" + edge.SourcePointer;
                    if (!seen.Add(key))
                    {
                        throw new InvalidOperationException("The external reference graph contains a duplicate edge.");
                    }

                    GraphDocument target = FindDocument(edge.TargetDocumentId);
                    if (!TryResolvePointer(target.Root, edge.TargetPointer, out _))
                    {
                        throw new NormalizedSpecException(
                            "The external reference target could not be resolved.",
                            target.SourcePath,
                            target.Root.Line,
                            target.Root.Column,
                            edge.TargetPointer,
                            "OACG102");
                    }
                }
            }

            private GraphDocument FindDocument(string documentId)
            {
                for (int index = 0; index < documents.Count; index++)
                {
                    if (string.Equals(documents[index].DocumentId, documentId, StringComparison.Ordinal))
                    {
                        return documents[index];
                    }
                }

                throw new InvalidOperationException("The external reference target document is missing.");
            }

            private delegate void ReferenceCollector(
                SpecNode node,
                ReferencePath path,
                List<ReferenceValue> references);

            private static void CollectReferences(SpecNode node, List<ReferenceValue> references)
            {
                SpecObjectNode? root = node as SpecObjectNode;
                if (root == null) return;
                var path = new ReferencePath();
                if (TryGetProperty(root, "openapi", out _))
                {
                    CollectOpenApiDocument(root, path, references);
                }
                else if (TryGetProperty(root, "swagger", out _))
                {
                    CollectSwaggerDocument(root, path, references);
                }
                else
                {
                    // External documents may intentionally be a bare JSON Schema.
                    CollectSchema(node, path, references);
                }
            }

            private static void CollectOpenApiDocument(
                SpecObjectNode root,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                CollectPathMapProperty(root, path, "paths", PathItemCollector, references);
                CollectPathMapProperty(root, path, "webhooks", PathItemCollector, references);
                if (!TryGetObjectProperty(root, "components", out SpecObjectNode? components)) return;
                path.Push("components");
                CollectObjectMapProperty(components, path, "schemas", SchemaCollector, references);
                CollectObjectMapProperty(components, path, "responses", ResponseCollector, references);
                CollectObjectMapProperty(components, path, "parameters", ParameterCollector, references);
                CollectObjectMapProperty(components, path, "examples", ReferenceObjectCollector, references);
                CollectObjectMapProperty(components, path, "requestBodies", RequestBodyCollector, references);
                CollectObjectMapProperty(components, path, "headers", ParameterCollector, references);
                for (int index = 0; index < ReferenceObjectComponentMaps.Length; index++)
                {
                    CollectObjectMapProperty(
                        components,
                        path,
                        ReferenceObjectComponentMaps[index],
                        ReferenceObjectCollector,
                        references);
                }

                CollectObjectMapProperty(components, path, "callbacks", CallbackCollector, references);
                CollectObjectMapProperty(components, path, "pathItems", PathItemCollector, references);
                path.Pop();
            }

            private static void CollectSwaggerDocument(
                SpecObjectNode root,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                CollectPathMapProperty(root, path, "paths", PathItemCollector, references);
                CollectObjectMapProperty(root, path, "definitions", SchemaCollector, references);
                CollectObjectMapProperty(root, path, "parameters", ParameterCollector, references);
                CollectObjectMapProperty(root, path, "responses", ResponseCollector, references);
            }

            private static void CollectPathItem(
                SpecNode node,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                CollectDirectReference(node, path, references);
                SpecObjectNode? pathItem = node as SpecObjectNode;
                if (pathItem == null) return;
                CollectArrayProperty(pathItem, path, "parameters", ParameterCollector, references);
                for (int index = 0; index < HttpMethods.Length; index++)
                {
                    string method = HttpMethods[index];
                    if (!TryGetProperty(pathItem, method, out SpecNode? operation)) continue;
                    path.Push(method);
                    CollectOperation(operation!, path, references);
                    path.Pop();
                }
            }

            private static void CollectOperation(
                SpecNode node,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                SpecObjectNode? operation = node as SpecObjectNode;
                if (operation == null) return;
                CollectArrayProperty(operation, path, "parameters", ParameterCollector, references);
                CollectProperty(operation, path, "requestBody", RequestBodyCollector, references);
                CollectObjectMapProperty(
                    operation,
                    path,
                    "responses",
                    ResponseCollector,
                    references,
                    true);
                CollectObjectMapProperty(operation, path, "callbacks", CallbackCollector, references);
            }

            private static void CollectParameter(
                SpecNode node,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                CollectDirectReference(node, path, references);
                SpecObjectNode? parameter = node as SpecObjectNode;
                if (parameter == null) return;
                CollectProperty(parameter, path, "schema", SchemaCollector, references);
                CollectContentProperty(parameter, path, references);
                CollectObjectMapProperty(parameter, path, "examples", ReferenceObjectCollector, references);
                CollectProperty(parameter, path, "items", SchemaCollector, references);
            }

            private static void CollectRequestBody(
                SpecNode node,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                CollectDirectReference(node, path, references);
                if (node is SpecObjectNode requestBody)
                {
                    CollectContentProperty(requestBody, path, references);
                }
            }

            private static void CollectResponse(
                SpecNode node,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                CollectDirectReference(node, path, references);
                SpecObjectNode? response = node as SpecObjectNode;
                if (response == null) return;
                CollectContentProperty(response, path, references);
                CollectProperty(response, path, "schema", SchemaCollector, references);
                CollectObjectMapProperty(response, path, "headers", ParameterCollector, references);
                CollectObjectMapProperty(response, path, "links", ReferenceObjectCollector, references);
            }

            private static void CollectCallback(
                SpecNode node,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                int originalCount = references.Count;
                CollectDirectReference(node, path, references);
                if (references.Count != originalCount) return;
                SpecObjectNode? callback = node as SpecObjectNode;
                if (callback == null) return;
                foreach (SpecProperty expression in callback.Properties)
                {
                    if (expression.Name.StartsWith("x-", StringComparison.Ordinal)) continue;
                    path.Push(expression.Name);
                    CollectPathItem(expression.Value, path, references);
                    path.Pop();
                }
            }

            private static void CollectContentProperty(
                SpecObjectNode owner,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                if (!TryGetObjectProperty(owner, "content", out SpecObjectNode? content)) return;
                path.Push("content");
                foreach (SpecProperty mediaType in content.Properties)
                {
                    SpecObjectNode? media = mediaType.Value as SpecObjectNode;
                    if (media == null) continue;
                    path.Push(mediaType.Name);
                    CollectProperty(media, path, "schema", SchemaCollector, references);
                    CollectObjectMapProperty(media, path, "examples", ReferenceObjectCollector, references);
                    if (TryGetObjectProperty(media, "encoding", out SpecObjectNode? encodings))
                    {
                        path.Push("encoding");
                        foreach (SpecProperty encoding in encodings.Properties)
                        {
                            if (encoding.Value is SpecObjectNode encodingObject)
                            {
                                path.Push(encoding.Name);
                                CollectObjectMapProperty(
                                    encodingObject,
                                    path,
                                    "headers",
                                    ParameterCollector,
                                    references);
                                path.Pop();
                            }
                        }

                        path.Pop();
                    }

                    path.Pop();
                }

                path.Pop();
            }

            private static void CollectSchema(
                SpecNode node,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                CollectDirectReference(node, path, references);
                SpecObjectNode? schema = node as SpecObjectNode;
                if (schema == null) return;
                for (int index = 0; index < SchemaMapProperties.Length; index++)
                {
                    CollectObjectMapProperty(
                        schema,
                        path,
                        SchemaMapProperties[index],
                        SchemaCollector,
                        references);
                }

                for (int index = 0; index < SchemaProperties.Length; index++)
                {
                    CollectProperty(schema, path, SchemaProperties[index], SchemaCollector, references);
                }

                for (int index = 0; index < SchemaArrayProperties.Length; index++)
                {
                    CollectArrayProperty(schema, path, SchemaArrayProperties[index], SchemaCollector, references);
                }
            }

            private static void CollectReferenceObject(
                SpecNode node,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                CollectDirectReference(node, path, references);
            }

            private static void CollectDirectReference(
                SpecNode node,
                ReferencePath path,
                List<ReferenceValue> references)
            {
                SpecObjectNode? objectNode = node as SpecObjectNode;
                if (objectNode == null || !TryGetProperty(objectNode, "$ref", out SpecNode? value)) return;
                path.Push("$ref");
                string referencePointer = path.ToPointer();
                path.Pop();
                SpecStringNode? referenceNode = value as SpecStringNode;
                if (referenceNode == null)
                {
                    throw new InvalidOperationException(
                        "An OpenAPI '$ref' value must be a string at " + referencePointer + ".");
                }

                references.Add(new ReferenceValue(referencePointer, referenceNode));
            }

            private static void CollectObjectMapProperty(
                SpecObjectNode owner,
                ReferencePath path,
                string propertyName,
                ReferenceCollector collectValue,
                List<ReferenceValue> references,
                bool skipExtensions = false)
            {
                if (!TryGetObjectProperty(owner, propertyName, out SpecObjectNode? map)) return;
                path.Push(propertyName);
                foreach (SpecProperty property in map.Properties)
                {
                    if (skipExtensions && property.Name.StartsWith("x-", StringComparison.Ordinal)) continue;
                    path.Push(property.Name);
                    collectValue(property.Value, path, references);
                    path.Pop();
                }

                path.Pop();
            }

            private static void CollectPathMapProperty(
                SpecObjectNode owner,
                ReferencePath path,
                string propertyName,
                ReferenceCollector collectValue,
                List<ReferenceValue> references)
            {
                if (!TryGetObjectProperty(owner, propertyName, out SpecObjectNode? map)) return;
                path.Push(propertyName);
                foreach (SpecProperty property in map.Properties)
                {
                    if (property.Name.StartsWith("x-", StringComparison.Ordinal)) continue;
                    path.Push(property.Name);
                    collectValue(property.Value, path, references);
                    path.Pop();
                }

                path.Pop();
            }

            private static void CollectArrayProperty(
                SpecObjectNode owner,
                ReferencePath path,
                string propertyName,
                ReferenceCollector collectItem,
                List<ReferenceValue> references)
            {
                if (!TryGetProperty(owner, propertyName, out SpecNode? value) ||
                    !(value is SpecArrayNode array)) return;
                path.Push(propertyName);
                for (int index = 0; index < array.Items.Count; index++)
                {
                    path.Push(index.ToString(CultureInfo.InvariantCulture));
                    collectItem(array.Items[index], path, references);
                    path.Pop();
                }

                path.Pop();
            }

            private static void CollectProperty(
                SpecObjectNode owner,
                ReferencePath path,
                string propertyName,
                ReferenceCollector collectValue,
                List<ReferenceValue> references)
            {
                if (!TryGetProperty(owner, propertyName, out SpecNode? value)) return;
                path.Push(propertyName);
                collectValue(value!, path, references);
                path.Pop();
            }

            private sealed class ReferencePath
            {
                private readonly List<string> segments = new List<string>();

                internal void Push(string segment)
                {
                    segments.Add(segment);
                }

                internal void Pop()
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                internal string ToPointer()
                {
                    var builder = new StringBuilder();
                    for (int index = 0; index < segments.Count; index++)
                    {
                        builder.Append('/');
                        builder.Append(segments[index].Replace("~", "~0").Replace("/", "~1"));
                    }

                    return builder.ToString();
                }
            }

            private static bool TryGetObjectProperty(
                SpecObjectNode owner,
                string propertyName,
                out SpecObjectNode? value)
            {
                if (TryGetProperty(owner, propertyName, out SpecNode? node) && node is SpecObjectNode objectNode)
                {
                    value = objectNode;
                    return true;
                }

                value = null;
                return false;
            }

            private static bool TryGetProperty(
                SpecObjectNode owner,
                string propertyName,
                out SpecNode? value)
            {
                foreach (SpecProperty property in owner.Properties)
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
                    {
                        value = property.Value;
                        return true;
                    }
                }

                value = null;
                return false;
            }

            private static string DecodePointer(string fragment, string sourcePath)
            {
                if (string.IsNullOrEmpty(fragment))
                {
                    return string.Empty;
                }

                string encoded = fragment[0] == '#' ? fragment.Substring(1) : fragment;
                string pointer;
                try
                {
                    pointer = Uri.UnescapeDataString(encoded);
                }
                catch (UriFormatException)
                {
                    throw new InvalidOperationException(
                        "The external reference fragment is not valid: " + sourcePath);
                }

                if (pointer.Length == 0)
                {
                    return string.Empty;
                }

                if (pointer[0] != '/')
                {
                    throw new NotSupportedException(
                        "Only JSON Pointer external reference fragments are supported: " + sourcePath);
                }

                ValidateJsonPointer(pointer, sourcePath);

                return pointer;
            }

            private static void ValidateJsonPointer(string pointer, string sourcePath)
            {
                for (int index = 0; index < pointer.Length; index++)
                {
                    if (pointer[index] != '~')
                    {
                        continue;
                    }

                    if (index + 1 >= pointer.Length ||
                        (pointer[index + 1] != '0' && pointer[index + 1] != '1'))
                    {
                        throw new InvalidOperationException(
                            "The external reference fragment contains an invalid JSON Pointer escape: " +
                            sourcePath);
                    }

                    index++;
                }
            }

            private static bool TryResolvePointer(SpecNode root, string pointer, out SpecNode resolved)
            {
                resolved = root;
                if (string.IsNullOrEmpty(pointer))
                {
                    return true;
                }

                string[] segments = pointer.Substring(1).Split('/');
                for (int index = 0; index < segments.Length; index++)
                {
                    string segment = segments[index].Replace("~1", "/").Replace("~0", "~");
                    SpecObjectNode? objectNode = resolved as SpecObjectNode;
                    if (objectNode != null)
                    {
                        SpecNode? next = null;
                        foreach (SpecProperty property in objectNode.Properties)
                        {
                            if (string.Equals(property.Name, segment, StringComparison.Ordinal))
                            {
                                next = property.Value;
                                break;
                            }
                        }

                        if (next == null)
                        {
                            resolved = root;
                            return false;
                        }

                        resolved = next!;
                        continue;
                    }

                    SpecArrayNode? arrayNode = resolved as SpecArrayNode;
                    int arrayIndex;
                    if (arrayNode == null ||
                        !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out arrayIndex) ||
                        arrayIndex < 0 ||
                        arrayIndex >= arrayNode.Items.Count)
                    {
                        resolved = root;
                        return false;
                    }

                    resolved = arrayNode.Items[arrayIndex];
                }

                return true;
            }

            private static int CompareDocuments(GraphDocument left, GraphDocument right)
            {
                return StringComparer.Ordinal.Compare(left.DocumentId, right.DocumentId);
            }

            private static int CompareEdges(ReferenceEdge left, ReferenceEdge right)
            {
                int result = StringComparer.Ordinal.Compare(left.SourceDocumentId, right.SourceDocumentId);
                if (result != 0)
                {
                    return result;
                }

                result = StringComparer.Ordinal.Compare(left.SourcePointer, right.SourcePointer);
                if (result != 0)
                {
                    return result;
                }

                result = StringComparer.Ordinal.Compare(left.TargetDocumentId, right.TargetDocumentId);
                if (result != 0)
                {
                    return result;
                }

                return StringComparer.Ordinal.Compare(left.TargetPointer, right.TargetPointer);
            }
        }

        private string ResolveLocalPath(string path, bool isExternal)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("A local OpenAPI document path is empty.");
            }

            string fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(projectRoot, path));
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException("The local OpenAPI document does not exist: " +
                                                    (IsSameOrDescendant(projectRoot, fullPath) ? ToDisplayPath(fullPath) : "<external-file>"));
            }

            if (isExternal)
            {
                EnsureNoSymbolicLink(projectRoot, fullPath);
                if (!IsSameOrDescendant(projectRoot, fullPath))
                {
                    throw new SafeGenerationException(
                        "Local external references must remain inside the Unity project: " +
                        "<external-file>");
                }
            }

            return fullPath;
        }

        private string GetDisplayPath(string effectiveUri, bool isRemote)
        {
            if (isRemote)
            {
                return RedactUri(new Uri(effectiveUri, UriKind.Absolute));
            }

            string fullPath = new Uri(effectiveUri, UriKind.Absolute).LocalPath;
            string rootPrefix = projectRoot + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(rootPrefix, PathComparison))
            {
                return fullPath.Substring(rootPrefix.Length).Replace('\\', '/');
            }

            return fullPath.Replace('\\', '/');
        }

        private string GetDocumentIdentity(string effectiveUri, bool isRemote)
        {
            if (isRemote)
            {
                return RedactUri(new Uri(effectiveUri, UriKind.Absolute));
            }

            // Local external documents are project-bound. Hashing this normalized project-relative
            // identity keeps document IDs stable when the Unity checkout moves on disk.
            return GetDisplayPath(effectiveUri, false).Replace('\\', '/');
        }

        private string ToDisplayPath(string fullPath)
        {
            return GetDisplayPath(new Uri(fullPath, UriKind.Absolute).AbsoluteUri, false);
        }

        private static void ValidateRemoteUri(Uri uri)
        {
            if (uri == null ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new NotSupportedException("Only HTTP(S) URLs are supported for remote OpenAPI documents.");
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new NotSupportedException("OpenAPI URLs containing userinfo are not supported.");
            }

            if (ContainsWhitespace(uri.AbsoluteUri))
            {
                throw new InvalidOperationException("OpenAPI URLs must not contain whitespace.");
            }
        }

        private static OpenApiDocumentFormat DetectLocalFormat(string path)
        {
            string extension = Path.GetExtension(path);
            if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                return OpenApiDocumentFormat.Json;
            }

            if (string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
            {
                return OpenApiDocumentFormat.Yaml;
            }

            throw new NotSupportedException(
                "OpenAPI documents must use a .json, .yaml, or .yml extension: " +
                path.Replace('\\', '/'));
        }

        private static OpenApiDocumentFormat DetectFormatFromPath(string path)
        {
            string extension = Path.GetExtension(path);
            if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                return OpenApiDocumentFormat.Json;
            }

            if (string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
            {
                return OpenApiDocumentFormat.Yaml;
            }

            return (OpenApiDocumentFormat)(-1);
        }

        private static OpenApiDocumentFormat ResolveRemoteFormat(
            Uri finalUri,
            string contentType)
        {
            OpenApiDocumentFormat pathFormat = DetectFormatFromPath(finalUri.AbsolutePath);
            OpenApiDocumentFormat? mediaFormat = DetectFormatFromMediaType(contentType);
            if (pathFormat == (OpenApiDocumentFormat)(-1) && !mediaFormat.HasValue)
            {
                throw new TrustedRemoteFetchException(
                    "The final OpenAPI URL has neither a recognized extension nor a supported Content-Type: " +
                    RedactUri(finalUri));
            }

            if (pathFormat != (OpenApiDocumentFormat)(-1) && mediaFormat.HasValue && pathFormat != mediaFormat.Value)
            {
                throw new TrustedRemoteFetchException(
                    "The final OpenAPI URL extension and Content-Type disagree: " + RedactUri(finalUri));
            }

            return mediaFormat ?? pathFormat;
        }

        private static OpenApiDocumentFormat? DetectFormatFromMediaType(string mediaType)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                return null;
            }

            string normalized = mediaType.Trim().ToLowerInvariant();
            if (normalized == "application/json" || normalized.EndsWith("+json", StringComparison.Ordinal))
            {
                return OpenApiDocumentFormat.Json;
            }

            if (normalized == "application/yaml" ||
                normalized == "application/x-yaml" ||
                normalized == "text/yaml" ||
                normalized == "text/x-yaml" ||
                normalized == "text/x-yml")
            {
                return OpenApiDocumentFormat.Yaml;
            }

            return null;
        }

        private static string GetMediaType(MediaTypeHeaderValue? contentType)
        {
            return contentType == null ? string.Empty : contentType.MediaType ?? string.Empty;
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.Moved ||
                   statusCode == HttpStatusCode.Redirect ||
                   statusCode == HttpStatusCode.RedirectMethod ||
                   statusCode == HttpStatusCode.TemporaryRedirect ||
                   (int)statusCode == 308;
        }

        private static Uri RemoveFragment(Uri uri)
        {
            var builder = new UriBuilder(uri) { Fragment = string.Empty };
            return builder.Uri;
        }

        private static string RedactUri(Uri uri)
        {
            if (uri == null)
            {
                return string.Empty;
            }

            var builder = new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty,
                UserName = string.Empty,
                Password = string.Empty,
            };
            return builder.Uri.AbsoluteUri;
        }

        private static bool ContainsWhitespace(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsWhiteSpace(value[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Sha256Hex(string value)
        {
            return Sha256Hex(StrictUtf8.GetBytes(value));
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static void ValidateSpecId(string specId)
        {
            Guid parsed;
            if (specId == null ||
                !Guid.TryParseExact(specId, "N", out parsed) ||
                !string.Equals(parsed.ToString("N"), specId, StringComparison.Ordinal))
            {
                throw new ArgumentException("specId must be a lower-case Guid in N format.", nameof(specId));
            }
        }

        private static void EnsureNoSymbolicLink(string root, string candidate)
        {
            string current = Path.GetFullPath(candidate);
            while (IsSameOrDescendant(root, current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "Local external references must not traverse a symbolic link.");
                }

                if (PathsEqual(root, current))
                {
                    break;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }

        private static bool IsSameOrDescendant(string parent, string candidate)
        {
            string normalizedParent = Path.GetFullPath(parent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCandidate = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (PathsEqual(normalizedParent, normalizedCandidate))
            {
                return true;
            }

            return normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                PathComparison);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(left, right, PathComparison);
        }

        private static StringComparison PathComparison
        {
            get { return Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal; }
        }

        private sealed class SourceRequest
        {
            private SourceRequest(
                bool isRemote,
                string localPath,
                string fetchKey,
                string displayUri,
                OpenApiDocumentFormat format,
                bool isRoot)
            {
                IsRemote = isRemote;
                LocalPath = localPath;
                FetchKey = fetchKey;
                DisplayUri = displayUri;
                Format = format;
                IsRoot = isRoot;
            }

            internal bool IsRemote { get; }
            internal string LocalPath { get; }
            internal string FetchKey { get; }
            internal string DisplayUri { get; }
            internal OpenApiDocumentFormat Format { get; }
            internal bool IsRoot { get; }

            internal static SourceRequest Local(
                string path,
                string fetchKey,
                OpenApiDocumentFormat format,
                bool isRoot)
            {
                return new SourceRequest(false, path, fetchKey, path.Replace('\\', '/'), format, isRoot);
            }

            internal static SourceRequest Remote(
                string fetchKey,
                string displayUri,
                OpenApiDocumentFormat format,
                bool isRoot)
            {
                return new SourceRequest(true, string.Empty, fetchKey, displayUri, format, isRoot);
            }
        }

        private sealed class DocumentPayload
        {
            internal DocumentPayload(
                byte[] bytes,
                OpenApiDocumentFormat format,
                string rawSha256,
                string effectiveUri,
                int statusCode,
                int redirectCount)
            {
                Bytes = bytes;
                Format = format;
                RawSha256 = rawSha256;
                EffectiveUri = effectiveUri;
                StatusCode = statusCode;
                RedirectCount = redirectCount;
            }

            internal byte[] Bytes { get; }
            internal OpenApiDocumentFormat Format { get; }
            internal string RawSha256 { get; }
            internal string EffectiveUri { get; }
            internal int StatusCode { get; }
            internal int RedirectCount { get; }
        }

        private sealed class ReferenceValue
        {
            internal ReferenceValue(string pointer, SpecStringNode node)
            {
                Pointer = pointer;
                Node = node;
            }

            internal string Pointer { get; }
            internal SpecStringNode Node { get; }
            internal string Value { get { return Node.Value; } }
        }
    }

}
