using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class RawJsonNormalizer
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

            string source = DecodeRawJson(rawBytes, sourcePath);
            SpecNode root = StrictJsonSpecParser.Parse(source, sourcePath);
            string rawSha256 = ComputeSha256(rawBytes);
            byte[] bytes = CanonicalSpecBundleWriter.Write(specId, rawSha256, sourcePath, root);
            return new NormalizedSpecBundle(specId, rawSha256, sourcePath, bytes);
        }

        private static string DecodeRawJson(byte[] rawBytes, string sourcePath)
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

    internal sealed class NormalizedSpecBundle
    {
        internal NormalizedSpecBundle(string specId, string rawSha256, string sourcePath, byte[] bytes)
        {
            SpecId = specId;
            RawSha256 = rawSha256;
            SourcePath = sourcePath;
            Bytes = bytes;
        }

        internal string SpecId { get; }

        internal string RawSha256 { get; }

        internal string SourcePath { get; }

        internal byte[] Bytes { get; }
    }
}
