using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Runtime の OpenApiHttpTransport に対応する値。値は Runtime の enum と一致させる。
    /// Mirrors the Runtime OpenApiHttpTransport values.
    /// </summary>
    internal enum HttpTransportKind
    {
        HttpClient = 0,
        UnityWebRequest = 1
    }

    /// <summary>
    /// Runtime の OpenApiJsonSerializer に対応する値。値は Runtime の enum と一致させる。
    /// Mirrors the Runtime OpenApiJsonSerializer values.
    /// </summary>
    internal enum JsonSerializerKind
    {
        NewtonsoftJson = 0,
        SystemTextJson = 1
    }

    internal enum LibrarySelectionIssueKind
    {
        NotImplemented,
        JsonLibraryMissing,
        HttpImplementationMissing,
        VersionTooLow,
        WebGlHttpClient
    }

    /// <summary>
    /// 選択された組み合わせに対する 1 件の問題。WebGlHttpClient のみ警告で、他は生成を止める。
    /// One problem found for a selection. Only WebGlHttpClient is a warning; the rest block generation.
    /// </summary>
    internal sealed class LibrarySelectionIssue
    {
        internal LibrarySelectionIssue(LibrarySelectionIssueKind kind, params string[] arguments)
        {
            Kind = kind;
            Arguments = arguments;
        }

        internal LibrarySelectionIssueKind Kind { get; }

        internal string[] Arguments { get; }

        internal bool IsBlocking => Kind != LibrarySelectionIssueKind.WebGlHttpClient;
    }

    internal static class LibrarySelection
    {
        internal static bool IsDefined(HttpTransportKind transport)
        {
            return transport == HttpTransportKind.HttpClient ||
                   transport == HttpTransportKind.UnityWebRequest;
        }

        internal static bool IsDefined(JsonSerializerKind serializer)
        {
            return serializer == JsonSerializerKind.NewtonsoftJson ||
                   serializer == JsonSerializerKind.SystemTextJson;
        }

        /// <summary>
        /// 実装済みの組み合わせか。後続フェーズで STJ / UnityWebRequest を実装したらここを広げる。
        /// Whether an emitter exists for the combination. Widen this as STJ / UnityWebRequest land.
        /// </summary>
        internal static bool IsImplemented(HttpTransportKind transport, JsonSerializerKind serializer)
        {
            return transport == HttpTransportKind.HttpClient &&
                   serializer == JsonSerializerKind.NewtonsoftJson;
        }

        /// <summary>
        /// 組み合わせ全体に対してライブラリの不足・版・WebGL を判定する。未実装の組み合わせでも判定する。
        /// Checks libraries, versions and WebGL for the whole combination, even when not implemented.
        /// </summary>
        internal static IReadOnlyList<LibrarySelectionIssue> Evaluate(
            HttpTransportKind transport,
            JsonSerializerKind serializer,
            LibraryAvailability availability)
        {
            var issues = new List<LibrarySelectionIssue>();
            if (!IsImplemented(transport, serializer))
            {
                issues.Add(new LibrarySelectionIssue(
                    LibrarySelectionIssueKind.NotImplemented,
                    "the combination HttpTransport=" + transport +
                    ", JsonSerializer=" + serializer + " is not implemented"));
            }

            switch (serializer)
            {
                case JsonSerializerKind.NewtonsoftJson:
                    AddJsonIssues(
                        issues,
                        serializer,
                        "Newtonsoft.Json",
                        LibraryAvailability.NewtonsoftJsonMarkerType,
                        availability.NewtonsoftJsonVersion,
                        LibraryAvailability.MinimumNewtonsoftJsonVersion);
                    break;
                case JsonSerializerKind.SystemTextJson:
                    AddJsonIssues(
                        issues,
                        serializer,
                        "System.Text.Json",
                        LibraryAvailability.SystemTextJsonMarkerType,
                        availability.SystemTextJsonVersion,
                        LibraryAvailability.MinimumSystemTextJsonVersion);
                    break;
            }

            switch (transport)
            {
                case HttpTransportKind.HttpClient:
                    if (!availability.HasHttpClient)
                    {
                        issues.Add(new LibrarySelectionIssue(
                            LibrarySelectionIssueKind.HttpImplementationMissing,
                            transport.ToString(),
                            LibraryAvailability.HttpClientMarkerType));
                    }

                    if (availability.IsWebGl)
                    {
                        issues.Add(new LibrarySelectionIssue(LibrarySelectionIssueKind.WebGlHttpClient));
                    }

                    break;
                case HttpTransportKind.UnityWebRequest:
                    if (!availability.HasUnityWebRequest)
                    {
                        issues.Add(new LibrarySelectionIssue(
                            LibrarySelectionIssueKind.HttpImplementationMissing,
                            transport.ToString(),
                            LibraryAvailability.UnityWebRequestMarkerType));
                    }

                    break;
            }

            return issues;
        }

        internal static bool HasBlockingIssue(IReadOnlyList<LibrarySelectionIssue> issues)
        {
            for (int index = 0; index < issues.Count; index++)
            {
                if (issues[index].IsBlocking)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddJsonIssues(
            List<LibrarySelectionIssue> issues,
            JsonSerializerKind serializer,
            string libraryName,
            string markerType,
            Version? actual,
            Version minimum)
        {
            if (actual == null)
            {
                issues.Add(new LibrarySelectionIssue(
                    LibrarySelectionIssueKind.JsonLibraryMissing,
                    serializer.ToString(),
                    libraryName,
                    markerType));
                return;
            }

            if (actual < minimum)
            {
                issues.Add(new LibrarySelectionIssue(
                    LibrarySelectionIssueKind.VersionTooLow,
                    serializer.ToString(),
                    libraryName,
                    minimum.Major + "." + minimum.Minor,
                    actual.ToString()));
            }
        }
    }
}
