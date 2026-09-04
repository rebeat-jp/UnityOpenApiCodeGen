# Normalized Spec Bundle v2

## Purpose and audience

This document defines the compiler input exchanged between the Editor-side
Source Generator provider and the Roslyn analyzer. It is for maintainers who
change the normalizer, cache publication, or analyzer reader. The Bundle is an
internal deterministic format; users normally encounter it through the
`AdditionalFile` generated for one client definition.

The boundary is deliberate:

- The Editor performs network access, filesystem access, raw JSON/YAML parsing,
  reference resolution, and Bundle publication.
- The analyzer reads only the Bundle AdditionalFile. It does not fetch URLs or
  read external files.
- One `specId` produces one multi-document Bundle v2 and one AdditionalFile.

## Envelope

Bundle v2 is strict UTF-8 JSON without a BOM, with LF line endings, two-space
indentation, fixed property order, no unknown or duplicate properties, and no
trailing commas. The top-level properties appear exactly in this order:

```text
formatVersion, specId, rawSha256, rootDocumentId, documents, referenceEdges
```

The envelope has the following shape:

```json
{
  "formatVersion": 2,
  "specId": "<32 lower-case hexadecimal characters>",
  "rawSha256": "<64 lower-case hexadecimal characters>",
  "rootDocumentId": "root",
  "documents": [],
  "referenceEdges": []
}
```

`rawSha256` is the raw-byte SHA-256 of the root document and must match the
root document entry. `specId` remains the stable client-definition identity;
it is not a hash of the generated C# source.

## Documents

Each `documents` entry has properties in this order:

```text
documentId, sourcePath, format, rawSha256, root
```

The values are:

| Field | Rule |
| --- | --- |
| `documentId` | `root` for the root document; local external documents use their canonical project-relative path; remote external documents use `doc-` followed by 64 lower-case hexadecimal characters. |
| `sourcePath` | The root may use a normalized project-relative path, absolute path, or HTTP(S) URI. Local external documents are project-relative; remote display URIs are redacted. All path separators are `/`. |
| `format` | Exactly `json` or `yaml`. |
| `rawSha256` | SHA-256 of that document's raw bytes, lower-case hexadecimal. |
| `root` | The existing normalized `SpecNode` tree, including source line/column data and sanitized `$ref` values. |

The root document is first. External documents are sorted by `documentId` in
ordinal order. There are at most 64 documents. A local external document ID
is its project-relative path after path and symbolic-link checks. Remote identity uses
the canonical final fetch key, retaining query for fetch identity while
excluding fragments; the persisted display path does not expose the query.

The `SpecNode` representation is the same normalized node shape used by Bundle
v1. Changes to node field order or node validation are format changes and must
be reflected in both writer and reader tests.

## Reference edges

Each `referenceEdges` entry has properties in this order:

```text
sourceDocumentId, sourcePointer, targetDocumentId, targetPointer
```

An edge maps the source document's string `$ref` node at `sourcePointer` to the
target document node at `targetPointer`. Pointers are RFC 6901 JSON Pointers;
`sourcePointer` must identify a `$ref` string and `targetPointer` may be empty
to identify the target document root. Every `$ref` in every document must have
exactly one edge. Both document IDs must be declared in `documents`, and both
pointer targets must exist.

Edges are sorted by `sourceDocumentId`, then `sourcePointer` in ordinal order.
Duplicate source identities, missing edges, invalid pointers, missing target
documents, and unresolved targets are invalid Bundle structure. The analyzer
uses the pair `(documentId, JSON Pointer)` as node identity for semantic
resolution and cycle detection.

## Editor graph and URL policy

The Editor resolves relative references against the referring document's final
effective URI. It deduplicates a fetch key once, excludes fragments from fetch
identity, and retains query in remote identity. Root URL fragments, userinfo,
whitespace, non-HTTP(S) schemes, explicit `file:` references, remote-to-local
references, and HTTPS-to-HTTP direct or redirected transitions are rejected.
Local external files must remain within the Unity project after real-path and
symbolic-link checks.

Remote format is determined from the final URL extension and response
`Content-Type`: recognized values must agree, one recognized value is
sufficient, and neither recognized value is an error. Content sniffing and a
caller-supplied format hint are not used.

The hard limits are 30 seconds per request, 120 seconds per graph, 4 MiB per
document, 32 MiB per graph, 64 documents, five redirects, and reference depth
256. Automatic retry and ETag revalidation are not performed.

The supported external graph includes bare Schema documents. Bare non-Schema
documents and `$id`, `$anchor`, `$dynamicAnchor`, and `$dynamicRef` are
unsupported. Unresolved targets report `OACG102`; cycles report `OACG103`.
Bundle v1 external-reference input retains `OACG104` for compatibility.

## Publication and manifest

The Editor stages the complete graph before publication. It publishes bundle,
manifest, compiler mirror, and generated definition in that order under one
recoverable transaction. Any publication failure restores the previous bytes;
script compilation is requested only after the complete marker is committed.

```text
Library/OpenApiCodeGen/SourceGenerator/SpecCache/<specId>/normalized-v2.json
Library/OpenApiCodeGen/SourceGenerator/SpecCache/<specId>/manifest-v1.json
Assets/OpenApiCodeGen/Generated/SpecCache/
  <specId>.Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile
```

`manifest-v1.json` is Editor metadata, not analyzer input. It records the
bundle format/version, spec ID, bundle path and hash, then per-document
redacted source metadata: source display, source-key hash, format, raw hash,
retrieval kind, HTTP status, and redirect count. Query strings, userinfo,
response headers, and raw remote bodies are not persisted. If the input query
was stripped from persisted display data, Generate reports a warning asking
the user to re-enter the complete URL.

Fetch, parse, reference, or publication failure keeps the last successful cache
and mirror intact but returns generation failure; the failed graph is not
published. A byte-identical Bundle and definition do not request script
compilation.

## Backward compatibility

The analyzer reader accepts Bundle v1 envelopes containing exactly one root
document and no reference edges. Bundle v1 uses the existing single-document
schema and remains useful for existing generated AdditionalFiles. The Editor
always writes Bundle v2 once a new Generate succeeds. An unsupported bundle
format is reported as a format diagnostic rather than interpreted heuristically.
