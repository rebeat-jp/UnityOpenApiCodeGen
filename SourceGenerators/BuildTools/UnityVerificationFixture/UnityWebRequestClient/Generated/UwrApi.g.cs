#nullable enable
namespace Rhycol.OpenApiCodeGen.UnityWebRequestVerification
{
    [global::Newtonsoft.Json.JsonObject(global::Newtonsoft.Json.MemberSerialization.OptIn)]
    public sealed class Item
    {
        [global::Newtonsoft.Json.JsonProperty("id", Required = global::Newtonsoft.Json.Required.Always, NullValueHandling = global::Newtonsoft.Json.NullValueHandling.Include, DefaultValueHandling = global::Newtonsoft.Json.DefaultValueHandling.Include)]
        public int Id { get; set; }

        [global::Newtonsoft.Json.JsonProperty("label", Required = global::Newtonsoft.Json.Required.DisallowNull, NullValueHandling = global::Newtonsoft.Json.NullValueHandling.Ignore)]
        public string? Label { get; set; }
    }
}

#nullable enable
namespace Rhycol.OpenApiCodeGen.UnityWebRequestVerification
{
    [global::Newtonsoft.Json.JsonObject(global::Newtonsoft.Json.MemberSerialization.OptIn)]
    public sealed class Problem
    {
        [global::Newtonsoft.Json.JsonProperty("title", Required = global::Newtonsoft.Json.Required.Always, NullValueHandling = global::Newtonsoft.Json.NullValueHandling.Include, DefaultValueHandling = global::Newtonsoft.Json.DefaultValueHandling.Include)]
        public string Title { get; set; } = null !;
        [global::System.Runtime.Serialization.OnSerializing]
        private void ValidateRequiredPropertiesOnSerializing(global::System.Runtime.Serialization.StreamingContext context)
        {
            if (Title == null)
            {
                throw new global::Newtonsoft.Json.JsonSerializationException("Required property 'title' cannot be null during serialization.");
            }
        }
    }
}

#nullable enable
namespace Rhycol.OpenApiCodeGen.UnityWebRequestVerification
{
    public partial class UwrApi
    {
        private readonly global::System.Uri? _baseAddress;
        private readonly string _baseUrl;
        /// <summary>
        /// Creates a client that sends its requests with UnityWebRequest. The server URL of the OpenAPI document is used.
        /// </summary>
        /// <param name = "baseAddress">
        /// The absolute address that a relative server URL ("/v1", "//host/v1", "v1") resolves against; the counterpart of
        /// HttpClient.BaseAddress. It may be null only when the server URL is absolute. UnityWebRequest has no base
        /// address, so a relative server URL without an absolute base address fails when a request is made. On WebGL
        /// pass new Uri(Application.absoluteURL) to resolve against the page that hosts the player.
        /// </param>
        public UwrApi(global::System.Uri? baseAddress) : this(baseAddress, "/api")
        {
        }

        /// <summary>Creates a client that sends its requests with UnityWebRequest and uses <paramref name = "baseUrl"/> as its server URL.</summary>
        /// <param name = "baseAddress">See the other constructor.</param>
        /// <param name = "baseUrl">The server URL; absolute, or relative to <paramref name = "baseAddress"/>.</param>
        public UwrApi(global::System.Uri? baseAddress, string baseUrl)
        {
            if (baseAddress != null && !baseAddress.IsAbsoluteUri)
            {
                throw new global::System.ArgumentException("The base address must be an absolute URI.", nameof(baseAddress));
            }

            _baseAddress = baseAddress;
            _baseUrl = baseUrl ?? throw new global::System.ArgumentNullException(nameof(baseUrl));
        }

        /// <summary>The server URL templates of the OpenAPI document (servers[].url), in document order. The constructor without a base URL uses the first one with each variable replaced by its default.</summary>
        public static global::System.Collections.Generic.IReadOnlyList<string> Servers { get; } = global::System.Array.AsReadOnly(new string[]{"/api", });
        /// <summary>Expands the server URL template at <paramref name = "index"/>. Variables not supplied use their default; a supplied value must be one of the declared enum values.</summary>
        public static string BuildServerUrl(int index, global::System.Collections.Generic.IReadOnlyDictionary<string, string>? variables = null)
        {
            switch (index)
            {
                case 0:
                    return ExpandServerUrl(Servers[0], global::System.Array.Empty<string[]>(), variables);
                default:
                    throw new global::System.ArgumentOutOfRangeException(nameof(index));
            }
        }

        private static string ExpandServerUrl(string template, string[][] declared, global::System.Collections.Generic.IReadOnlyDictionary<string, string>? variables)
        {
            if (variables != null)
            {
                foreach (var supplied in variables)
                {
                    bool known = false;
                    foreach (string[] variable in declared)
                    {
                        if (string.Equals(variable[0], supplied.Key, global::System.StringComparison.Ordinal))
                        {
                            known = true;
                            break;
                        }
                    }

                    if (!known)
                    {
                        throw new global::System.ArgumentException("The server variable '" + supplied.Key + "' is not declared.", nameof(variables));
                    }
                }
            }

            var values = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal);
            foreach (string[] variable in declared)
            {
                string value = variable[1];
                if (variables != null && variables.TryGetValue(variable[0], out string? suppliedValue))
                {
                    value = suppliedValue ?? throw new global::System.ArgumentException("The server variable '" + variable[0] + "' must not be null.", nameof(variables));
                }

                if (variable.Length > 2)
                {
                    bool allowed = false;
                    for (int index = 2; index < variable.Length; index++)
                    {
                        if (string.Equals(variable[index], value, global::System.StringComparison.Ordinal))
                        {
                            allowed = true;
                            break;
                        }
                    }

                    if (!allowed)
                    {
                        throw new global::System.ArgumentException("The value '" + value + "' is not allowed for the server variable '" + variable[0] + "'.", nameof(variables));
                    }
                }

                values[variable[0]] = value;
            }

            // One pass: substituted values are not scanned again for placeholders.
            var url = new global::System.Text.StringBuilder(template.Length);
            int cursor = 0;
            while (cursor < template.Length)
            {
                int open = template.IndexOf((char)123, cursor);
                int close = open < 0 ? -1 : template.IndexOf((char)125, open + 1);
                if (open < 0 || close < 0)
                {
                    url.Append(template, cursor, template.Length - cursor);
                    break;
                }

                url.Append(template, cursor, open - cursor);
                string name = template.Substring(open + 1, close - open - 1);
                if (values.TryGetValue(name, out string? replacement))
                {
                    url.Append(replacement);
                }
                else
                {
                    url.Append(template, open, close - open + 1);
                }

                cursor = close + 1;
            }

            return url.ToString();
        }

