#!/usr/bin/env bash

# Run all local Unity gates and use the same evidence aggregator as cloud CI.
set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

if [[ $# -gt 1 || ( $# -eq 1 && "${1}" != "--allow-dirty" ) ]]; then
  echo "Usage: $0 [--allow-dirty]" >&2
  exit 2
fi

pack_release_args=()
if [[ $# -eq 1 ]]; then
  pack_release_args=("--allow-dirty")
fi

release_version="$(node -p "require('${repository_root}/Packages/OpenApiCodeGen/package.json').version")"
release_output="${SOURCE_GENERATOR_RELEASE_OUTPUT:-${repository_root}/artifacts/upm/${release_version}}"
evidence_root="${release_output}/unity-gate"
release_manifest="${release_output}/release-manifest.json"

if ! command -v node >/dev/null 2>&1; then
  echo "node is required to aggregate Unity gate evidence." >&2
  exit 2
fi

mkdir -p "${release_output}" "${evidence_root}"

dotnet_status=0
if "${scripts_directory}/verify.sh"; then
  :
else
  dotnet_status=$?
fi

if [[ "${dotnet_status}" -eq 0 ]]; then
  if SOURCE_GENERATOR_RELEASE_OUTPUT="${release_output}" \
      "${scripts_directory}/pack-release.sh" "${pack_release_args[@]}"; then
    :
  else
    dotnet_status=$?
  fi
fi

# A failed automated preflight must not prevent the other version directories
# from receiving explicit not-run evidence. When preflight succeeds, run every
# editor gate even if an earlier editor is unavailable or fails.
run_gate() {
  local version="$1"
  local gate_script="$2"
  local gate_status=0

  if [[ "${dotnet_status}" -ne 0 ]]; then
    write_synthetic_gate "${version}" "automated verify.sh preflight failed"
    return 0
  fi

  if SOURCE_GENERATOR_SKIP_DOTNET_VERIFY=1 \
      SOURCE_GENERATOR_RELEASE_OUTPUT="${release_output}" \
      SOURCE_GENERATOR_EVIDENCE_ROOT="${evidence_root}/${version}" \
      "${gate_script}" "${version}" "${pack_release_args[@]}"; then
    :
  else
    gate_status=$?
    # The per-version script owns status classification and gate.json. Keep
    # going so the aggregate can distinguish failed from not-run.
    echo "Unity gate ${version} exited with ${gate_status}; continuing matrix." >&2
  fi
}

write_synthetic_gate() {
  local version="$1"
  local reason="$2"

  mkdir -p "${evidence_root}/${version}"
  node - "${evidence_root}/${version}/gate.json" "${version}" "${reason}" "${release_output}" <<'NODE'
const fs = require('fs');
const path = require('path');

const [outputPath, version, reason, releaseOutput] = process.argv.slice(2);
const evidenceRoot = path.dirname(outputPath);
const summary = {
  schemaVersion: 2,
  status: 'not-run',
  version,
  exitCode: 2,
  reason,
  evidencePath: path.relative(releaseOutput, evidenceRoot).split(path.sep).join('/'),
  files: [],
  timestampUtc: new Date().toISOString()
};
fs.writeFileSync(outputPath, JSON.stringify(summary, null, 2) + '\n', 'utf8');
NODE
}

# Bash resolves functions at execution time, so declare the synthetic writer
# before invoking run_gate in the loop below.
run_gate "2021.3.19f1" "${scripts_directory}/verify-base-unity.sh"
run_gate "6000.0.23f1" "${scripts_directory}/verify-unity.sh"
run_gate "6000.3.2f1" "${scripts_directory}/verify-unity.sh"

if [[ ! -f "${release_manifest}" ]]; then
  echo "Release manifest was not produced; Unity gate aggregate cannot be recorded." >&2
  exit 1
fi

expected_node_version="$(tr -d '[:space:]' < "${repository_root}/.node-version")"
node "${scripts_directory}/aggregate-unity-evidence.js" \
  --release-output "${release_output}" \
  --analyzer "${package_analyzer}" \
  --expected-version "${release_version}" \
  --expected-node-version "v${expected_node_version}" \
  --expected-npm-version "11.19.0" \
  --require-unity-gate

aggregate_status="$(node - "${release_manifest}" <<'NODE'
const fs = require('fs');
const manifest = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
process.stdout.write(manifest.unityGate.status);
NODE
)"
if [[ "${aggregate_status}" == "passed" ]]; then
  exit 0
fi
exit 1
