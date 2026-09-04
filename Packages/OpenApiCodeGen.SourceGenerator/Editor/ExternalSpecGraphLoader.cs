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
        }

        internal async Task<NormalizedSpecGraph> LoadAsync(
            string source,
            string specId,
            CancellationToken cancellationToken)
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
                var graph = new GraphBuilder(this, specId, graphCancellation.Token);
                return await graph.LoadAsync(source).ConfigureAwait(false);
            }
        }

        private sealed class TrustedRemoteFetchException : InvalidOperationException
        {
            internal TrustedRemoteFetchException(string message)
                : base(message)
            {
            }
        }

        private sealed class GraphBuilder
        {
            private readonly ExternalSpecGraphLoader owner;
            private readonly string specId;
            private readonly CancellationToken cancellationToken;
            private readonly Dictionary<string, GraphDocument> documentsByKey =
                new Dictionary<string, GraphDocument>(StringComparer.Ordinal);
            private readonly List<GraphDocument> documents = new List<GraphDocument>();
            private readonly List<ReferenceEdge> edges = new List<ReferenceEdge>();
            private long totalRawBytes;
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
                CancellationToken cancellationToken)
            {
                this.owner = owner;
                this.specId = specId;
                this.cancellationToken = cancellationToken;
            }

            internal async Task<NormalizedSpecGraph> LoadAsync(string source)
            {
                ValidateSpecId(specId);
                SourceRequest root = CreateRootRequest(source);
                rootWasUrl = root.IsRemote;
                rootRequestedDisplay = root.DisplayUri;
                rootFetchKey = root.FetchKey;
                GraphDocument rootDocument = await LoadDocumentAsync(root, true, 0).ConfigureAwait(false);

                for (int index = 0; index < documents.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CollectReferences(documents[index]);
                }

                ValidateEdges();
                for (int index = 0; index < documents.Count; index++)
                {
                    documents[index].Root = SanitizeReferences(documents[index].Root);
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
                    Sha256Hex(rootFetchKey),
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

                string path = owner.ResolveLocalPath(source, false, false);
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
                if (depth > NormalizedSpecBundleConstants.MaximumDepth)
                {
                    throw new InvalidOperationException(
                        "The external OpenAPI reference graph exceeds the maximum depth of " +
                        NormalizedSpecBundleConstants.MaximumDepth + ".");
                }

                string key = request.FetchKey;
                GraphDocument existing;
                if (documentsByKey.TryGetValue(key, out existing))
                {
                    return existing;
                }

                if (documents.Count >= NormalizedSpecBundleConstants.MaximumDocumentCount)
                {
                    throw new InvalidOperationException(
                        "The external OpenAPI reference graph exceeds the maximum document count of " +
                        NormalizedSpecBundleConstants.MaximumDocumentCount + ".");
                }

                DocumentPayload payload = request.IsRemote
                    ? await FetchRemoteAsync(request).ConfigureAwait(false)
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
                    throw new InvalidOperationException(
                        "The external OpenAPI reference graph exceeds the maximum size of 32 MiB.");
                }

                SpecNode root = Parse(payload.Bytes, payload.Format, request.DisplayUri);
                string effectiveKey = payload.EffectiveUri;
                GraphDocument effectiveDocument;
                if (!isRoot &&
                    documentsByKey.TryGetValue(effectiveKey, out effectiveDocument))
                {
                    documentsByKey[key] = effectiveDocument;
                    return effectiveDocument;
                }

                string documentIdentity = owner.GetDocumentIdentity(
                    effectiveKey,
                    request.IsRemote);
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

            private async Task ResolveDocumentReferencesAsync(GraphDocument document, int depth)
            {
                foreach (ReferenceValue reference in EnumerateReferences(document.Root, string.Empty))
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
                string localPath = owner.ResolveLocalPath(
                    fetchUri.LocalPath,
                    !sameDocument,
                    !sourceDocument.IsRemote);
                if (sourceDocument.IsRemote)
                {
                    throw new NotSupportedException(
                        "A remote document must not reference a local file: " + sourceDocument.SourcePath);
                }

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
                catch (TrustedRemoteFetchException exception)
                {
                    // Only messages created by this loader are allowed to cross the remote
                    // boundary. HttpClient/content/stream exceptions are redacted below.
                    throw new InvalidOperationException(exception.Message);
                }
                catch (Exception)
                {
                    // HttpClient and stream exceptions can include the complete request URI in
                    // their message or inner exception. Remote failures must never retain that
                    // data, because query values are treated as user-provided secrets.
                    throw new InvalidOperationException(
                        "The remote OpenAPI document could not be fetched.");
                }
            }

            private async Task<DocumentPayload> FetchRemoteCoreAsync(SourceRequest request)
            {
                using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;
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
                                response = await client.SendAsync(
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

            private static SpecNode SanitizeReferences(SpecNode node)
            {
                SpecObjectNode? objectNode = node as SpecObjectNode;
                if (objectNode != null)
                {
                    var properties = new List<SpecProperty>(objectNode.Properties.Count);
                    foreach (SpecProperty property in objectNode.Properties)
                    {
                        SpecNode value = SanitizeReferences(property.Value);
                        if (string.Equals(property.Name, "$ref", StringComparison.Ordinal))
                        {
                            SpecStringNode? reference = value as SpecStringNode;
                            if (reference != null)
                            {
                                value = new SpecStringNode(
                                    reference.Line,
                                    reference.Column,
                                    RedactReference(reference.Value));
                            }
                        }

                        properties.Add(new SpecProperty(
                            property.Name,
                            property.Line,
                            property.Column,
                            value));
                    }

                    return new SpecObjectNode(objectNode.Line, objectNode.Column, properties);
                }

                SpecArrayNode? arrayNode = node as SpecArrayNode;
                if (arrayNode != null)
                {
                    var items = new List<SpecNode>(arrayNode.Items.Count);
                    foreach (SpecNode item in arrayNode.Items)
                    {
                        items.Add(SanitizeReferences(item));
                    }

                    return new SpecArrayNode(arrayNode.Line, arrayNode.Column, items);
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

            private void CollectReferences(GraphDocument document)
            {
                foreach (ReferenceValue reference in EnumerateReferences(document.Root, string.Empty))
                {
                    // ResolveDocumentReferencesAsync already built the edge. This second pass only
                    // catches malformed special keywords in documents loaded through a deduplicated path.
                    if (reference.Value.Length == 0)
                    {
                        throw new InvalidOperationException(
                            "An external reference must not be empty: " + document.SourcePath);
                    }
                }
            }

            private void ValidateEdges()
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < edges.Count; index++)
                {
                    ReferenceEdge edge = edges[index];
                    string key = edge.SourceDocumentId + "\n" + edge.SourcePointer + "\n" +
                                 edge.TargetDocumentId + "\n" + edge.TargetPointer;
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

            private static IEnumerable<ReferenceValue> EnumerateReferences(SpecNode node, string pointer)
            {
                SpecObjectNode? objectNode = node as SpecObjectNode;
                if (objectNode != null)
                {
                    foreach (SpecProperty property in objectNode.Properties)
                    {
                        string propertyPointer = AppendPointer(pointer, property.Name);
                        // JSON Schema identity keywords stay in the Bundle for analyzer semantic
                        // validation. Only ordinary $ref values participate in the edge map.

                        if (string.Equals(property.Name, "$ref", StringComparison.Ordinal))
                        {
                            SpecStringNode? referenceNode = property.Value as SpecStringNode;
                            if (referenceNode == null)
                            {
                                throw new InvalidOperationException(
                                    "An OpenAPI '$ref' value must be a string at " + propertyPointer + ".");
                            }

                            yield return new ReferenceValue(
                                propertyPointer,
                                referenceNode.Value);
                        }

                        foreach (ReferenceValue nested in EnumerateReferences(property.Value, propertyPointer))
                        {
                            yield return nested;
                        }
                    }

                    yield break;
                }

                SpecArrayNode? arrayNode = node as SpecArrayNode;
                if (arrayNode != null)
                {
                    for (int index = 0; index < arrayNode.Items.Count; index++)
                    {
                        foreach (ReferenceValue nested in EnumerateReferences(
                                     arrayNode.Items[index],
                                     AppendPointer(pointer, index.ToString(CultureInfo.InvariantCulture))))
                        {
                            yield return nested;
                        }
                    }
                }
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

            private static string AppendPointer(string pointer, string segment)
            {
                return pointer + "/" + segment.Replace("~", "~0").Replace("/", "~1");
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

        private string ResolveLocalPath(string path, bool isExternal, bool sourceIsLocal)
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
                                                    ToDisplayPath(fullPath));
            }

            if (isExternal)
            {
                EnsureNoSymbolicLink(projectRoot, fullPath);
                if (!IsSameOrDescendant(projectRoot, fullPath))
                {
                    throw new InvalidOperationException(
                        "Local external references must remain inside the Unity project: " +
                        ToDisplayPath(fullPath));
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
                // The effective fetch key intentionally retains query because it is part of the
                // remote identity. Fragments have already been removed before reaching this point.
                return RemoveFragment(new Uri(effectiveUri, UriKind.Absolute)).AbsoluteUri;
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
                        "Local external references must not traverse a symbolic link: " + current);
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
                OpenApiDocumentFormat format)
            {
                IsRemote = isRemote;
                LocalPath = localPath;
                FetchKey = fetchKey;
                DisplayUri = displayUri;
                Format = format;
            }

            internal bool IsRemote { get; }
            internal string LocalPath { get; }
            internal string FetchKey { get; }
            internal string DisplayUri { get; }
            internal OpenApiDocumentFormat Format { get; }

            internal static SourceRequest Local(
                string path,
                string fetchKey,
                OpenApiDocumentFormat format,
                bool isRoot)
            {
                return new SourceRequest(false, path, fetchKey, path.Replace('\\', '/'), format);
            }

            internal static SourceRequest Remote(
                string fetchKey,
                string displayUri,
                OpenApiDocumentFormat format,
                bool isRoot)
            {
                return new SourceRequest(true, string.Empty, fetchKey, displayUri, format);
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
            internal ReferenceValue(string pointer, string value)
            {
                Pointer = pointer;
                Value = value;
            }

            internal string Pointer { get; }
            internal string Value { get; }
        }
    }

}