        /// <summary>List items</summary>
        /// <param name = "cancellationToken">Cancellation token.</param>
        public async global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<Item>> listItems(global::System.Threading.CancellationToken cancellationToken = default)
        {
            string relativePath = "/items";
            ValidatePathSegments(relativePath);
            var requestUri = CreateRequestUri(relativePath);
            using (var request = new global::System.Net.Http.HttpRequestMessage(global::System.Net.Http.HttpMethod.Get, requestUri))
            {
                using (var response = await SendUnityWebRequestAsync(request, cancellationToken))
                {
                    if (!(((int)response.StatusCode == 200)))
                    {
                        throw new UwrApiException(response.StatusCode, response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    }

                    string[] __OacgContentType_DeclaredMediaTypes;
                    switch ((int)response.StatusCode)
                    {
                        case 200:
                            __OacgContentType_DeclaredMediaTypes = new string[]{"application/json"};
                            break;
                        default:
                            __OacgContentType_DeclaredMediaTypes = new string[]{};
                            break;
                    }

                    __OacgContentType_Validate(response, __OacgContentType_DeclaredMediaTypes);
                    string responseBody = response.Content == null ? string.Empty : await ReadJsonResponseBodyAsync(response.Content).ConfigureAwait(false);
                    if (global::System.String.IsNullOrWhiteSpace(responseBody))
                    {
                        throw new global::Newtonsoft.Json.JsonSerializationException("The successful response requires a JSON body.");
                    }

                    if (global::System.String.Equals(responseBody.Trim(), "null", global::System.StringComparison.Ordinal))
                    {
                        throw new global::Newtonsoft.Json.JsonSerializationException("The successful response requires a non-null JSON body.");
                    }

                    var defaultResponseSettings = global::Newtonsoft.Json.JsonConvert.DefaultSettings?.Invoke();
                    var responseSerializer = global::Newtonsoft.Json.JsonSerializer.Create();
                    responseSerializer.DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None;
                    responseSerializer.MaxDepth = defaultResponseSettings?.MaxDepth > 0 ? defaultResponseSettings.MaxDepth : 64;
                    string validatedResponseBody = __OacgJsonContract_Parse(responseBody, __OacgJsonContract_0, false, responseSerializer.MaxDepth);
                    var deserializedResponse = __OacgJsonContract_Deserialize<global::System.Collections.Generic.List<Item>>(validatedResponseBody, responseSerializer);
                    if (deserializedResponse is null)
                    {
                        throw new global::Newtonsoft.Json.JsonSerializationException("The successful response requires a non-null JSON body.");
                    }

                    __OacgResponseValidator_0(deserializedResponse, "$", "response", new global::System.Collections.Generic.Dictionary<object, global::System.Collections.Generic.HashSet<int>>(new __OacgResponseValidator_Comparer()));
                    return deserializedResponse!;
                }
            }
        }

        /// <summary>Create an item</summary>
        /// <param name = "body">JSON request body.</param>
        /// <param name = "cancellationToken">Cancellation token.</param>
        public async global::System.Threading.Tasks.Task<Item> createItem(Item body, global::System.Threading.CancellationToken cancellationToken = default)
        {
            if (body is null)
            {
                throw new global::System.ArgumentNullException(nameof(body));
            }

            string relativePath = "/items";
            ValidatePathSegments(relativePath);
            var requestUri = CreateRequestUri(relativePath);
            using (var request = new global::System.Net.Http.HttpRequestMessage(global::System.Net.Http.HttpMethod.Post, requestUri))
            {
                string requestJson = __OacgContentType_SerializeRequestJson(body);
                request.Content = CreateJsonContent(requestJson, "application/json");
                using (var response = await SendUnityWebRequestAsync(request, cancellationToken))
                {
                    if (!(((int)response.StatusCode == 201)))
                    {
                        string oacgErrorBody = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        object? oacgError = null;
                        global::System.Exception? oacgErrorFailure = null;
                        if ((int)response.StatusCode == 400)
                        {
                            oacgError = __OacgBody_DeserializeError<Problem>(oacgErrorBody, out oacgErrorFailure);
                        }

                        throw new UwrApiException(response.StatusCode, oacgErrorBody, oacgError, oacgErrorFailure);
                    }

                    string[] __OacgContentType_DeclaredMediaTypes;
                    switch ((int)response.StatusCode)
                    {
                        case 201:
                            __OacgContentType_DeclaredMediaTypes = new string[]{"application/json"};
                            break;
                        default:
                            __OacgContentType_DeclaredMediaTypes = new string[]{};
                            break;
                    }

                    __OacgContentType_Validate(response, __OacgContentType_DeclaredMediaTypes);
                    string responseBody = response.Content == null ? string.Empty : await ReadJsonResponseBodyAsync(response.Content).ConfigureAwait(false);
                    if (global::System.String.IsNullOrWhiteSpace(responseBody))
                    {
                        throw new global::Newtonsoft.Json.JsonSerializationException("The successful response requires a JSON body.");
                    }

                    if (global::System.String.Equals(responseBody.Trim(), "null", global::System.StringComparison.Ordinal))
                    {
                        throw new global::Newtonsoft.Json.JsonSerializationException("The successful response requires a non-null JSON body.");
                    }

                    var defaultResponseSettings = global::Newtonsoft.Json.JsonConvert.DefaultSettings?.Invoke();
                    var responseSerializer = global::Newtonsoft.Json.JsonSerializer.Create();
                    responseSerializer.DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None;
                    responseSerializer.MaxDepth = defaultResponseSettings?.MaxDepth > 0 ? defaultResponseSettings.MaxDepth : 64;
                    string validatedResponseBody = __OacgJsonContract_Parse(responseBody, __OacgJsonContract_1, false, responseSerializer.MaxDepth);
                    var deserializedResponse = __OacgJsonContract_Deserialize<Item>(validatedResponseBody, responseSerializer);
                    if (deserializedResponse is null)
                    {
                        throw new global::Newtonsoft.Json.JsonSerializationException("The successful response requires a non-null JSON body.");
                    }

                    return deserializedResponse!;
                }
            }
        }

        private static readonly global::Newtonsoft.Json.JsonConverter DateOnlyJsonConverterInstance = new DateOnlyJsonConverter();
        private static string __OacgContentType_SerializeRequestJson(object? value)
        {
            var serializer = global::Newtonsoft.Json.JsonSerializer.Create();
            serializer.Converters.Add(DateOnlyJsonConverterInstance);
            using (var textWriter = new global::System.IO.StringWriter(global::System.Globalization.CultureInfo.InvariantCulture))
            using (var jsonWriter = new global::Newtonsoft.Json.JsonTextWriter(textWriter))
            {
                serializer.Serialize(jsonWriter, value);
                jsonWriter.Flush();
                return textWriter.ToString();
            }
        }

        private static void __OacgContentType_Validate(global::System.Net.Http.HttpResponseMessage response, string[] declaredMediaTypes)
        {
            if (declaredMediaTypes.Length == 0)
                return;
            if (response.Content == null || !response.Content.Headers.TryGetValues("Content-Type", out var headerValues))
                return;
            string? header = null;
            foreach (string value in headerValues)
            {
                if (header != null)
                    throw new global::Newtonsoft.Json.JsonSerializationException("The response contains multiple Content-Type values.");
                header = value;
            }

            if (string.IsNullOrWhiteSpace(header) || !__OacgContentType_ValidateHasValidHead(header) || !global::System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(header, out var actual) || actual.MediaType == null || actual.MediaType.IndexOf('*') >= 0)
                throw new global::Newtonsoft.Json.JsonSerializationException("The response Content-Type is malformed.");
            var actualParameters = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in actual.Parameters)
            {
                if (parameter.Value == null || actualParameters.ContainsKey(parameter.Name) || !__OacgContentType_ValidateDecodeParameter(parameter.Value, out string decodedValue))
                    throw new global::Newtonsoft.Json.JsonSerializationException("The response Content-Type is malformed.");
                actualParameters.Add(parameter.Name, decodedValue);
            }

            foreach (string declaredValue in declaredMediaTypes)
            {
                if (!global::System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(declaredValue, out var declared) || declared.MediaType == null)
                    continue;
                int separator = declared.MediaType.IndexOf('/');
                int actualSeparator = actual.MediaType.IndexOf('/');
                if (separator <= 0 || actualSeparator <= 0 || !global::System.String.Equals(declared.MediaType.Substring(0, separator), actual.MediaType.Substring(0, actualSeparator), global::System.StringComparison.OrdinalIgnoreCase))
                    continue;
                string subtype = declared.MediaType.Substring(separator + 1);
                string actualSubtype = actual.MediaType.Substring(actualSeparator + 1);
                bool subtypeMatches = global::System.String.Equals(subtype, actualSubtype, global::System.StringComparison.OrdinalIgnoreCase) || (global::System.String.Equals(subtype, "*+json", global::System.StringComparison.OrdinalIgnoreCase) && actualSubtype.Length > 5 && actualSubtype.EndsWith("+json", global::System.StringComparison.OrdinalIgnoreCase));
                if (!subtypeMatches)
                    continue;
                bool parametersMatch = true;
                foreach (var parameter in declared.Parameters)
                {
                    if (parameter.Value == null || !__OacgContentType_ValidateDecodeParameter(parameter.Value, out string expected))
                    {
                        parametersMatch = false;
                        break;
                    }

                    if (!actualParameters.TryGetValue(parameter.Name, out string? actualValue) || !global::System.String.Equals(expected, actualValue, global::System.String.Equals(parameter.Name, "charset", global::System.StringComparison.OrdinalIgnoreCase) ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal))
                    {
                        parametersMatch = false;
                        break;
                    }
                }

                if (parametersMatch)
                    return;
            }

            throw new global::Newtonsoft.Json.JsonSerializationException("The response Content-Type '" + header + "' does not match the declared media type.");
        }

