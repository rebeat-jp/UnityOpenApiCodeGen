# Release and rollback procedure

Source Generator generation is in beta. Review the supported features before use.
Keep **Source Generator (Beta)** in the provider UI and **OpenApiCodeGen Source Generator (Beta)**
in Package Manager and release descriptions. The beta status applies to Source Generator generation;
package IDs and the shared version remain unchanged. See the
[Support Matrix](SourceGenerators/OpenApiMvpSupportMatrix.md).

## Distribution and prerequisites

This runbook is for maintainers updating the analyzer DLL and releasing the two
UPM packages. Publication starts manually and defaults to **dry-run**. Both
packages use the same stable version and tag, initially `0.5.0`.

| Package | Distribution |
| --- | --- |
| `jp.rhycol.openapicodegen` | Existing OpenUPM Git-tag tracking |
| `jp.rhycol.openapicodegen.source-generator` | Verified GitHub Release `.tgz` asset, distributed by OpenUPM |

The analyzer DLL remains committed in the add-on for Git URL installation.
Examples target the planned `0.5.0` tag; before it exists, replace that ref with
the reviewed commit SHA in both Git dependencies. Candidate verification does
not establish that a version is published.

| Workflow | Entry point | Result |
| --- | --- | --- |
| `source-generator-ci.yml` | PR, main/develop push, manual SHA | .NET/package checks, Unity evidence, CD dry-run |
| `source-generator-verify.yml` | Reused by CI and release | One candidate shared by all Unity versions |
| `source-generator-update-dll.yml` | Manual, workflow ref `main` | DLL-only PR against `develop` or `main` |
| `source-generator-release.yml` | Manual commit/version/publish | Dry-run, or immutable tag and Release |
| `source-generator-openupm.yml` | Explicit dispatch at release **tag** | Base publication, add-on publication, registry byte comparison |

Manual workflows become available after their definitions reach the default
branch. Before that merge, a same-repository PR exercises the shared pipeline
and the actual `publish-release.js` script with `PUBLISH=false`.

Unity runs on GitHub-hosted Linux in GameCI containers. The three Linux/amd64
image digests are pinned in
[`unity-images.json`](SourceGenerators/BuildTools/CI/unity-images.json).
The Unity job uses Environment **`UNITY_LICENSE`**, containing Secrets
`UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD`. Reusable-workflow callers
retain `secrets: inherit` so these Environment secrets resolve in the called
job; a host check stops before image downloads if any are unavailable.
The repository Environment requires approval by **HIROHIRO1224**. Self-review
is allowed, and administrator bypass is disabled. Before approval, inspect the
exact candidate commit linked by the Environment URL and candidate job summary.
This applies to same-repository PRs and manual dry-runs as well as publication;
repository write access alone does not grant approval.
The Personal ULF supplies
the serial through the GameCI activation procedure. Activation and license
return logs stay in the disposable container. Test logs/XML are redacted before
being written to the artifact directory.

All three editors run serially in one job. A repository-wide concurrency group
also prevents overlapping Unity jobs across CI and release workflows. Fork PRs
run .NET/package checks without the Unity job or its Secrets.

## Update the committed DLL

After source changes reach the selected base branch, start:

```sh
gh workflow run source-generator-update-dll.yml --ref main \
  -f base_branch=develop
```

The accepted bases are `develop` (default) and `main`. A read-only build job
runs `sync-analyzer.sh` and verification. A separate job uses the trusted
workflow controller to commit only the DLL, open a PR, and explicitly dispatch
CI for its exact SHA. Existing `.meta`/GUID and package versions are preserved.
An unchanged DLL produces no PR. Review and merge the PR through the normal
repository process.

The explicit dispatch is required because a PR created with `GITHUB_TOKEN`
does not automatically trigger the ordinary PR workflow.

## Candidate contract and evidence

The current package contract is:

