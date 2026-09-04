using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    /// <summary>
    /// Decodes UTF-8 YAML and lowers it directly to the shared SpecNode tree used by JSON.
    /// </summary>
    internal sealed class RawYamlNormalizer
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal NormalizedSpecBundle Normalize(byte[] rawBytes, string specId, string sourcePath)
        {
            if (rawBytes == null)
            {
                throw new ArgumentNullException(nameof(rawBytes));
            }

            ValidateSpecId(specId);
            if (string.IsNullOrEmpty(sourcePath))
            {
                throw new ArgumentException("A source identity is required.", nameof(sourcePath));
            }

            string source = DecodeRawYaml(rawBytes, sourcePath);
            SpecNode root;
            try
            {
                root = YamlParser.Parse(source, sourcePath);
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

            string rawSha256 = ComputeSha256(rawBytes);
            byte[] bytes = CanonicalSpecBundleWriter.Write(specId, rawSha256, sourcePath, root);
            return new NormalizedSpecBundle(specId, rawSha256, sourcePath, bytes);
        }

        private static string DecodeRawYaml(byte[] rawBytes, string sourcePath)
        {
            try
            {
                string source = StrictUtf8.GetString(rawBytes);
                if (source.Length > 0 && source[0] == '\uFEFF')
                {
                    source = source.Substring(1);
                }

                return source;
            }
            catch (DecoderFallbackException exception)
            {
                throw new NormalizedSpecException(
                    "The raw document is not valid UTF-8.",
                    sourcePath,
                    1,
                    1,
                    string.Empty,
                    YamlDiagnosticCodes.ToCode(YamlDiagnosticCode.LexicalError),
                    exception);
            }
        }

        private static void ValidateSpecId(string specId)
        {
            Guid parsed;
            if (specId == null ||
                !Guid.TryParseExact(specId, "N", out parsed) ||
                !string.Equals(parsed.ToString("N", CultureInfo.InvariantCulture), specId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "specId must be a lower-case Guid in N format.",
                    nameof(specId));
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            byte[] hash;
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(bytes);
            }

            var builder = new StringBuilder(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
