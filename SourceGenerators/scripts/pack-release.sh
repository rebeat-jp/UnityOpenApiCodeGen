#!/usr/bin/env bash

# Build and verify the reproducible UPM release candidate. This script deliberately
# never publishes to npm, creates tags, or talks to a release service.
set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

readonly release_version="0.5.0"
readonly release_tag="0.5.0"
readonly release_output="${repository_root}/artifacts/upm/${release_version}"
readonly base_package_root="${repository_root}/Packages/OpenApiCodeGen"
readonly source_generator_package_root="${repository_root}/Packages/OpenApiCodeGen.SourceGenerator"
readonly base_package_name="jp.rhycol.openapicodegen"
readonly source_generator_package_name="jp.rhycol.openapicodegen.source-generator"
readonly base_archive_name="${base_package_name}-${release_version}.tgz"
readonly source_generator_archive_name="${source_generator_package_name}-${release_version}.tgz"

require_command() {
  local command_name="$1"
  if ! command -v "${command_name}" >/dev/null 2>&1; then
    echo "Required command is not available: ${command_name}" >&2
    exit 1
  fi
}

read_package_field() {
  local manifest="$1"
  local field="$2"

  node - "${manifest}" "${field}" <<'NODE'
const fs = require('fs');
const manifestPath = process.argv[2];
const field = process.argv[3];
const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
const value = manifest[field];
if (typeof value !== 'string') {
  process.stderr.write(`Expected string package field: ${field}\n`);
  process.exit(1);
}
process.stdout.write(value);
NODE
}

file_size_bytes() {
  local path="$1"
  if stat -f '%z' "${path}" >/dev/null 2>&1; then
    stat -f '%z' "${path}"
  else
    stat -c '%s' "${path}"
  fi
}

sha256_hex() {
  shasum -a 256 "$1" | awk '{ print $1 }'
}