- Base `0.5.0`, minimum Unity `2021.3`.
- Add-on `0.5.0`, minimum Unity `6000.0`.
- Add-on dependency on base `0.5.0` and
  `com.unity.nuget.newtonsoft-json` `3.2.2`.
- Package versions, dependency version, requested version, and tag match.

The shared pipeline checks out the exact clean SHA, runs script and .NET tests,
checks deterministic DLL builds and committed DLL synchronization, and rejects
forbidden bundled dependencies. It then packs one candidate. The second pack
inside `pack-release.sh` checks byte reproducibility; every editor receives
the same candidate bytes.

| Unity | Required verification |
| --- | --- |
| `2021.3.19f1` | Base-only tarball compilation and EditMode/Docker compatibility tests |
| `6000.0.23f1` | Add-on tests, JSON/YAML/URL/external refs, unchanged/changed input, regeneration, removal and base-only tests |
| `6000.3.2f1` | Same add-on and removal checks |

Tests also cover cancellation, safe errors, cache exclusion and publication
recovery. Missing editors, activation failures, missing logs/XML, failed tests,
nonzero exits, and mismatched provenance cannot pass the release gate. Unity
jobs cannot silently repack a missing candidate.

The final `verified-release-<run-id>-<aggregate-execution-attempt>` artifact is
uploaded only after successful aggregation and dry-run validation. It contains:

```text
jp.rhycol.openapicodegen-0.5.0.tgz
jp.rhycol.openapicodegen.source-generator-0.5.0.tgz
release-manifest.json
SHA256SUMS
unity-gate.json
unity-gate/<editor-version>/gate.json, logs, XML, fixture evidence
verification-evidence.tar.gz
```

The schema-2 manifest records clean commit, tool versions, candidate CI
run/attempt, archive and analyzer hashes, and the aggregate Unity gate. Each
editor's evidence binds that same candidate identity and adds
`ci.executionAttempt` for the Unity job that produced it. All three editors
must use the same execution attempt. The candidate attempt remains unchanged
when only failed jobs are rerun. The execution-attempt check accepts legacy schema-2 provenance without that
field when no expected execution attempt is supplied; current required evidence
files still apply. Mixed execution formats or attempts are rejected.
The base gate requires `base-consumer.log` for compilation without Test Framework
and `base-test-framework-consumer.log` for compilation with Test Framework but
without `testables`. Both Unity 6 gates require `source-generator-consumer.log`
for the latter configuration. Each consumer phase checks that production
assemblies exist and package test assemblies do not, before explicitly enabling
tests. Cloud gates also require a sanitized `ci-host.log`.
`SHA256SUMS` covers both packages and the manifest; the manifest and gate
summaries transitively cover all verification files. Use the successful
artifact for the commit being reviewed. Older or dirty-tree evidence is for
development only.

### Unity time budget

The Unity job has a 240-minute limit. Its first step records the start time;
the host controller stops new work at 210 minutes. Limits are 600 seconds for
Docker build, 300 seconds per Editor or activation, and 120 seconds for license
return and cleanup. A process-group watchdog also stops commands that ignore
termination signals and their child processes.

At the work deadline the host terminates the active container, allows bounded
license-return/cleanup time, records the interrupted gate as `failed` and
unstarted versions as `not-run`, and uploads partial redacted evidence. No
verified artifact is saved unless every gate passes. Inspect `gate.json` and
`ci-host.log` for the failed phase before retrying.

## Dry-run and publication

Start the manual release workflow with an exact 40-character SHA:

```sh
gh workflow run source-generator-release.yml --ref main \
  -f commit=REPLACE_WITH_FULL_COMMIT_SHA -f version=0.5.0 -F publish=false
```

The default `publish=false` verifies the full candidate and executes the CD
script without remote publication. Review the workflow result and complete
artifact. CI and CD dry-run success are the implementation's review gate;
registry delivery is confirmed at the first formal release.

