#!/usr/bin/env bash

# Run every manual Unity gate, preserve evidence for each editor version, and
# record the aggregate state in the local release manifest. Unity CI is
# intentionally not part of GitHub Actions for this release candidate.
set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

release_version="0.5.0"
release_output="${SOURCE_GENERATOR_RELEASE_OUTPUT:-${repository_root}/artifacts/upm/${release_version}}"
evidence_root="${release_output}/unity-gate"
aggregate_gate="${release_output}/unity-gate.json"
release_manifest="${release_output}/release-manifest.json"
checksums_file="${release_output}/SHA256SUMS"

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
      "${gate_script}" "${version}"; then
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
  node - "${evidence_root}/${version}/gate.json" "${version}" "${reason}" "${repository_root}" <<'NODE'
const fs = require('fs');
const path = require('path');

const [outputPath, version, reason, repositoryRoot] = process.argv.slice(2);
const evidenceRoot = path.dirname(outputPath);
const summary = {
  schemaVersion: 1,
  status: 'not-run',
  version,
  exitCode: 2,
  reason,
  resultPath: null,
  logPath: null,
  evidencePath: path.relative(repositoryRoot, evidenceRoot).split(path.sep).join('/'),
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

node - "${release_manifest}" "${checksums_file}" "${evidence_root}" \
  "${aggregate_gate}" "${repository_root}" <<'NODE'
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const [manifestPath, checksumsPath, evidenceRoot, aggregatePath, repositoryRoot] = process.argv.slice(2);
const versions = ['2021.3.19f1', '6000.0.23f1', '6000.3.2f1'];
const readJson = filePath => JSON.parse(fs.readFileSync(filePath, 'utf8'));
const fail = message => { throw new Error(message); };
const manifest = readJson(manifestPath);
const evidence = versions.map(version => {
  const filePath = path.join(evidenceRoot, version, 'gate.json');
  if (!fs.statSync(filePath).isFile()) fail(`missing gate evidence: ${version}`);
  const gate = readJson(filePath);
  if (gate.version !== version || !['passed', 'failed', 'not-run'].includes(gate.status)) {
    fail(`invalid gate evidence status: ${version}`);
  }
  return {
    version,
    status: gate.status,
    path: path.relative(repositoryRoot, filePath).split(path.sep).join('/')
  };
});
const statuses = evidence.map(item => item.status);
const status = statuses.every(value => value === 'passed')
  ? 'passed'
  : statuses.includes('failed') ? 'failed' : 'not-run';
const aggregate = {
  schemaVersion: 1,
  mode: 'manual',
  status,
  versions: evidence,
  timestampUtc: new Date().toISOString()
};
fs.writeFileSync(aggregatePath, JSON.stringify(aggregate, null, 2) + '\n', 'utf8');
manifest.unityGate = {
  mode: 'manual',
  status,
  versions,
  evidence: evidence.map(item => item.path)
};
fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n', 'utf8');

const archiveFiles = manifest.archives.map(archive => archive.file);
const files = archiveFiles.concat('release-manifest.json').sort();
const sha256 = fileName => crypto.createHash('sha256')
  .update(fs.readFileSync(path.join(path.dirname(manifestPath), fileName)))
  .digest('hex');
fs.writeFileSync(checksumsPath, files.map(fileName => `${sha256(fileName)}  ${fileName}`).join('\n') + '\n', 'utf8');
console.log(`Unity gate aggregate: ${status}`);
evidence.forEach(item => console.log(`${item.version}: ${item.status} (${item.path})`));
NODE

if node - "${release_manifest}" "${checksums_file}" <<'NODE'
const fs = require('fs');
const crypto = require('crypto');

const [manifestPath, checksumsPath] = process.argv.slice(2);
const outputDirectory = require('path').dirname(manifestPath);
const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
const expectedFiles = manifest.archives.map(archive => archive.file).concat('release-manifest.json').sort();
const lines = fs.readFileSync(checksumsPath, 'utf8').trim().split(/\r?\n/);
if (lines.length !== expectedFiles.length) process.exit(1);
for (let index = 0; index < lines.length; index += 1) {
  const match = /^([0-9a-f]{64})  (\S+)$/.exec(lines[index]);
  if (!match || match[2] !== expectedFiles[index]) process.exit(1);
  const actual = crypto.createHash('sha256')
    .update(fs.readFileSync(require('path').join(outputDirectory, match[2])))
    .digest('hex');
  if (actual !== match[1]) process.exit(1);
}
NODE
then
  :
else
  echo "SHA256SUMS validation failed after updating Unity gate aggregate." >&2
  exit 1
fi

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
