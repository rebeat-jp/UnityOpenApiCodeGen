#nullable enable

using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.Editor.Generation
{
    /// <summary>
    /// Decides whether the user's GitHub token may be attached to an outgoing OpenAPI document
    /// request. The token is sent only to an exact allowed origin (scheme, host and port), and
    /// callers must evaluate every request, including each redirect hop, independently.
    /// </summary>
    internal sealed class GitHubAuthorizationPolicy
    {
        internal const string AuthorizationScheme = "Bearer";

        internal static GitHubAuthorizationPolicy Default { get; } =
            new GitHubAuthorizationPolicy(new[] { new Uri("https://raw.githubusercontent.com/") });

        readonly List<Uri> _allowedOrigins;

        internal GitHubAuthorizationPolicy(IEnumerable<Uri> allowedOrigins)
        {
            if (allowedOrigins == null)
            {
                throw new ArgumentNullException(nameof(allowedOrigins));
            }

            _allowedOrigins = new List<Uri>();
            foreach (Uri origin in allowedOrigins)
            {
                if (origin == null || !origin.IsAbsoluteUri)
                {
                    throw new ArgumentException("Allowed origins must be absolute URIs.", nameof(allowedOrigins));
                }

                _allowedOrigins.Add(origin);
            }
        }

        /// <summary>
        /// Returns true when <paramref name="requestUri"/> targets an allowed origin.
        /// </summary>
        internal bool IsAllowedOrigin(Uri? requestUri)
        {
            if (requestUri == null ||
                !requestUri.IsAbsoluteUri ||
                !string.IsNullOrEmpty(requestUri.UserInfo))
            {
                return false;
            }

            foreach (Uri origin in _allowedOrigins)
            {
                if (string.Equals(origin.Scheme, requestUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(origin.IdnHost, requestUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                    origin.Port == requestUri.Port)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the normalized token to send to <paramref name="requestUri"/>, or false when
        /// no token is configured, the token is malformed, or the origin is not allowed.
        /// </summary>
        internal bool TryGetToken(Uri? requestUri, string? configuredToken, out string token)
        {
            token = string.Empty;
            string? normalized = NormalizeToken(configuredToken);
            if (normalized == null || !IsAllowedOrigin(requestUri))
            {
                return false;
            }

            token = normalized;
            return true;
        }

        /// <summary>
        /// Trims the configured token and rejects values that cannot be sent safely as a single
        /// HTTP header token (empty, whitespace, control or non-ASCII characters).
        /// </summary>
        internal static string? NormalizeToken(string? configuredToken)
        {
            if (configuredToken == null)
            {
                return null;
            }

            string trimmed = configuredToken.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            foreach (char character in trimmed)
            {
                if (character <= ' ' || character >= '\u007f')
                {
                    return null;
                }
            }

            return trimmed;
        }
    }
}