For publication, complete the add-on's
[initial OpenUPM registration](SourceGenerators/OpenUPM/README.md), then start
the same workflow from `main` with `-F publish=true`. The trusted workflow
checks exact SHA, clean checkout, and membership in `origin/main` before
running target scripts, and repeats these checks at the write boundary.

After verification, the publisher creates the immutable tag and a draft
GitHub Release, uploads both packages, manifest, checksums and evidence
archive, downloads the assets again to verify their bytes, then publishes the
Release. Only the publication job receives repository write permissions.

The publisher dispatches `source-generator-openupm.yml` using the **tag as its
ref**. OpenUPM's OIDC token must refer to that exact tag, so this cannot run in
the original main-ref job. No extra OpenUPM Secret is needed. The follow-up
verifies the Release, waits for the base package through the official OpenUPM
action, then does the same for the add-on, and compares the registry add-on
tarball byte-for-byte with the verified asset.

## Failures and resuming

- **CI/activation failure:** inspect the failed gate and sanitized logs, fix
  the cause, and rerun verification. Missing or invalid credentials produce a
  non-success gate.
- **Failed Unity job:** rerun failed jobs in the same Actions run. The original
  candidate is reused, and the new Unity job publishes a distinct evidence
  artifact. Aggregation uses the artifact name and execution attempt returned
  by that Unity job, rather than guessing from the current run attempt.
- **Aggregate-only retry:** the successful Unity job's original evidence is
  reused, and a new successful verified artifact is named for the aggregate
  attempt. No existing artifact is overwritten.
- **Incorrect manual ref:** DLL update requires `main`; OpenUPM requires the
  release tag. Read-only preflight fails before write/OIDC jobs can begin.
- **DLL/version/evidence mismatch:** correct source, committed DLL or metadata
  in a reviewed commit and rebuild. Do not edit evidence to bypass checks.
- **Interrupted publication:** rerun only the failed publication job in the
  same Actions run. It reuses the original verified artifact and verification
  attempt. Matching draft assets remain; only missing assets are uploaded.
- **Existing tag/asset differs:** stop without overwriting. A full new
  verification run creates different run provenance and cannot substitute for
  the original resume artifact. Retain the original Actions run and artifact.
- **Published Release incomplete/unexpected:** stop and investigate. The
  publisher does not modify published assets to repair it.
- **OpenUPM failure:** fix registration/service conditions and rerun the
  follow-up at the same tag. A successful main release job alone does not prove
  registry delivery. A byte mismatch requires investigation and a new version.

## Local Mac verification

Install the three licensed Editors, Node from `.node-version`, npm `11.19.0`,
and .NET SDK `10.0.301`. From a clean checkout:

```sh
SourceGenerators/scripts/verify.sh
SourceGenerators/scripts/verify-unity-matrix.sh
```

The matrix packs once and uses the shared schema-2 aggregator. `--allow-dirty`
is available for development and records no publishable commit.
`UNITY_EXECUTABLE`, `SOURCE_GENERATOR_RELEASE_OUTPUT`,
`SOURCE_GENERATOR_EVIDENCE_ROOT`, and
`SOURCE_GENERATOR_UNITY_TIMEOUT_SECONDS` support local overrides. The CD
evidence archive uses GNU tar and runs on Linux in Actions.

## Rollback

1. Remove `jp.rhycol.openapicodegen.source-generator` from the Unity project
   manifest and allow Unity to remove its provider define.
2. Keep the base package and select the Docker provider in Settings.
3. Pin the base to the existing `0.4.0` release if its `0.5.0` contract is also
   implicated.
4. Regenerate affected clients and adjust code that depended on generated
   types. Record the project/package changes.

Initial formal publication and add-on OpenUPM registration are tracked in
[#62](https://github.com/rebeat-jp/UnityOpenApiCodeGen/issues/62), separately from
PR #56's review fixes and dry-run verification.

Never move or delete a published tag or replace a published package. Fixes use
a new patch version, such as `0.5.1`, through the same verification process.
