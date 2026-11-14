using System;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// コンパイルが参照しているライブラリの状況。インクリメンタルキャッシュのため等値比較可能。
    /// The libraries referenced by the compilation. Value-equatable so the incremental cache applies.
    /// </summary>
    internal readonly struct LibraryAvailability : IEquatable<LibraryAvailability>
    {
        internal const string NewtonsoftJsonMarkerType = "Newtonsoft.Json.JsonConvert";
        internal const string SystemTextJsonMarkerType = "System.Text.Json.JsonSerializer";
        internal const string UnityWebRequestMarkerType = "UnityEngine.Networking.UnityWebRequest";
        internal const string HttpClientMarkerType = "System.Net.Http.HttpClient";
        internal const string WebGlSymbol = "UNITY_WEBGL";

        internal static readonly Version MinimumNewtonsoftJsonVersion = new Version(13, 0, 0, 0);
        internal static readonly Version MinimumSystemTextJsonVersion = new Version(8, 0, 0, 0);

        internal LibraryAvailability(
            Version? newtonsoftJsonVersion,
            Version? systemTextJsonVersion,
            bool hasUnityWebRequest,
            bool hasHttpClient,
            bool isWebGl)
        {
            NewtonsoftJsonVersion = newtonsoftJsonVersion;
            SystemTextJsonVersion = systemTextJsonVersion;
            HasUnityWebRequest = hasUnityWebRequest;
            HasHttpClient = hasHttpClient;
            IsWebGl = isWebGl;
        }

        /// <summary>Referenced assembly version of the Newtonsoft.Json marker type, or null when absent.</summary>
        internal Version? NewtonsoftJsonVersion { get; }

        /// <summary>Referenced assembly version of the System.Text.Json marker type, or null when absent.</summary>
        internal Version? SystemTextJsonVersion { get; }

        internal bool HasUnityWebRequest { get; }

        internal bool HasHttpClient { get; }

        internal bool IsWebGl { get; }

        internal static LibraryAvailability Create(Compilation compilation, ParseOptions parseOptions)
        {
            bool isWebGl = false;
            if (parseOptions is CSharpParseOptions csharpOptions)
            {
                foreach (string symbol in csharpOptions.PreprocessorSymbolNames)
                {
                    if (string.Equals(symbol, WebGlSymbol, StringComparison.Ordinal))
                    {
                        isWebGl = true;
                        break;
                    }
                }
            }

            return new LibraryAvailability(
                FindPublicTypeAssemblyVersion(compilation, NewtonsoftJsonMarkerType),
                FindPublicTypeAssemblyVersion(compilation, SystemTextJsonMarkerType),
                FindPublicTypeAssemblyVersion(compilation, UnityWebRequestMarkerType) != null,
                FindPublicTypeAssemblyVersion(compilation, HttpClientMarkerType) != null,
                isWebGl);
        }

        public bool Equals(LibraryAvailability other)
        {
            return Equals(NewtonsoftJsonVersion, other.NewtonsoftJsonVersion) &&
                   Equals(SystemTextJsonVersion, other.SystemTextJsonVersion) &&
                   HasUnityWebRequest == other.HasUnityWebRequest &&
                   HasHttpClient == other.HasHttpClient &&
                   IsWebGl == other.IsWebGl;
        }

        public override bool Equals(object? obj)
        {
            return obj is LibraryAvailability other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = NewtonsoftJsonVersion?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ (SystemTextJsonVersion?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (HasUnityWebRequest ? 1 : 0);
                hashCode = (hashCode * 397) ^ (HasHttpClient ? 1 : 0);
                hashCode = (hashCode * 397) ^ (IsWebGl ? 1 : 0);
                return hashCode;
            }
        }

        /// <summary>
        /// 同名の public 型を持つ参照が複数ある場合（例: Unity パッケージと NuGetForUnity の DLL 併存）は、
        /// 最大の版を採用する。
        /// When several references define the public type (e.g. a Unity package plus a NuGetForUnity DLL),
        /// the highest assembly version wins.
        /// </summary>
        private static Version? FindPublicTypeAssemblyVersion(Compilation compilation, string metadataName)
        {
            Version? highest = null;
            foreach (INamedTypeSymbol type in compilation.GetTypesByMetadataName(metadataName))
            {
                if (type.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                Version version = type.ContainingAssembly.Identity.Version;
                if (highest == null || version > highest)
                {
                    highest = version;
                }
            }

            return highest;
        }
    }
}