        private static bool __OacgContentType_ValidateDecodeParameter(string value, out string decoded)
        {
            decoded = string.Empty;
            if (value.Length > 0 && (value[0] == (char)34 || value[value.Length - 1] == (char)34))
            {
                if (value.Length < 2 || value[0] != (char)34 || value[value.Length - 1] != (char)34)
                    return false;
                var builder = new global::System.Text.StringBuilder(value.Length - 2);
                for (int index = 1; index < value.Length - 1; index++)
                {
                    char character = value[index];
                    if (character == (char)92)
                    {
                        if (++index >= value.Length - 1)
                            return false;
                        character = value[index];
                        if (!(character == (char)9 || (character >= (char)32 && character <= (char)126) || (character >= (char)128 && character <= (char)255)))
                            return false;
                    }
                    else if (!(character == (char)9 || character == (char)32 || character == (char)33 || (character >= (char)35 && character <= (char)91) || (character >= (char)93 && character <= (char)126) || (character >= (char)128 && character <= (char)255)))
                        return false;
                    builder.Append(character);
                }

                decoded = builder.ToString();
                return true;
            }

            if (value.IndexOf((char)34) >= 0 || value.IndexOf((char)92) >= 0)
                return false;
            decoded = value;
            return true;
        }

        private static bool __OacgContentType_ValidateHasValidHead(string value)
        {
            int index = 0;
            while (index < value.Length && (value[index] == (char)32 || value[index] == (char)9))
                index++;
            int start = index;
            while (index < value.Length && __OacgContentType_ValidateIsTokenCharacter(value[index]))
                index++;
            if (index == start || index >= value.Length || value[index] != (char)47)
                return false;
            index++;
            start = index;
            while (index < value.Length && __OacgContentType_ValidateIsTokenCharacter(value[index]))
                index++;
            return index > start && (index == value.Length || value[index] == (char)59 || value[index] == (char)32 || value[index] == (char)9);
        }

        private static bool __OacgContentType_ValidateIsTokenCharacter(char character)
        {
            return (character >= (char)48 && character <= (char)57) || (character >= (char)65 && character <= (char)90) || (character >= (char)97 && character <= (char)122) || character == (char)33 || character == (char)35 || character == (char)36 || character == (char)37 || character == (char)38 || character == (char)39 || character == (char)42 || character == (char)43 || character == (char)45 || character == (char)46 || character == (char)94 || character == (char)95 || character == (char)96 || character == (char)124 || character == (char)126;
        }

        private static string __OacgContentType_ValidateHeaderValue(string value, string name)
        {
            foreach (char character in value)
            {
                if ((character < (char)32 && character != (char)9) || character > (char)126)
                    throw new global::System.ArgumentException("Request header '" + name + "' contains a character outside the supported ASCII HTTP field value range.", nameof(value));
            }

            return value;
        }

        private global::System.Uri CreateRequestUri(string relativePath)
        {
            return CreateRequestUri(relativePath, _baseUrl);
        }

