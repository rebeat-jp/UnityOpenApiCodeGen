# Release and rollback procedure

## Purpose and audience

This runbook is for the maintainer preparing the `0.5.0` UPM release
candidate. It separates automated .NET/package evidence from the Unity Editor
manual gate. The repository scripts do not create Git tags, publish to a
registry, or create a GitHub release.

## Release inputs

Confirm the working tree contains the intended implementation and documentation
changes, then verify these package contracts:

- `jp.rhycol.openapicodegen` version `0.5.0`, minimum Unity `2021.3`.
- `jp.rhycol.openapicodegen.source-generator` version `0.5.0`, minimum Unity
  `6000.0`.
- Source Generator dependency on base package `0.5.0` and
  `com.unity.nuget.newtonsoft-json` `3.2.2`.
- Both packages are installed as separate project-manifest Git dependencies:

```text
https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.0
https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.0
```

Do not assume that a tag or registry package exists until a maintainer performs
that external release action.

## Automated candidate build

Run from the repository root:

```sh
SourceGenerators/scripts/verify.sh
SourceGenerators/scripts/pack-release.sh
```

`verify.sh` checks production tests, reproducible analyzer output, analyzer
sync, and UPM package inspection. `pack-release.sh` then creates two identical
packs for each package, verifies their archive roots and dependency DLL policy,
and writes:

```text
artifacts/upm/0.5.0/
  jp.rhycol.openapicodegen-0.5.0.tgz
  jp.rhycol.openapicodegen.source-generator-0.5.0.tgz
  release-manifest.json
  SHA256SUMS
```

The manifest records schema version and, for each archive, its package,
version, `path`, file, root, `size`, and SHA-256. It also records the packaged
analyzer SHA-256 and the manual Unity gate. It initially
contains `"unityGate": { "mode": "manual", "status": "not-run" }`. A failed
command is a release blocker; do not hand off the generated archives.

## Manual Unity gate

The Unity gate is deliberately manual and is not part of GitHub Actions. Run:

```sh
SourceGenerators/scripts/verify-unity-matrix.sh
```

Use Unity `2021.3.19f1` for the base package and Unity `6000.0.23f1` plus
`6000.3.2f1` for the Source Generator add-on. Install the candidate tarballs
into temporary projects and verify:

1. clean base-package compile without the add-on;
2. local JSON and YAML generation;
3. URL generation against the deterministic loopback fixture;
4. local JSON with the external bare YAML Schema fixture;
5. regeneration with unchanged input and no unnecessary compilation;
6. changed input updates only the related generated client;
7. add-on removal removes the provider define before recompilation;
8. base-only project remains usable after add-on removal; and
9. no `CS8785` or unexpected analyzer/package errors.

Save version-specific logs, result XML, and `unity-gate.json` as evidence. If
the Editor or Test Runner does not produce usable evidence, record that version
as `not-run`; a failed or missing version keeps the release candidate
non-releasable.

Current evidence is:

- Source Generator .NET tests: **101/101**.
- Analyzer reproducibility, analyzer synchronization, package inspection,
  tarball reproducibility, and checksum verification: **pass**.
- Base Unity `2021.3.19f1` tarball/EditMode gate: **36/36**.
- Unity `6000.0.23f1`: initial **185/185**, regeneration **185/185**, and
  without-add-on **36/36**; the full Source Generator flow passed.
- Unity `6000.3.2f1`: initial **185/185**, regeneration **185/185**, and
  without-add-on **36/36**; the full Source Generator flow passed.
- Aggregate manual evidence in `artifacts/upm/0.5.0/unity-gate.json`:
  **passed**.

The manual gate therefore passed, but the release boundary remains open: the
Unity GitHub Actions job required by #54 is intentionally not implemented.
Consequently #54 and Phase 7 are not complete and this candidate is not
release-ready until that decision changes.

## Provenance before publication

`release-manifest.json` records the repository `HEAD` used to create the
candidate archives. The current implementation worktree is dirty, so that
commit value is not sufficient provenance for publication. Before publishing,
commit the intended changes to a clean tree, rerun `verify.sh`,
`pack-release.sh`, and the full Unity matrix, then review the regenerated
manifest and `SHA256SUMS`. Do not publish archives or a tag from a dirty-tree
candidate.

## Handoff and publication

Only after maintainers resolve or explicitly waive the open #54 release
criterion and all manual evidence is `pass`, review `SHA256SUMS` and the commit
recorded in `release-manifest.json`. A maintainer may then create the immutable
`0.5.0` tag and publish the release through the chosen external process. Do not
move or delete an existing tag. The repository's packaging script itself
performs no tagging, registry publication, or release upload.

## Rollback

If the Source Generator add-on must be withdrawn:

1. Remove `jp.rhycol.openapicodegen.source-generator` from the project
   manifest and allow Unity to remove its provider define.
2. Keep the base package and select the Docker provider in Settings.
3. Pin the base package to `0.4.0` if the base `0.5.0` contract is also
   implicated.
4. Regenerate affected clients with the Docker provider and record the
   project/package changes.

Never move or delete the published `0.5.0` tag. Corrective changes use a new
`0.5.1` fix-forward release after the same automated and manual gates.