single_archive_from() {
  local directory="$1"
  local archives=()
  local archive

  while IFS= read -r archive; do
    if [[ -n "${archive}" ]]; then
      archives+=("${archive}")
    fi
  done < <(find "${directory}" -maxdepth 1 -type f -name '*.tgz' -print | LC_ALL=C sort)

  if [[ ${#archives[@]} -ne 1 ]]; then
    echo "npm pack must produce exactly one tarball in ${directory}." >&2
    printf 'Found: %s\n' "${archives[@]:-<none>}" >&2
    exit 1
  fi

  printf '%s\n' "${archives[0]}"
}

pack_once() {
  local package_root_to_pack="$1"
  local destination="$2"

  mkdir -p "${destination}"
  (
    cd "${package_root_to_pack}"
    npm pack --ignore-scripts --loglevel=error --pack-destination "${destination}" >/dev/null
  )
  single_archive_from "${destination}"
}

assert_package_version() {
  local package_root_to_check="$1"
  local expected_name="$2"
  local manifest="${package_root_to_check}/package.json"
  local actual_name
  local actual_version

  require_file "${manifest}"
  actual_name="$(read_package_field "${manifest}" name)"
  actual_version="$(read_package_field "${manifest}" version)"
  if [[ "${actual_name}" != "${expected_name}" || "${actual_version}" != "${release_version}" ]]; then
    echo "Package metadata mismatch in ${manifest}: ${actual_name}@${actual_version}" >&2
    exit 1
  fi
}

validate_package_manifest_metadata() {
  node - "$1" "$2" "$3" "$4" <<'NODE'
const fs = require('fs');

const manifestPath = process.argv[2];
const expectedName = process.argv[3];
const expectedUnity = process.argv[4];
const packagePath = process.argv[5];

function fail(message) {
  throw new Error(message);
}

function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    fail(`${label}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
const repositoryBase = 'https://github.com/rebeat-jp/UnityOpenApiCodeGen';
const treeUrl = `${repositoryBase}/tree/0.5.0/${packagePath}`;
const blobUrl = fileName => `${repositoryBase}/blob/0.5.0/${packagePath}/${fileName}`;

assertEqual(manifest.name, expectedName, 'package name');
assertEqual(manifest.version, '0.5.0', 'package version');
assertEqual(manifest.unity, expectedUnity, 'minimum Unity version');
if (!manifest.repository || typeof manifest.repository !== 'object') {
  fail('package repository metadata is missing');
}
assertEqual(manifest.repository.type, 'git', 'repository type');
assertEqual(manifest.repository.url, treeUrl, 'repository URL');
assertEqual(manifest.documentationUrl, blobUrl('README.md'), 'documentation URL');
assertEqual(manifest.changelogUrl, blobUrl('CHANGELOG.md'), 'changelog URL');
assertEqual(manifest.licensesUrl, blobUrl('LICENSE.md'), 'licenses URL');
NODE
}

validate_package_contract() {
  validate_package_manifest_metadata \
    "${base_package_root}/package.json" \
    "${base_package_name}" \
    "2021.3" \
    "Packages/OpenApiCodeGen"
  validate_package_manifest_metadata \
    "${source_generator_package_root}/package.json" \
    "${source_generator_package_name}" \
    "6000.0" \
    "Packages/OpenApiCodeGen.SourceGenerator"

  node - "${base_package_root}/package.json" "${source_generator_package_root}/package.json" "${repository_root}/Packages/packages-lock.json" "${source_generator_package_root}" <<'NODE'
const fs = require('fs');
const path = require('path');

const baseManifestPath = process.argv[2];
const sourceGeneratorManifestPath = process.argv[3];
const lockPath = process.argv[4];
const sourceGeneratorRoot = process.argv[5];

function fail(message) {
  throw new Error(message);
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    fail(`${label}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}

function assertDirectory(relativePath) {
  const filePath = path.join(sourceGeneratorRoot, relativePath);
  if (!fs.statSync(filePath).isDirectory()) {
    fail(`Missing sample directory: ${relativePath}`);
  }
  return filePath;
}

function assertFile(relativePath) {
  const filePath = path.join(sourceGeneratorRoot, relativePath);
  if (!fs.statSync(filePath).isFile()) {
    fail(`Missing sample fixture: ${relativePath}`);
  }
  return filePath;
}

function assertTextIncludes(text, fragments, label) {
  for (const fragment of fragments) {
    if (!text.includes(fragment)) {
      fail(`${label} is missing ${JSON.stringify(fragment)}`);
    }
  }
}

const base = readJson(baseManifestPath);
const sourceGenerator = readJson(sourceGeneratorManifestPath);
const lock = readJson(lockPath);
assertEqual(base.name, 'jp.rhycol.openapicodegen', 'base package name');
assertEqual(base.version, '0.5.0', 'base package version');
assertEqual(base.unity, '2021.3', 'base minimum Unity version');
assertEqual(sourceGenerator.name, 'jp.rhycol.openapicodegen.source-generator', 'source-generator package name');
assertEqual(sourceGenerator.version, '0.5.0', 'source-generator package version');
assertEqual(sourceGenerator.unity, '6000.0', 'source-generator minimum Unity version');
assertEqual(sourceGenerator.dependencies['jp.rhycol.openapicodegen'], '0.5.0', 'source-generator base dependency');
assertEqual(sourceGenerator.dependencies['com.unity.nuget.newtonsoft-json'], '3.2.2', 'Newtonsoft dependency');

const lockedSourceGenerator = lock.dependencies['jp.rhycol.openapicodegen.source-generator'];
if (!lockedSourceGenerator || !lockedSourceGenerator.dependencies) {
  fail('packages-lock is missing the source-generator dependency record');
}
assertEqual(lockedSourceGenerator.dependencies['jp.rhycol.openapicodegen'], '0.5.0', 'locked source-generator base dependency');
assertEqual(lockedSourceGenerator.dependencies['com.unity.nuget.newtonsoft-json'], '3.2.2', 'locked Newtonsoft dependency');

const expectedSamples = [
  ['Local JSON', 'Samples~/Local JSON'],
  ['Local YAML', 'Samples~/Local YAML'],
  ['URL', 'Samples~/URL'],
  ['External Reference', 'Samples~/External Reference']
];
if (!Array.isArray(sourceGenerator.samples) || sourceGenerator.samples.length !== expectedSamples.length) {
  fail('source-generator package must declare exactly four samples');
}
sourceGenerator.samples.forEach((sample, index) => {
  if (!sample || sample.displayName !== expectedSamples[index][0] || sample.path !== expectedSamples[index][1]) {
    fail(`sample ${index} displayName/path mismatch`);
  }
  if (typeof sample.description !== 'string' || sample.description.trim().length === 0) {
    fail(`sample ${index} description must be a non-empty string`);
  }
  assertDirectory(sample.path);
});

const localJsonPath = assertFile('Samples~/Local JSON/openapi.json');
const urlJsonPath = assertFile('Samples~/URL/openapi.json');
const localYamlPath = assertFile('Samples~/Local YAML/openapi.yaml');
const externalJsonPath = assertFile('Samples~/External Reference/openapi.json');
const externalYamlPath = assertFile('Samples~/External Reference/models.yaml');
const localJson = readJson(localJsonPath);
const urlJson = readJson(urlJsonPath);

function assertItemsDocument(document, label, reference) {
  assertEqual(document.openapi, '3.1.0', `${label} OpenAPI version`);
  const operation = document.paths && document.paths['/items'] && document.paths['/items'].get;
  if (!operation) {
    fail(`${label} is missing GET /items`);
  }
  assertEqual(operation.operationId, 'getItems', `${label} operationId`);
  const responseSchema = operation.responses && operation.responses['200'] &&
    operation.responses['200'].content && operation.responses['200'].content['application/json'] &&
    operation.responses['200'].content['application/json'].schema;
  if (!responseSchema || responseSchema.type !== 'array' || !responseSchema.items) {
    fail(`${label} is missing the Items response schema`);
  }
  assertEqual(responseSchema.items.$ref, reference, `${label} response reference`);
}

assertItemsDocument(localJson, 'Local JSON', '#/components/schemas/Item');
assertItemsDocument(urlJson, 'URL', '#/components/schemas/Item');
if (!Buffer.from(fs.readFileSync(localJsonPath)).equals(Buffer.from(fs.readFileSync(urlJsonPath)))) {
  fail('URL sample must contain the same spec asset as the Local JSON sample');
}

const localYaml = fs.readFileSync(localYamlPath, 'utf8');
assertTextIncludes(localYaml, [
  'openapi: 3.1.0',
  '/items:',
  'operationId: getItems',
  'Item:',
  'id:',
  'label:'
], 'Local YAML sample');

const externalJson = readJson(externalJsonPath);
assertEqual(externalJson.openapi, '3.1.0', 'External Reference OpenAPI version');
const externalOperation = externalJson.paths && externalJson.paths['/items'] && externalJson.paths['/items'].get;
if (!externalOperation) {
  fail('External Reference sample is missing GET /items');
}
assertEqual(externalOperation.operationId, 'getItems', 'External Reference operationId');
const externalSchema = externalOperation.responses['200'].content['application/json'].schema;
assertEqual(externalSchema.items.$ref, './models.yaml', 'External Reference target');
const externalYaml = fs.readFileSync(externalYamlPath, 'utf8');
assertTextIncludes(externalYaml, ['type: object', 'required:', 'id:', 'label:'], 'External YAML schema');

console.log('Verified package versions, lock dependencies, samples, and sample fixtures.');
NODE
}

validate_release_outputs() {
  node - "${release_manifest}" "${release_output}" "${package_analyzer}" <<'NODE'
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const manifestPath = process.argv[2];
const outputDirectory = process.argv[3];
const analyzerPath = process.argv[4];

function fail(message) {
  throw new Error(message);
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function assertExactKeys(value, keys, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    fail(`${label} must be an object`);
  }
  const actual = Object.keys(value).sort();
  const expected = keys.slice().sort();
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index])) {
    fail(`${label} has an unexpected field set: ${actual.join(', ')}`);
  }
}

function assertHex(value, length, label) {
  if (typeof value !== 'string' || !new RegExp(`^[0-9a-f]{${length}}$`).test(value)) {
    fail(`${label} must be ${length} lowercase hexadecimal characters`);
  }
}

function sha256(filePath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex');
}

const manifest = readJson(manifestPath);
assertExactKeys(manifest, [
  'schemaVersion',
  'version',
  'tag',
  'commit',
  'archives',
  'analyzerSha256',
  'unityGate',
  'registryPublication'
], 'release manifest');
if (manifest.schemaVersion !== 1 || typeof manifest.schemaVersion !== 'number') {
  fail('release manifest schemaVersion must be numeric 1');
}
if (manifest.version !== '0.5.0' || manifest.tag !== '0.5.0') {
  fail('release manifest version/tag must both be 0.5.0');
}
assertHex(manifest.commit, 40, 'release manifest commit');
assertHex(manifest.analyzerSha256, 64, 'release manifest analyzerSha256');
if (!Array.isArray(manifest.archives) || manifest.archives.length !== 2) {
  fail('release manifest must contain exactly two archives');
}

const expectedArchives = [
  {
    package: 'jp.rhycol.openapicodegen',
    version: '0.5.0',
    path: 'Packages/OpenApiCodeGen',
    file: 'jp.rhycol.openapicodegen-0.5.0.tgz'
  },
  {
    package: 'jp.rhycol.openapicodegen.source-generator',
    version: '0.5.0',
    path: 'Packages/OpenApiCodeGen.SourceGenerator',
    file: 'jp.rhycol.openapicodegen.source-generator-0.5.0.tgz'
  }
];
manifest.archives.forEach((archive, index) => {
  assertExactKeys(archive, ['package', 'version', 'path', 'file', 'root', 'size', 'sha256'], `archive ${index}`);
  const expected = expectedArchives[index];
  for (const field of ['package', 'version', 'path', 'file']) {
    if (archive[field] !== expected[field]) {
      fail(`archive ${index} ${field} mismatch`);
    }
  }
  if (archive.root !== 'package/' || !Number.isSafeInteger(archive.size) || archive.size <= 0) {
    fail(`archive ${index} root/size is invalid`);
  }
  assertHex(archive.sha256, 64, `archive ${index} sha256`);
  const archivePath = path.join(outputDirectory, archive.file);
  if (!fs.statSync(archivePath).isFile()) {
    fail(`archive file is missing: ${archive.file}`);
  }
  if (fs.statSync(archivePath).size !== archive.size) {
    fail(`archive size does not match manifest: ${archive.file}`);
  }
  if (sha256(archivePath) !== archive.sha256) {
    fail(`archive SHA-256 does not match manifest: ${archive.file}`);
  }
});

if (sha256(analyzerPath) !== manifest.analyzerSha256) {
  fail('analyzer SHA-256 does not match release manifest');
}

assertExactKeys(manifest.unityGate, ['mode', 'status', 'versions', 'evidence'], 'unityGate');
if (manifest.unityGate.mode !== 'manual' || manifest.unityGate.status !== 'not-run') {
  fail('unityGate must start in manual/not-run state');
}
const expectedUnityVersions = ['2021.3.19f1', '6000.0.23f1', '6000.3.2f1'];
if (JSON.stringify(manifest.unityGate.versions) !== JSON.stringify(expectedUnityVersions) ||
    !Array.isArray(manifest.unityGate.evidence) || manifest.unityGate.evidence.length !== 0) {
  fail('unityGate versions/evidence mismatch');
}
if (manifest.registryPublication !== 'not-performed') {
  fail('registryPublication must be not-performed');
}

const checksumsPath = path.join(outputDirectory, 'SHA256SUMS');
const checksumLines = fs.readFileSync(checksumsPath, 'utf8').trim().split(/\r?\n/);
const expectedChecksumFiles = manifest.archives.map(archive => archive.file).concat('release-manifest.json');
expectedChecksumFiles.sort();
if (checksumLines.length !== expectedChecksumFiles.length) {
  fail('SHA256SUMS must contain exactly the two archives and release manifest');
}
const seen = new Set();
checksumLines.forEach((line, index) => {
  const match = /^([0-9a-f]{64})  (\S+)$/.exec(line);
  if (!match) {
    fail(`invalid SHA256SUMS line ${index + 1}`);
  }
  const checksum = match[1];
  const fileName = match[2];
  if (fileName !== expectedChecksumFiles[index] || seen.has(fileName)) {
    fail('SHA256SUMS filenames are not unique and filename-sorted');
  }
  seen.add(fileName);
  const filePath = path.join(outputDirectory, fileName);
  if (!fs.statSync(filePath).isFile() || sha256(filePath) !== checksum) {
    fail(`SHA256SUMS checksum mismatch: ${fileName}`);
  }
});

console.log('Verified release manifest schema, archive metadata, analyzer SHA, gate state, and SHA256SUMS.');
NODE

(
  cd "${release_output}"
  shasum -a 256 -c "$(basename "${checksums_file}")"
)
}