        private global::System.Uri CreateRequestUri(string relativePath, string serverUrl)
        {
            SplitPathAndQuery(relativePath, out string operationPath, out string operationQuery);
            int baseFragment = serverUrl.IndexOf('#');
            string baseUrl = baseFragment < 0 ? serverUrl : serverUrl.Substring(0, baseFragment);
            if (baseUrl.StartsWith("//", global::System.StringComparison.Ordinal))
            {
                if (_baseAddress is null)
                {
                    throw new global::System.InvalidOperationException("A base address is required for a network-path server URL.");
                }

                var networkBaseUri = new global::System.Uri(_baseAddress, baseUrl);
                return CombineAbsoluteUri(networkBaseUri, operationPath, operationQuery);
            }

            if (baseUrl.StartsWith("/", global::System.StringComparison.Ordinal))
            {
                if (_baseAddress is null)
                {
                    throw new global::System.InvalidOperationException("A base address is required for a root-relative server URL.");
                }

                var rootBaseUri = new global::System.Uri(_baseAddress, baseUrl);
                return CombineAbsoluteUri(rootBaseUri, operationPath, operationQuery);
            }

            if (global::System.Uri.TryCreate(baseUrl, global::System.UriKind.Absolute, out var absoluteBaseUri) && (absoluteBaseUri.Scheme == global::System.Uri.UriSchemeHttp || absoluteBaseUri.Scheme == global::System.Uri.UriSchemeHttps))
            {
                return CombineAbsoluteUri(absoluteBaseUri, operationPath, operationQuery);
            }

            SplitPathAndQuery(baseUrl, out string basePath, out string baseQuery);
            string combinedPath = CombinePaths(basePath, operationPath);
            string combinedQuery = CombineQueries(baseQuery, operationQuery);
            if (_baseAddress != null)
            {
                var resolvedUri = new global::System.Uri(_baseAddress, combinedPath.TrimStart('/'));
                string resolvedQuery = CombineQueries(_baseAddress.Query.TrimStart('?'), combinedQuery);
                var resolvedBuilder = new global::System.UriBuilder(resolvedUri)
                {Query = resolvedQuery};
                return resolvedBuilder.Uri;
            }

            string combined = AppendQuery(combinedPath, combinedQuery);
            if (global::System.Uri.TryCreate(combined, global::System.UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri;
            }

            return new global::System.Uri(combined, global::System.UriKind.Relative);
        }

        private static global::System.Uri CombineAbsoluteUri(global::System.Uri baseUri, string operationPath, string operationQuery)
        {
            var builder = new global::System.UriBuilder(baseUri)
            {Path = CombinePaths(baseUri.AbsolutePath, operationPath), Query = CombineQueries(baseUri.Query.TrimStart('?'), operationQuery)};
            return builder.Uri;
        }

        private static string CombinePaths(string basePath, string operationPath)
        {
            if (string.IsNullOrEmpty(basePath))
            {
                return operationPath;
            }

            if (string.IsNullOrEmpty(operationPath))
            {
                return basePath;
            }

            return basePath.TrimEnd('/') + "/" + operationPath.TrimStart('/');
        }

        private static string CombineQueries(string baseQuery, string operationQuery)
        {
            if (string.IsNullOrEmpty(baseQuery))
            {
                return operationQuery;
            }

            if (string.IsNullOrEmpty(operationQuery))
            {
                return baseQuery;
            }

            return baseQuery.TrimEnd('&') + "&" + operationQuery.TrimStart('&');
        }

        private static string AppendQuery(string path, string query)
        {
            return string.IsNullOrEmpty(query) ? path : path + "?" + query;
        }

        private static void SplitPathAndQuery(string value, out string path, out string query)
        {
            int separator = value.IndexOf('?');
            if (separator < 0)
            {
                path = value;
                query = string.Empty;
                return;
            }

            path = value.Substring(0, separator);
            query = value.Substring(separator + 1);
        }

        private static void ValidatePathSegments(string relativePath)
        {
            SplitPathAndQuery(relativePath, out string path, out _);
            string[] segments = path.Split('/');
            foreach (string segment in segments)
            {
                string decodedSegment = global::System.Uri.UnescapeDataString(segment);
                if (decodedSegment == "." || decodedSegment == "..")
                {
                    throw new global::System.ArgumentException("Path parameter values must not produce '.' or '..' path segments.", nameof(relativePath));
                }
            }
        }

        private static global::System.Net.Http.HttpContent CreateJsonContent(string requestJson, string mediaType)
        {
            var contentType = global::System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mediaType);
            global::System.Text.Encoding encoding = global::System.Text.Encoding.UTF8;
            if (string.IsNullOrWhiteSpace(contentType.CharSet))
            {
                contentType.CharSet = encoding.WebName;
            }
            else
            {
                string charset = contentType.CharSet.Trim().Trim('"');
                encoding = global::System.Text.Encoding.GetEncoding(charset);
            }

            var content = new global::System.Net.Http.StringContent(requestJson, encoding, contentType.MediaType ?? "application/json");
            content.Headers.ContentType = contentType;
            return content;
        }

        private static async global::System.Threading.Tasks.Task<string> ReadJsonResponseBodyAsync(global::System.Net.Http.HttpContent content)
        {
            byte[] bytes = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            string? charset = content.Headers.ContentType?.CharSet;
            global::System.Text.Encoding encoding;
            int offset = 0;
            if (!string.IsNullOrWhiteSpace(charset))
            {
                charset = charset.Trim();
                if (charset.Length >= 2 && charset[0] == '"' && charset[charset.Length - 1] == '"')
                {
                    charset = charset.Substring(1, charset.Length - 2);
                }

                try
                {
                    encoding = global::System.Text.Encoding.GetEncoding(charset, global::System.Text.EncoderFallback.ExceptionFallback, global::System.Text.DecoderFallback.ExceptionFallback);
                }
                catch (global::System.ArgumentException exception)
                {
                    throw new global::System.InvalidOperationException("The response charset is not supported.", exception);
                }

                byte[] preamble = encoding.GetPreamble();
                if (HasPreamble(bytes, preamble))
                {
                    offset = preamble.Length;
                }
            }
            else if (HasPreamble(bytes, new byte[]{0xEF, 0xBB, 0xBF}))
            {
                encoding = new global::System.Text.UTF8Encoding(false, true);
                offset = 3;
            }
            else if (HasPreamble(bytes, new byte[]{0xFF, 0xFE, 0x00, 0x00}))
            {
                encoding = new global::System.Text.UTF32Encoding(false, false, true);
                offset = 4;
            }
            else if (HasPreamble(bytes, new byte[]{0xFF, 0xFE}))
            {
                encoding = new global::System.Text.UnicodeEncoding(false, false, true);
                offset = 2;
            }
            else if (HasPreamble(bytes, new byte[]{0xFE, 0xFF}))
            {
                encoding = new global::System.Text.UnicodeEncoding(true, false, true);
                offset = 2;
            }
            else
            {
                encoding = new global::System.Text.UTF8Encoding(false, true);
            }

            try
            {
                return encoding.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (global::System.Text.DecoderFallbackException exception)
            {
                throw new global::Newtonsoft.Json.JsonSerializationException("The successful response contains invalid encoded bytes.", exception);
            }
        }

        private static bool HasPreamble(byte[] bytes, byte[] preamble)
        {
            if (preamble.Length == 0 || bytes.Length < preamble.Length)
            {
                return false;
            }

            for (int index = 0; index < preamble.Length; index++)
            {
                if (bytes[index] != preamble[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static string ConvertToString(object value)
        {
            if (value == null)
            {
                throw new global::System.ArgumentNullException(nameof(value));
            }

            if (value is string text)
            {
                return text;
            }

            if (value is byte[] bytes)
            {
                return global::System.Convert.ToBase64String(bytes);
            }

            if (value is bool boolean)
            {
                return boolean ? "true" : "false";
            }

            if (value is global::System.DateTime date)
            {
                return date.ToString("yyyy-MM-dd", global::System.Globalization.CultureInfo.InvariantCulture);
            }

            if (value is global::System.DateTimeOffset dateTime)
            {
                return dateTime.ToString("O", global::System.Globalization.CultureInfo.InvariantCulture);
            }

            if (value is global::System.Guid guid)
            {
                return guid.ToString("D");
            }

            if (value is global::System.Enum)
            {
                var serializer = global::Newtonsoft.Json.JsonSerializer.Create();
                var token = global::Newtonsoft.Json.Linq.JToken.FromObject(value, serializer);
                return (string? )token ?? string.Empty;
            }

            return value is global::System.IFormattable formattable ? formattable.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty;
        }

        private sealed class DateOnlyJsonConverter : global::Newtonsoft.Json.JsonConverter
        {
            public override bool CanRead => false;
            public override bool CanConvert(global::System.Type objectType)
            {
                return objectType == typeof(global::System.DateTime) || objectType == typeof(global::System.DateTime? );
            }

            public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, object? value, global::Newtonsoft.Json.JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }

                // JValue writes its DateTime through a converter with a default serializer. Only members typed as date use the request serializer that carries this converter, so a DateTime held by a JToken keeps its full value.
                if (!serializer.Converters.Contains(this))
                {
                    writer.WriteValue((global::System.DateTime)value);
                    return;
                }

                var date = (global::System.DateTime)value;
                writer.WriteValue(date.ToString("yyyy-MM-dd", global::System.Globalization.CultureInfo.InvariantCulture));
            }

            public override object? ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, object? existingValue, global::Newtonsoft.Json.JsonSerializer serializer)
            {
                throw new global::System.NotSupportedException();
            }
        }

        /// <summary>
        /// The longest time to wait for a response. null (the default) waits without a limit. UnityWebRequest takes
        /// whole seconds, so the value is rounded up. A request that runs out of time throws a TaskCanceledException
        /// whose InnerException is a TimeoutException (the exception HttpClient throws for HttpClient.Timeout).
        /// A failed request counts as timed out when the UnityWebRequest error text mentions a timeout or when the time since
        /// the send reached 90% of the rounded timeout (timers may fire slightly before the stopwatch reaches the limit).
        /// </summary>
        public global::System.TimeSpan? RequestTimeout { get; set; }

        /// <summary>
        /// Called with every UnityWebRequest after the generated code has set it up and right before it is sent: set a
        /// certificate handler, the redirect limit or other options here. It runs on the thread that called the client method.
        /// </summary>
        public global::System.Action<global::UnityEngine.Networking.UnityWebRequest>? ConfigureUnityWebRequest { get; set; }

        /// <summary>
        /// Sends the request with UnityWebRequest and returns the answer as an HttpResponseMessage.
        /// The UnityWebRequest is created, sent and disposed on the context of the caller, which has to be the Unity main
        /// thread: no continuation before the send uses ConfigureAwait(false). Cancelling the token aborts the request and
        /// throws an OperationCanceledException. A network failure throws an HttpRequestException. An HTTP error status
        /// (4xx, 5xx) is not an exception: it is returned like HttpClient returns it.
        /// Differences from HttpClient: UnityWebRequest merges the values of a repeated response header into one
        /// comma-separated value, the response reason phrase is not available, and the headers that the platform
        /// restricts (Host, Connection, Expect, Transfer-Encoding, Cookie / User-Agent on WebGL, ...) cannot be sent: UnityWebRequest refuses them and the
        /// call throws an InvalidOperationException that names the header (the original exception is its InnerException);
        /// the header is never dropped silently. Call the client methods from the Unity main thread (a SynchronizationContext
        /// is needed for the Abort() of a cancelled token to run there; without one it runs on the cancelling thread).
        /// </summary>
        private async global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> SendUnityWebRequestAsync(global::System.Net.Http.HttpRequestMessage request, global::System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            global::System.Uri? unityRequestUri = request.RequestUri;
            if (unityRequestUri is null || !unityRequestUri.IsAbsoluteUri)
            {
                throw new global::System.InvalidOperationException("UnityWebRequest needs an absolute request URI: pass an absolute base address to the constructor or use an absolute server URL.");
            }

            // No ConfigureAwait(false): the code after this await still runs on the caller's context.
            byte[]? requestBody = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync();
            cancellationToken.ThrowIfCancellationRequested();
            using (var unityRequest = new global::UnityEngine.Networking.UnityWebRequest(unityRequestUri.AbsoluteUri, request.Method.Method))
            {
                unityRequest.downloadHandler = new global::UnityEngine.Networking.DownloadHandlerBuffer();
                if (requestBody != null && requestBody.Length > 0)
                {
                    unityRequest.uploadHandler = new global::UnityEngine.Networking.UploadHandlerRaw(requestBody);
                }

                foreach (var header in request.Headers)
                {
                    SetUnityRequestHeader(unityRequest, header.Key, header.Value);
                }

                if (request.Content != null)
                {
                    foreach (var header in request.Content.Headers)
                    {
                        // UnityWebRequest computes the length of the body itself.
                        if (!string.Equals(header.Key, "Content-Length", global::System.StringComparison.OrdinalIgnoreCase))
                        {
                            SetUnityRequestHeader(unityRequest, header.Key, header.Value);
                        }
                    }

                    if (unityRequest.uploadHandler != null && request.Content.Headers.ContentType != null)
                    {
                        unityRequest.uploadHandler.contentType = request.Content.Headers.ContentType.ToString();
                    }
                }

                int timeoutSeconds = 0;
                global::System.TimeSpan? timeout = RequestTimeout;
                if (timeout.HasValue && timeout.Value > global::System.TimeSpan.Zero)
                {
                    double seconds = global::System.Math.Ceiling(timeout.Value.TotalSeconds);
                    timeoutSeconds = seconds >= int.MaxValue ? int.MaxValue : (int)seconds;
                    unityRequest.timeout = timeoutSeconds;
                }

                ConfigureUnityWebRequest?.Invoke(unityRequest);
                var completion = new global::System.Threading.Tasks.TaskCompletionSource<bool>();
                var context = global::System.Threading.SynchronizationContext.Current;
                bool finished = false;
                var finishedGate = new object ();
                var elapsed = global::System.Diagnostics.Stopwatch.StartNew();
                var operation = unityRequest.SendWebRequest();
                operation.completed += _ => completion.TrySetResult(true);
                if (operation.isDone)
                {
                    completion.TrySetResult(true);
                }

                // Abort() belongs to the main thread; the token may be cancelled on any thread. The gate keeps Abort() from
                // running while or after the request is disposed (without a context the callback runs on the cancelling thread).
                using (cancellationToken.Register(() =>
                {
                    global::System.Action abort = () =>
                    {
                        lock (finishedGate)
                        {
                            if (!finished)
                            {
                                unityRequest.Abort();
                            }
                        }
                    };
                    if (context != null)
                    {
                        context.Post(_ => abort(), null);
                    }
                    else
                    {
                        abort();
                    }
                }))
                {
                    await completion.Task;
                    lock (finishedGate)
                    {
                        finished = true;
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new global::System.OperationCanceledException(cancellationToken);
                }

                var unityResult = unityRequest.result;
                if (unityResult != global::UnityEngine.Networking.UnityWebRequest.Result.Success && unityResult != global::UnityEngine.Networking.UnityWebRequest.Result.ProtocolError)
                {
                    string unityError = unityRequest.error ?? unityResult.ToString();
                    if (timeoutSeconds > 0 && (unityError.IndexOf("timeout", global::System.StringComparison.OrdinalIgnoreCase) >= 0 || elapsed.Elapsed.TotalSeconds >= timeoutSeconds * 0.9))
                    {
                        throw new global::System.Threading.Tasks.TaskCanceledException("The request was canceled due to the configured RequestTimeout of " + timeoutSeconds + " seconds elapsing.", new global::System.TimeoutException(unityError));
                    }

                    throw new global::System.Net.Http.HttpRequestException("UnityWebRequest failed (" + unityResult + "): " + unityError);
                }

                var response = new global::System.Net.Http.HttpResponseMessage((global::System.Net.HttpStatusCode)(int)unityRequest.responseCode);
                try
                {
                    byte[]? responseData = unityRequest.downloadHandler?.data;
                    response.Content = new global::System.Net.Http.ByteArrayContent(responseData ?? new byte[0]);
                    response.RequestMessage = request;
                    var responseHeaders = unityRequest.GetResponseHeaders();
                    if (responseHeaders != null)
                    {
                        foreach (var header in responseHeaders)
                        {
                            AddUnityResponseHeader(response, header.Key, header.Value);
                        }
                    }

                    return response;
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            }
        }

        private static void SetUnityRequestHeader(global::UnityEngine.Networking.UnityWebRequest unityRequest, string name, global::System.Collections.Generic.IEnumerable<string> values)
        {
            string separator = string.Equals(name, "Cookie", global::System.StringComparison.OrdinalIgnoreCase) ? "; " : ", ";
            try
            {
                unityRequest.SetRequestHeader(name, string.Join(separator, values));
            }
            catch (global::System.Exception exception)when (exception is global::System.InvalidOperationException || exception is global::System.ArgumentException)
            {
                // Not swallowed: a dropped Authorization header would go unnoticed.
                throw new global::System.InvalidOperationException("The request header '" + name + "' cannot be sent with UnityWebRequest: " + exception.Message, exception);
            }
        }

        /// <summary>Adds a response header to the response or, for the Content-* family, to its content.</summary>
        private static void AddUnityResponseHeader(global::System.Net.Http.HttpResponseMessage response, string name, string value)
        {
            // A pseudo header that some platforms report for the status line, and the length of the (decoded) body.
            if (string.IsNullOrEmpty(name) || string.Equals(name, "Content-Length", global::System.StringComparison.OrdinalIgnoreCase) || (value != null && value.StartsWith("HTTP/", global::System.StringComparison.Ordinal)))
            {
                return;
            }

            bool isContentHeader = name.StartsWith("Content-", global::System.StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Allow", global::System.StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Expires", global::System.StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Last-Modified", global::System.StringComparison.OrdinalIgnoreCase);
            bool added = isContentHeader ? response.Content.Headers.TryAddWithoutValidation(name, value) : response.Headers.TryAddWithoutValidation(name, value);
            if (!added)
            {
                if (isContentHeader)
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }
                else
                {
                    response.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        private static object? __OacgBody_DeserializeError<T>(string json, out global::System.Exception? failure)
        {
            failure = null;
            if (global::System.String.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var defaultSettings = global::Newtonsoft.Json.JsonConvert.DefaultSettings?.Invoke();
                var serializer = global::Newtonsoft.Json.JsonSerializer.Create();
                serializer.DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None;
                serializer.MaxDepth = defaultSettings?.MaxDepth > 0 ? defaultSettings.MaxDepth : 64;
                using (var textReader = new global::System.IO.StringReader(json))
                using (var reader = new global::Newtonsoft.Json.JsonTextReader(textReader)
                {DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None, MaxDepth = serializer.MaxDepth})
                {
                    return serializer.Deserialize<T>(reader);
                }
            }
            catch (global::System.Exception exception)when (exception is global::Newtonsoft.Json.JsonException || exception is global::System.FormatException || exception is global::System.InvalidCastException || exception is global::System.OverflowException || exception is global::System.ArgumentException)
            {
                failure = exception;
                return null;
            }
        }

        private static void __OacgResponseValidator_0(global::System.Collections.Generic.List<Item>? value, string path, string contract, global::System.Collections.Generic.Dictionary<object, global::System.Collections.Generic.HashSet<int>> visited)
        {
            if (value is null)
            {
                return;
            }

            if (!visited.TryGetValue(value, out var validatedTypes))
            {
                validatedTypes = new global::System.Collections.Generic.HashSet<int>();
                visited.Add(value, validatedTypes);
            }

            if (!validatedTypes.Add(0))
            {
                return;
            }

            for (int index = 0; index < value.Count; index++)
            {
                var item = value[index];
                string itemPath = path + "[" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "]";
                if (item is null)
                {
                    throw new global::Newtonsoft.Json.JsonSerializationException("Non-nullable " + contract + " array element was null at " + itemPath + ".");
                }
            }
        }

        private sealed class __OacgResponseValidator_Comparer : global::System.Collections.Generic.IEqualityComparer<object>
        {
            public bool Equals(object? left, object? right) => global::System.Object.ReferenceEquals(left, right);
            public int GetHashCode(object value) => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }

        private static string __OacgJsonContract_Parse(string json, global::System.Action<global::Newtonsoft.Json.Linq.JToken, string, bool, string, global::System.Collections.Generic.List<int>, global::System.Collections.Generic.List<global::System.Tuple<int, int, string>>> validate, bool nullable, int? maxDepth)
        {
            __OacgJsonContract_ValidateSyntax(json, maxDepth);
            var lineStarts = new global::System.Collections.Generic.List<int>{0};
            for (int index = 0; index < json.Length; index++)
            {
                if (json[index] != '\r' && json[index] != '\n')
                    continue;
                if (json[index] == '\r' && index + 1 < json.Length && json[index + 1] == '\n')
                    index++;
                lineStarts.Add(index + 1);
            }

            var replacements = new global::System.Collections.Generic.List<global::System.Tuple<int, int, string>>();
            using (var textReader = new global::System.IO.StringReader(json))
            using (var reader = new global::Newtonsoft.Json.JsonTextReader(textReader)
            {DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None, MaxDepth = maxDepth})
            {
                var token = global::Newtonsoft.Json.Linq.JToken.ReadFrom(reader, new global::Newtonsoft.Json.Linq.JsonLoadSettings{DuplicatePropertyNameHandling = global::Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error});
                if (reader.Read())
                {
                    throw new global::Newtonsoft.Json.JsonSerializationException("The successful response has trailing JSON content.");
                }

                validate(token, "$", nullable, json, lineStarts, replacements);
            }

            if (replacements.Count == 0)
                return json;
            replacements.Sort((left, right) => left.Item1.CompareTo(right.Item1));
            var normalized = new global::System.Text.StringBuilder(json.Length);
            int copiedTo = 0;
            foreach (var replacement in replacements)
            {
                normalized.Append(json, copiedTo, replacement.Item1 - copiedTo);
                normalized.Append(replacement.Item3);
                copiedTo = replacement.Item2;
            }

            normalized.Append(json, copiedTo, json.Length - copiedTo);
            return normalized.ToString();
        }

        private static T? __OacgJsonContract_Deserialize<T>(string json, global::Newtonsoft.Json.JsonSerializer serializer)
        {
            using (var textReader = new global::System.IO.StringReader(json))
            using (var reader = new global::Newtonsoft.Json.JsonTextReader(textReader)
            {DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None, MaxDepth = serializer.MaxDepth})
            {
                return serializer.Deserialize<T>(reader);
            }
        }

        private static void __OacgJsonContract_0(global::Newtonsoft.Json.Linq.JToken token, string path, bool nullable, string json, global::System.Collections.Generic.List<int> lineStarts, global::System.Collections.Generic.List<global::System.Tuple<int, int, string>> replacements)
        {
            if (token.Type == global::Newtonsoft.Json.Linq.JTokenType.Null)
            {
                if (nullable)
                    return;
                throw new global::Newtonsoft.Json.JsonSerializationException("Non-nullable response value was null at " + path + ".");
            }

            if (token is global::Newtonsoft.Json.Linq.JObject wrapper)
            {
                if (wrapper.Property("$ref")is not null && wrapper.Count == 1)
                    return;
                foreach (var property in wrapper.Properties())
                {
                    if (property.Name != "$id" && property.Name != "$values")
                        throw new global::Newtonsoft.Json.JsonSerializationException("Undeclared response property at " + path + "." + property.Name + ".");
                }

                token = wrapper["$values"] ?? token;
            }

            if (!(token is global::Newtonsoft.Json.Linq.JArray array))
                throw new global::Newtonsoft.Json.JsonSerializationException("Expected a response array at " + path + ".");
            for (int index = 0; index < array.Count; index++)
            {
                __OacgJsonContract_1(array[index], path + "[" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "]", false, json, lineStarts, replacements);
            }
        }

        private static void __OacgJsonContract_1(global::Newtonsoft.Json.Linq.JToken token, string path, bool nullable, string json, global::System.Collections.Generic.List<int> lineStarts, global::System.Collections.Generic.List<global::System.Tuple<int, int, string>> replacements)
        {
            if (token.Type == global::Newtonsoft.Json.Linq.JTokenType.Null)
            {
                if (nullable)
                    return;
                throw new global::Newtonsoft.Json.JsonSerializationException("Non-nullable response value was null at " + path + ".");
            }

            if (!(token is global::Newtonsoft.Json.Linq.JObject objectToken))
                throw new global::Newtonsoft.Json.JsonSerializationException("Expected a response object at " + path + ".");
            if (objectToken.Property("$ref")is not null && objectToken.Count == 1)
                return;
            foreach (var property in objectToken.Properties())
            {
                switch (property.Name)
                {
                    case "id":
                        __OacgJsonContract_2(property.Value, path + "." + "id", false, json, lineStarts, replacements);
                        break;
                    case "label":
                        __OacgJsonContract_3(property.Value, path + "." + "label", true, json, lineStarts, replacements);
                        break;
                    default:
                        if (property.Name == "$id")
                            break;
                        throw new global::Newtonsoft.Json.JsonSerializationException("Undeclared response property at " + path + "." + property.Name + ".");
                }
            }

            if (objectToken.Property("id") == null)
                throw new global::Newtonsoft.Json.JsonSerializationException("Missing required response property at " + path + "." + "id" + ".");
        }

        private static void __OacgJsonContract_2(global::Newtonsoft.Json.Linq.JToken token, string path, bool nullable, string json, global::System.Collections.Generic.List<int> lineStarts, global::System.Collections.Generic.List<global::System.Tuple<int, int, string>> replacements)
        {
            if (token.Type == global::Newtonsoft.Json.Linq.JTokenType.Null)
            {
                if (nullable)
                    return;
                throw new global::Newtonsoft.Json.JsonSerializationException("Non-nullable response value was null at " + path + ".");
            }

            if (!__OacgJsonContract_IsIntegralNumber(token, json, lineStarts, true, replacements))
                throw new global::Newtonsoft.Json.JsonSerializationException("Unexpected response JSON token at " + path + ".");
        }

        private static void __OacgJsonContract_3(global::Newtonsoft.Json.Linq.JToken token, string path, bool nullable, string json, global::System.Collections.Generic.List<int> lineStarts, global::System.Collections.Generic.List<global::System.Tuple<int, int, string>> replacements)
        {
            if (token.Type == global::Newtonsoft.Json.Linq.JTokenType.Null)
            {
                if (nullable)
                    return;
                throw new global::Newtonsoft.Json.JsonSerializationException("Non-nullable response value was null at " + path + ".");
            }

            if (token.Type != global::Newtonsoft.Json.Linq.JTokenType.String)
                throw new global::Newtonsoft.Json.JsonSerializationException("Unexpected response JSON token at " + path + ".");
        }

        private static bool __OacgJsonContract_IsIntegralNumber(global::Newtonsoft.Json.Linq.JToken token, string json, global::System.Collections.Generic.List<int> lineStarts, bool int32, global::System.Collections.Generic.List<global::System.Tuple<int, int, string>> replacements)
        {
            if (token.Type == global::Newtonsoft.Json.Linq.JTokenType.Integer)
                return true;
            if (token.Type != global::Newtonsoft.Json.Linq.JTokenType.Float)
                return false;
            var location = (global::Newtonsoft.Json.IJsonLineInfo)token;
            if (!location.HasLineInfo())
                return false;
            if (location.LineNumber < 1 || location.LineNumber > lineStarts.Count)
                return false;
            int lineStart = lineStarts[location.LineNumber - 1];
            int end = lineStart + location.LinePosition;
            if (end > json.Length || end <= lineStart)
                return false;
            int start = end;
            while (start > lineStart)
            {
                char character = json[start - 1];
                if ((character >= '0' && character <= '9') || character == '-' || character == '+' || character == '.' || character == 'e' || character == 'E')
                    start--;
                else
                    break;
            }

            if (start == end)
                return false;
            string number = json.Substring(start, end - start);
            int exponentMarker = number.IndexOfAny(new[]{'e', 'E'});
            int mantissaEnd = exponentMarker < 0 ? number.Length : exponentMarker;
            int decimalPoint = number.IndexOf('.');
            int fractionalDigits = decimalPoint < 0 ? 0 : mantissaEnd - decimalPoint - 1;
            int trailingZeros = 0;
            bool hasNonzeroDigit = false;
            bool hasDigit = false;
            for (int index = mantissaEnd - 1; index >= 0; index--)
            {
                char character = number[index];
                if (character < '0' || character > '9')
                    continue;
                hasDigit = true;
                if (character == '0' && !hasNonzeroDigit)
                    trailingZeros++;
                else if (character != '0')
                    hasNonzeroDigit = true;
            }

            if (!hasDigit)
                return false;
            if (hasNonzeroDigit)
            {
                int exponent = 0;
                if (exponentMarker >= 0 && !global::System.Int32.TryParse(number.Substring(exponentMarker + 1), global::System.Globalization.NumberStyles.AllowLeadingSign, global::System.Globalization.CultureInfo.InvariantCulture, out exponent))
                {
                    if (number[exponentMarker + 1] == '-')
                        return false;
                }
                else if (exponent < fractionalDigits - trailingZeros)
                    return false;
            }

            if (!global::System.Decimal.TryParse(number, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var value))
                return false;
            if (int32 && (value < global::System.Int32.MinValue || value > global::System.Int32.MaxValue))
                return false;
            if (value < global::System.Int64.MinValue || value > global::System.Int64.MaxValue)
                return false;
            replacements.Add(global::System.Tuple.Create(start, end, value.ToString("0", global::System.Globalization.CultureInfo.InvariantCulture)));
            return true;
        }

        private static bool __OacgJsonContract_IsTrialFailure(global::System.Exception exception)
        {
            return exception is global::Newtonsoft.Json.JsonException || exception is global::System.FormatException || exception is global::System.InvalidCastException || exception is global::System.OverflowException || exception is global::System.ArgumentException;
        }

        private static bool __OacgJsonContract_AllDeclared(global::Newtonsoft.Json.Linq.JToken token, string[] names)
        {
            if (!(token is global::Newtonsoft.Json.Linq.JObject objectToken))
                return true;
            foreach (var property in objectToken.Properties())
            {
                if (global::System.Array.IndexOf(names, property.Name) < 0)
                    return false;
            }

            return true;
        }

        private static void __OacgJsonContract_ValidateSyntax(string json, int? maxDepth)
        {
            int index = 0;
            __OacgJsonContract_SkipWhitespace(json, ref index);
            __OacgJsonContract_ReadValue(json, ref index, 0, maxDepth);
            __OacgJsonContract_SkipWhitespace(json, ref index);
            if (index != json.Length)
                __OacgJsonContract_InvalidSyntax();
        }

        private static void __OacgJsonContract_ReadValue(string json, ref int index, int depth, int? maxDepth)
        {
            if (index >= json.Length || (maxDepth.HasValue && depth > maxDepth.Value))
                __OacgJsonContract_InvalidSyntax();
            char current = json[index];
            if (current == '{')
            {
                __OacgJsonContract_ReadObject(json, ref index, depth + 1, maxDepth);
                return;
            }

            if (current == '[')
            {
                __OacgJsonContract_ReadArray(json, ref index, depth + 1, maxDepth);
                return;
            }

            if (current == '"')
            {
                __OacgJsonContract_ReadString(json, ref index);
                return;
            }

            if (current == 't' && __OacgJsonContract_ReadLiteral(json, ref index, "true"))
                return;
            if (current == 'f' && __OacgJsonContract_ReadLiteral(json, ref index, "false"))
                return;
            if (current == 'n' && __OacgJsonContract_ReadLiteral(json, ref index, "null"))
                return;
            if (current == '-' || (current >= '0' && current <= '9'))
            {
                __OacgJsonContract_ReadNumber(json, ref index);
                return;
            }

            __OacgJsonContract_InvalidSyntax();
        }

        private static void __OacgJsonContract_ReadObject(string json, ref int index, int depth, int? maxDepth)
        {
            index++;
            __OacgJsonContract_SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == '}')
            {
                index++;
                return;
            }

            while (true)
            {
                if (index >= json.Length || json[index] != '"')
                    __OacgJsonContract_InvalidSyntax();
                __OacgJsonContract_ReadString(json, ref index);
                __OacgJsonContract_SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index++] != ':')
                    __OacgJsonContract_InvalidSyntax();
                __OacgJsonContract_SkipWhitespace(json, ref index);
                __OacgJsonContract_ReadValue(json, ref index, depth, maxDepth);
                __OacgJsonContract_SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == '}')
                {
                    index++;
                    return;
                }

                if (index >= json.Length || json[index++] != ',')
                    __OacgJsonContract_InvalidSyntax();
                __OacgJsonContract_SkipWhitespace(json, ref index);
            }
        }

        private static void __OacgJsonContract_ReadArray(string json, ref int index, int depth, int? maxDepth)
        {
            index++;
            __OacgJsonContract_SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ']')
            {
                index++;
                return;
            }

            while (true)
            {
                __OacgJsonContract_ReadValue(json, ref index, depth, maxDepth);
                __OacgJsonContract_SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ']')
                {
                    index++;
                    return;
                }

                if (index >= json.Length || json[index++] != ',')
                    __OacgJsonContract_InvalidSyntax();
                __OacgJsonContract_SkipWhitespace(json, ref index);
            }
        }

        private static void __OacgJsonContract_ReadString(string json, ref int index)
        {
            index++;
            while (index < json.Length)
            {
                char current = json[index++];
                if (current == '"')
                    return;
                if (current < 0x20)
                    __OacgJsonContract_InvalidSyntax();
                if (current != '\\')
                    continue;
                if (index >= json.Length)
                    __OacgJsonContract_InvalidSyntax();
                char escaped = json[index++];
                if (escaped == 'u')
                {
                    for (int digit = 0; digit < 4; digit++)
                    {
                        if (index >= json.Length)
                            __OacgJsonContract_InvalidSyntax();
                        char hex = json[index++];
                        if (!((hex >= '0' && hex <= '9') || (hex >= 'a' && hex <= 'f') || (hex >= 'A' && hex <= 'F')))
                            __OacgJsonContract_InvalidSyntax();
                    }
                }
                else if (escaped != '"' && escaped != '\\' && escaped != '/' && escaped != 'b' && escaped != 'f' && escaped != 'n' && escaped != 'r' && escaped != 't')
                    __OacgJsonContract_InvalidSyntax();
            }

            __OacgJsonContract_InvalidSyntax();
        }

        private static void __OacgJsonContract_ReadNumber(string json, ref int index)
        {
            if (json[index] == '-')
                index++;
            if (index >= json.Length)
                __OacgJsonContract_InvalidSyntax();
            if (json[index] == '0')
                index++;
            else
            {
                if (json[index] < '1' || json[index] > '9')
                    __OacgJsonContract_InvalidSyntax();
                do
                {
                    index++;
                }
                while (index < json.Length && json[index] >= '0' && json[index] <= '9');
            }

            if (index < json.Length && json[index] == '.')
            {
                index++;
                if (index >= json.Length || json[index] < '0' || json[index] > '9')
                    __OacgJsonContract_InvalidSyntax();
                do
                {
                    index++;
                }
                while (index < json.Length && json[index] >= '0' && json[index] <= '9');
            }

            if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
            {
                index++;
                if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                    index++;
                if (index >= json.Length || json[index] < '0' || json[index] > '9')
                    __OacgJsonContract_InvalidSyntax();
                do
                {
                    index++;
                }
                while (index < json.Length && json[index] >= '0' && json[index] <= '9');
            }
        }

        private static bool __OacgJsonContract_ReadLiteral(string json, ref int index, string literal)
        {
            if (index + literal.Length > json.Length || global::System.String.CompareOrdinal(json, index, literal, 0, literal.Length) != 0)
                return false;
            index += literal.Length;
            return true;
        }

        private static void __OacgJsonContract_SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && (json[index] == ' ' || json[index] == '\t' || json[index] == '\r' || json[index] == '\n'))
                index++;
        }

        private static void __OacgJsonContract_InvalidSyntax()
        {
            throw new global::Newtonsoft.Json.JsonSerializationException("The successful response is not valid JSON.");
        }
    }

    public sealed class UwrApiException : global::System.Exception
    {
        public UwrApiException(global::System.Net.HttpStatusCode statusCode, string responseBody) : this(statusCode, responseBody, null, null)
        {
        }

        /// <summary>Creates the exception with the declared error body of the response, when there is one.</summary>
        public UwrApiException(global::System.Net.HttpStatusCode statusCode, string responseBody, object? error, global::System.Exception? errorDeserializationException) : base("The API returned HTTP status " + ((int)statusCode).ToString(global::System.Globalization.CultureInfo.InvariantCulture) + " (" + statusCode + ").")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody ?? string.Empty;
            Error = error;
            ErrorDeserializationException = errorDeserializationException;
        }

        public global::System.Net.HttpStatusCode StatusCode { get; }

        public string ResponseBody { get; }

        /// <summary>The error body deserialized to the type the operation declares for this status (exact code, then 4XX / 5XX range, then default); null when none is declared or it could not be read.</summary>
        public object? Error { get; }

        /// <summary>Why the declared error body could not be deserialized; null when it was or none is declared. ResponseBody always keeps the raw text.</summary>
        public global::System.Exception? ErrorDeserializationException { get; }

        /// <summary>Gets the declared error body as <typeparamref name = "T"/>.</summary>
        public bool TryGetError<T>(out T value)
        {
            if (Error is T typed)
            {
                value = typed;
                return true;
            }

            value = default !;
            return false;
        }
    }
}