assert_archive_entry() {
  local entries_file="$1"
  local expected_entry="$2"
  if ! grep -F -x -q -- "${expected_entry}" "${entries_file}"; then
    echo "UPM archive is missing required entry: ${expected_entry}" >&2
    exit 1
  fi
}

verify_archive() {
  local archive="$1"
  local expected_package_name="$2"
  local expected_dll_count="$3"
  local expected_analyzer_entry="$4"
  local inspect_root="$5"
  local entries_file="${inspect_root}/entries.txt"
  local extracted_root="${inspect_root}/extracted"
  local dll_entries=()
  local entry

  mkdir -p "${inspect_root}" "${extracted_root}"
  tar -tzf "${archive}" > "${entries_file}"

  while IFS= read -r entry; do
    [[ -z "${entry}" ]] && continue

    case "${entry}" in
      package/*) ;;
      *)
        echo "UPM archive entry is outside the package/ root: ${entry}" >&2
        exit 1
        ;;
    esac

    case "${entry}" in
      package/../*|package/..|*/../*|../*|/*)
        echo "UPM archive contains an unsafe path: ${entry}" >&2
        exit 1
        ;;
    esac

    if printf '%s\n' "${entry}" | LC_ALL=C grep -E -i -q '(^|/)(bin|obj|library|temp)(/|$)'; then
      echo "UPM archive contains a build or Unity generated path: ${entry}" >&2
      exit 1
    fi

    if printf '%s\n' "${entry}" | LC_ALL=C grep -E -i -q '(^|/)(microsoft\.codeanalysis(\.csharp)?|newtonsoft\.json|system\.text\.json|yamldotnet)\.dll$'; then
      echo "UPM archive contains a forbidden dependency DLL: ${entry}" >&2
      exit 1
    fi

    case "${entry}" in
      *.dll) dll_entries+=("${entry}") ;;
    esac
  done < "${entries_file}"

  if [[ ${#dll_entries[@]} -ne ${expected_dll_count} ]]; then
    echo "Unexpected DLL count in ${archive}: ${#dll_entries[@]} (expected ${expected_dll_count})." >&2
    printf 'Found: %s\n' "${dll_entries[@]:-<none>}" >&2
    exit 1
  fi

  if [[ -n "${expected_analyzer_entry}" ]]; then
    assert_archive_entry "${entries_file}" "${expected_analyzer_entry}"
  fi

  assert_archive_entry "${entries_file}" "package/package.json"
  tar -xzf "${archive}" -C "${extracted_root}"

  local package_manifest="${extracted_root}/package/package.json"
  local actual_name
  local actual_version
  local expected_unity="2021.3"
  local expected_package_path="Packages/OpenApiCodeGen"
  actual_name="$(read_package_field "${package_manifest}" name)"
  actual_version="$(read_package_field "${package_manifest}" version)"
  if [[ "${actual_name}" != "${expected_package_name}" || "${actual_version}" != "${release_version}" ]]; then
    echo "UPM archive package metadata mismatch: ${actual_name}@${actual_version}" >&2
    exit 1
  fi
  if [[ "${expected_package_name}" == "${source_generator_package_name}" ]]; then
    expected_unity="6000.0"
    expected_package_path="Packages/OpenApiCodeGen.SourceGenerator"
  fi
  validate_package_manifest_metadata \
    "${package_manifest}" \
    "${expected_package_name}" \
    "${expected_unity}" \
    "${expected_package_path}"

  if LC_ALL=C grep -a -R -F -q -- "${repository_root}" "${extracted_root}/package"; then
    echo "UPM archive contains the absolute checkout path: ${repository_root}" >&2
    exit 1
  fi
  if [[ "${expected_package_name}" == "${source_generator_package_name}" ]]; then
    assert_archive_entry "${entries_file}" "package/Samples~/Local JSON/openapi.json"
    assert_archive_entry "${entries_file}" "package/Samples~/Local YAML/openapi.yaml"
    assert_archive_entry "${entries_file}" "package/Samples~/URL/openapi.json"
    assert_archive_entry "${entries_file}" "package/Samples~/External Reference/openapi.json"
    assert_archive_entry "${entries_file}" "package/Samples~/External Reference/models.yaml"
  fi
}

require_command npm
require_command node
require_command git
require_command tar
require_command shasum
require_command awk
require_command grep
require_command stat
require_command sort
require_command find

assert_package_version "${base_package_root}" "${base_package_name}"
assert_package_version "${source_generator_package_root}" "${source_generator_package_name}"
require_file "${analyzer_output}"
require_file "${package_analyzer}"
if ! cmp -s "${analyzer_output}" "${package_analyzer}"; then
  echo "The packaged analyzer is stale. Run SourceGenerators/scripts/sync-analyzer.sh first." >&2
  shasum -a 256 "${analyzer_output}" "${package_analyzer}" >&2
  exit 1
fi

commit="$(git -C "${repository_root}" rev-parse HEAD)"
if ! printf '%s\n' "${commit}" | LC_ALL=C grep -E -q '^[0-9a-f]{40}$'; then
  echo "git rev-parse HEAD did not return a 40-character commit: ${commit}" >&2
  exit 1
fi

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Pack.XXXXXX")"
trap 'rm -rf "${temporary_root}"' EXIT

base_first="$(pack_once "${base_package_root}" "${temporary_root}/base-first")"
base_second="$(pack_once "${base_package_root}" "${temporary_root}/base-second")"
source_generator_first="$(pack_once "${source_generator_package_root}" "${temporary_root}/source-generator-first")"
source_generator_second="$(pack_once "${source_generator_package_root}" "${temporary_root}/source-generator-second")"

if ! cmp -s "${base_first}" "${base_second}"; then
  echo "Base UPM package is not byte-for-byte reproducible." >&2
  shasum -a 256 "${base_first}" "${base_second}" >&2
  exit 1
fi
if ! cmp -s "${source_generator_first}" "${source_generator_second}"; then
  echo "Source-generator UPM package is not byte-for-byte reproducible." >&2
  shasum -a 256 "${source_generator_first}" "${source_generator_second}" >&2
  exit 1
fi

mkdir -p "${release_output}"
base_archive="${release_output}/${base_archive_name}"
source_generator_archive="${release_output}/${source_generator_archive_name}"
release_manifest="${release_output}/release-manifest.json"
checksums_file="${release_output}/SHA256SUMS"
rm -f "${base_archive}" "${source_generator_archive}" "${release_manifest}" "${checksums_file}"
cp "${base_first}" "${base_archive}"
cp "${source_generator_first}" "${source_generator_archive}"

verification_root="${temporary_root}/verification"
verify_archive "${base_archive}" "${base_package_name}" 0 "" "${verification_root}/base"
verify_archive \
  "${source_generator_archive}" \
  "${source_generator_package_name}" \
  1 \
  "package/Runtime/Analyzers/${analyzer_assembly}" \
  "${verification_root}/source-generator"

base_size="$(file_size_bytes "${base_archive}")"
source_generator_size="$(file_size_bytes "${source_generator_archive}")"
base_sha256="$(sha256_hex "${base_archive}")"
source_generator_sha256="$(sha256_hex "${source_generator_archive}")"
analyzer_sha256="$(sha256_hex "${package_analyzer}")"

cat > "${release_manifest}" <<EOF
{
  "schemaVersion": 1,
  "version": "${release_version}",
  "tag": "${release_tag}",
  "commit": "${commit}",
  "archives": [
    {
      "package": "${base_package_name}",
      "version": "${release_version}",
      "path": "Packages/OpenApiCodeGen",
      "file": "${base_archive_name}",
      "root": "package/",
      "size": ${base_size},
      "sha256": "${base_sha256}"
    },
    {
      "package": "${source_generator_package_name}",
      "version": "${release_version}",
      "path": "Packages/OpenApiCodeGen.SourceGenerator",
      "file": "${source_generator_archive_name}",
      "root": "package/",
      "size": ${source_generator_size},
      "sha256": "${source_generator_sha256}"
    }
  ],
  "analyzerSha256": "${analyzer_sha256}",
  "unityGate": {
    "mode": "manual",
    "status": "not-run",
    "versions": [
      "2021.3.19f1",
      "6000.0.23f1",
      "6000.3.2f1"
    ],
    "evidence": []
  },
  "registryPublication": "not-performed"
}
EOF

{
  printf '%s  %s\n' "${base_sha256}" "${base_archive_name}"
  printf '%s  %s\n' "${source_generator_sha256}" "${source_generator_archive_name}"
  printf '%s  %s\n' "$(sha256_hex "${release_manifest}")" "$(basename "${release_manifest}")"
} | LC_ALL=C sort -k2,2 > "${checksums_file}"

validate_package_contract
validate_release_outputs

echo "Created reproducible UPM release candidate in ${release_output}."
cat "${checksums_file}"
