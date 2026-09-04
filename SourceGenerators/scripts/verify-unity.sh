#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

if [[ $# -gt 1 ]]; then
  echo "Usage: $0 [unity-version]" >&2
  exit 2
fi

unity_version="${1:-6000.3.2f1}"
unity_executable="${UNITY_EXECUTABLE:-/Applications/Unity/Hub/Editor/${unity_version}/Unity.app/Contents/MacOS/Unity}"
verification_fixture="${source_generators_root}/BuildTools/UnityVerificationFixture"
package_removal_fixture="${source_generators_root}/BuildTools/UnityPackageRemovalVerification"
release_version="0.5.0"
release_output="${SOURCE_GENERATOR_RELEASE_OUTPUT:-${repository_root}/artifacts/upm/${release_version}}"
base_archive="${release_output}/jp.rhycol.openapicodegen-${release_version}.tgz"
source_generator_archive="${release_output}/jp.rhycol.openapicodegen.source-generator-${release_version}.tgz"
evidence_root="${SOURCE_GENERATOR_EVIDENCE_ROOT:-${release_output}/unity-gate/${unity_version}}"
unity_timeout_seconds="${SOURCE_GENERATOR_UNITY_TIMEOUT_SECONDS:-300}"

case "${unity_version}" in
  6000.0.23f1)
    unity_revision="1c4764c07fb4"
    test_framework_version="1.4.5"
    ;;
  6000.3.2f1)
    unity_revision="a9779f353c9b"
    test_framework_version="1.6.0"
    ;;
  2021.3.19f1)
    echo "Unity ${unity_version} uses the base-package gate; invoke verify-unity-matrix.sh." >&2
    exit 2
    ;;
  *)
    echo "Add the installed editor revision for ${unity_version} before using this script." >&2
    exit 2
    ;;
esac

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/UnityOpenApiCodeGen-SourceGenerator-${unity_version}.XXXXXX")"
project_path="${temporary_root}/UnityProject"
results="${temporary_root}/source-generator-results.xml"
log="${temporary_root}/source-generator.log"
bootstrap_log="${temporary_root}/source-generator-bootstrap.log"
generation_log="${temporary_root}/source-generator-generation.log"
unchanged_generation_log="${temporary_root}/source-generator-unchanged-generation.log"
no_change_log="${temporary_root}/source-generator-no-change.log"
regeneration_results="${temporary_root}/source-generator-regeneration-results.xml"
regeneration_log="${temporary_root}/source-generator-regeneration.log"
regeneration_test_log="${temporary_root}/source-generator-regeneration-tests.log"
without_addon_results="${temporary_root}/without-addon-results.xml"
without_addon_log="${temporary_root}/without-addon.log"
without_addon_bootstrap_log="${temporary_root}/without-addon-bootstrap.log"
gate_status="not-run"
gate_reason="Source Generator Unity gate did not start"
published_authoritative_cache=""

fail_not_run() {
  gate_status="not-run"
  gate_reason="$1"
  exit 2
}

fail_gate() {
  gate_status="failed"
  gate_reason="$1"
  exit 1
}

copy_evidence_file() {
  local source_path="$1"
  local evidence_name="$2"

  if [[ -f "${source_path}" ]]; then
    cp "${source_path}" "${evidence_root}/${evidence_name}"
  fi
}

write_gate_evidence() {
  local exit_code="$1"

  mkdir -p "${evidence_root}"
  copy_evidence_file "${results}" "source-generator-results.xml"
  copy_evidence_file "${log}" "source-generator.log"
  copy_evidence_file "${bootstrap_log}" "source-generator-bootstrap.log"
  copy_evidence_file "${generation_log}" "source-generator-generation.log"
  copy_evidence_file "${unchanged_generation_log}" "source-generator-unchanged-generation.log"
  copy_evidence_file "${no_change_log}" "source-generator-no-change.log"
  copy_evidence_file "${regeneration_results}" "source-generator-regeneration-results.xml"
  copy_evidence_file "${regeneration_log}" "source-generator-regeneration.log"
  copy_evidence_file "${regeneration_test_log}" "source-generator-regeneration-tests.log"
  copy_evidence_file "${without_addon_results}" "without-addon-results.xml"
  copy_evidence_file "${without_addon_log}" "without-addon.log"
  copy_evidence_file "${without_addon_bootstrap_log}" "without-addon-bootstrap.log"
  copy_evidence_file "${published_authoritative_cache}" "normalized-v2.json"

  if command -v node >/dev/null 2>&1; then
    node - "${evidence_root}/gate.json" "${gate_status}" "${exit_code}" \
      "${unity_version}" "${gate_reason}" "${evidence_root}" "${repository_root}" <<'NODE'
const fs = require('fs');
const path = require('path');

const [outputPath, status, exitCode, version, reason, evidenceRoot, repositoryRoot] = process.argv.slice(2);
const relativePath = (candidate) => fs.existsSync(candidate)
  ? path.relative(repositoryRoot, candidate).split(path.sep).join('/')
  : null;
const resultCandidates = [
  'source-generator-results.xml',
  'source-generator-regeneration-results.xml',
  'without-addon-results.xml'
];
const logCandidates = [
  'source-generator.log',
  'source-generator-bootstrap.log',
  'source-generator-generation.log',
  'source-generator-unchanged-generation.log',
  'source-generator-no-change.log',
  'source-generator-regeneration.log',
  'source-generator-regeneration-tests.log',
  'without-addon.log',
  'without-addon-bootstrap.log'
];
const firstExisting = names => {
  const name = names.find(candidate => fs.existsSync(path.join(evidenceRoot, candidate)));
  return name ? path.join(evidenceRoot, name) : null;
};
const resultPath = firstExisting(resultCandidates);
const logPath = firstExisting(logCandidates);
const summary = {
  schemaVersion: 1,
  status,
  version,
  exitCode: Number(exitCode),
  reason,
  resultPath: resultPath ? relativePath(resultPath) : null,
  logPath: logPath ? relativePath(logPath) : null,
  evidencePath: path.relative(repositoryRoot, evidenceRoot).split(path.sep).join('/'),
  timestampUtc: new Date().toISOString()
};
fs.writeFileSync(outputPath, JSON.stringify(summary, null, 2) + '\n', 'utf8');
NODE
  else
    printf '{\n  "schemaVersion": 1,\n  "status": "%s",\n  "version": "%s",\n  "exitCode": %s,\n  "reason": "manual evidence writer unavailable"\n}\n' \
      "${gate_status}" "${unity_version}" "${exit_code}" > "${evidence_root}/gate.json"
  fi
}

cleanup_temporary_root() {
  local exit_code=$?
  trap - EXIT
  write_gate_evidence "${exit_code}"
  if [[ "${SOURCE_GENERATOR_KEEP_TEMP:-0}" == "1" ]]; then
    echo "Preserved verification workspace: ${temporary_root}"
  else
    rm -rf "${temporary_root}"
  fi
  exit "${exit_code}"
}
trap cleanup_temporary_root EXIT

if [[ ! -x "${unity_executable}" ]]; then
  fail_not_run "Unity editor is not installed at ${unity_executable}"
fi
if ! command -v xmllint >/dev/null 2>&1; then
  fail_not_run "xmllint is unavailable"
fi
if ! command -v node >/dev/null 2>&1; then
  fail_not_run "node is unavailable for Bundle v2 and gate evidence"
fi
if [[ ! -f "${base_archive}" || ! -f "${source_generator_archive}" ]]; then
  if ! "${scripts_directory}/pack-release.sh"; then
    fail_not_run "release-candidate packing failed before the Unity gate"
  fi
fi
if [[ ! -f "${base_archive}" || ! -f "${source_generator_archive}" ]]; then
  fail_not_run "base or Source Generator release-candidate tarball is missing"
fi

for required_file in \
  "${package_analyzer}" \
  "${package_analyzer}.meta" \
  "${verification_fixture}/Specs/openapi.json" \
  "${verification_fixture}/Specs/VerificationItem.yaml" \
  "${verification_fixture}/Target/GeneratedClientProbe.cs" \
  "${verification_fixture}/Editor/SourceGeneratorVerticalGeneration.cs" \
  "${package_removal_fixture}/Editor/SourceGeneratorPackageRemovalVerification.cs"; do
  if [[ ! -f "${required_file}" ]]; then
    fail_not_run "required Unity gate input is missing: ${required_file}"
  fi
done

if [[ "${SOURCE_GENERATOR_SKIP_DOTNET_VERIFY:-0}" != "1" ]]; then
  if ! "${scripts_directory}/verify.sh"; then
    fail_not_run "dotnet verification failed before the Unity gate"
  fi
fi

mkdir -p "${project_path}"
rsync -a \
  --exclude '.git' \
  --exclude 'Library' \
  --exclude 'Temp' \
  --exclude 'Logs' \
  --exclude 'artifacts' \
  --exclude 'bin' \
  --exclude 'obj' \
  "${repository_root}/" \
  "${project_path}/"

printf 'm_EditorVersion: %s\nm_EditorVersionWithRevision: %s (%s)\n' \
  "${unity_version}" "${unity_version}" "${unity_revision}" \
  > "${project_path}/ProjectSettings/ProjectVersion.txt"

# Move embedded package copies out of the temporary project. The manifest below
# installs both release-candidate tarballs as independent file dependencies.
local_packages="${temporary_root}/LocalPackages"
mkdir -p "${local_packages}/embedded"
for embedded_package in OpenApiCodeGen OpenApiCodeGen.SourceGenerator; do
  if [[ -d "${project_path}/Packages/${embedded_package}" ]]; then
    mv "${project_path}/Packages/${embedded_package}" \
      "${local_packages}/embedded/${embedded_package}"
  fi
done
rm -rf "${project_path}/Packages/OpenApiCodeGen.SourceGenerator.Phase0"

base_archive_name="$(basename "${base_archive}")"
source_generator_archive_name="$(basename "${source_generator_archive}")"
cp "${base_archive}" "${local_packages}/${base_archive_name}"
cp "${source_generator_archive}" "${local_packages}/${source_generator_archive_name}"

# A manifest under Packages/ resolves file: paths relative to that directory.
# Keep both package entries explicit so Package Manager cannot satisfy the gate
# from a repository copy or from a transitive dependency.
printf '%s\n' \
  '{' \
  '  "dependencies": {' \
  "    \"com.unity.test-framework\": \"${test_framework_version}\"," \
  "    \"jp.rhycol.openapicodegen\": \"file:../../LocalPackages/${base_archive_name}\"," \
  "    \"jp.rhycol.openapicodegen.source-generator\": \"file:../../LocalPackages/${source_generator_archive_name}\"" \
  '  },' \
  '  "testables": ["jp.rhycol.openapicodegen", "jp.rhycol.openapicodegen.source-generator"]' \
  '}' \
  > "${project_path}/Packages/manifest.json"
rm -f "${project_path}/Packages/packages-lock.json"

verification_assets="${project_path}/Assets/OpenApiCodeGen/SourceGeneratorVerification"
mkdir -p "${verification_assets}"
rsync -a "${verification_fixture}/" "${verification_assets}/"
package_removal_verification_assets="${project_path}/Assets/OpenApiCodeGen/SourceGeneratorPackageRemovalVerification"
mkdir -p "${package_removal_verification_assets}"
rsync -a "${package_removal_fixture}/" "${package_removal_verification_assets}/"
raw_spec="${verification_assets}/Specs/openapi.json"
generated_definition="${verification_assets}/Target/Generated/Api.OpenApiDefinition.cs"
compiler_mirror_directory="${project_path}/Assets/OpenApiCodeGen/Generated/SpecCache"
authoritative_cache_root="${project_path}/Library/OpenApiCodeGen/SourceGenerator/SpecCache"

# Select the Source Generator in the same persisted setting read by the base
# package. Generation itself is invoked through the registered public provider
# after define synchronization has settled.
printf '%s\n' \
  '{"GenerateProvider":1,"ApiDocumentFilePathOrUrl":"Assets/OpenApiCodeGen/SourceGeneratorVerification/Specs/openapi.json","ApiClientOutputFolderPath":"Assets/OpenApiCodeGen/SourceGeneratorVerification/Target/Generated"}' \
  > "${project_path}/Assets/OpenApiCodeGen/projectSettings.json"

# ApplicationConstant resolves the project settings folder from the current
# directory, so Unity must inherit the temporary verification project as cwd.
cd "${project_path}"

run_editor() {
  local output_log="$1"
  shift

  rm -f "${output_log}"
  if command -v perl >/dev/null 2>&1; then
    perl -e 'alarm shift; exec @ARGV' "${unity_timeout_seconds}" "$@" \
      -logFile "${output_log}"
  else
    "$@" -logFile "${output_log}"
  fi
}

classify_editor_failure() {
  local output_log="$1"
  local editor_exit_code="$2"
  local operation="$3"

  if [[ "${editor_exit_code}" == "142" || "${editor_exit_code}" == "124" || \
        ! -f "${output_log}" ]]; then
    fail_not_run "${operation} timed out or the Unity editor was unusable (exit ${editor_exit_code})"
  fi
  fail_gate "${operation} failed; inspect the copied Unity gate log"
}

run_unity_tests() {
  local test_results="$1"
  local test_log="$2"

  rm -f "${test_results}" "${test_log}"
  if run_editor "${test_log}" \
      "${unity_executable}" \
      -batchmode \
      -nographics \
      -projectPath "${project_path}" \
      -runTests \
      -testPlatform EditMode \
      -testResults "${test_results}"; then
    :
  else
    editor_exit_code=$?
    if [[ -f "${test_results}" ]]; then
      test_result="$(xmllint --xpath 'string(/test-run/@result)' "${test_results}" 2>/dev/null || true)"
      if [[ "${test_result}" == "Failed" ]]; then
        fail_gate "Unity ${unity_version} EditMode assertion failure"
      fi
    fi
    classify_editor_failure "${test_log}" "${editor_exit_code}" "Unity ${unity_version} EditMode tests"
  fi

  if [[ ! -f "${test_results}" ]]; then
    fail_not_run "Unity ${unity_version} did not produce EditMode test XML"
  fi
  test_result="$(xmllint --xpath 'string(/test-run/@result)' "${test_results}" 2>/dev/null || true)"
  case "${test_result}" in
    Passed)
      ;;
    Failed)
      fail_gate "Unity ${unity_version} EditMode assertion failure"
      ;;
    *)
      fail_not_run "Unity ${unity_version} produced unusable EditMode test XML"
      ;;
  esac
  if grep -q 'CS8785' "${test_log}"; then
    echo "Unity ${unity_version} reported CS8785 while loading or running the analyzer." >&2
    grep 'CS8785' "${test_log}" >&2
    fail_gate "Unity ${unity_version} reported CS8785"
  fi
}

bootstrap_unity_project() {
  local bootstrap_output="$1"

  rm -f "${bootstrap_output}"
  if run_editor "${bootstrap_output}" \
      "${unity_executable}" \
      -batchmode \
      -nographics \
      -projectPath "${project_path}" \
      -executeMethod \
      Rhycol.OpenApiCodeGen.Core.SourceGeneratorDefineSynchronization.SynchronizeActiveBuildTargetForBatchMode; then
    :
  else
    editor_exit_code=$?
    classify_editor_failure "${bootstrap_output}" "${editor_exit_code}" "Unity ${unity_version} bootstrap"
  fi
}

run_source_generator() {
  local generation_output="$1"
  local expected_result="$2"

  rm -f "${generation_output}"
  if run_editor "${generation_output}" \
      "${unity_executable}" \
      -batchmode \
      -nographics \
      -projectPath "${project_path}" \
      -executeMethod \
      Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Editor.SourceGeneratorVerticalGeneration.Generate \
      -quit; then
    :
  else
    editor_exit_code=$?
    classify_editor_failure "${generation_output}" "${editor_exit_code}" "Unity ${unity_version} Source Generator invocation"
  fi

  if ! grep -q \
    'OpenApiCodeGen Unity vertical generation passed with provider SourceGenerator' \
    "${generation_output}"; then
    fail_gate "Unity did not invoke the registered Source Generator provider"
  fi
  if ! grep -Fq "${expected_result}" "${generation_output}"; then
    fail_gate "Source Generator did not report the expected result: ${expected_result}"
  fi
}

get_published_spec_id() {
  local additional_file_suffix=".${analyzer_assembly_name}.additionalfile"
  local mirror_count
  local mirror_path
  local mirror_file_name
  local spec_id
  local authoritative_cache

  if [[ ! -f "${generated_definition}" ]]; then
    fail_gate "generated client definition is missing: ${generated_definition}"
  fi
  if [[ ! -d "${compiler_mirror_directory}" ]]; then
    fail_gate "compiler mirror directory does not exist: ${compiler_mirror_directory}"
  fi

  mirror_count="$(find \
    "${compiler_mirror_directory}" \
    -maxdepth 1 \
    -type f \
    -name "*${additional_file_suffix}" \
    | wc -l \
    | tr -d '[:space:]')"
  if [[ "${mirror_count}" != "1" ]]; then
    fail_gate "expected exactly one compiler mirror, found ${mirror_count}"
  fi

  mirror_path="$(find \
    "${compiler_mirror_directory}" \
    -maxdepth 1 \
    -type f \
    -name "*${additional_file_suffix}")"
  mirror_file_name="$(basename "${mirror_path}")"
  spec_id="${mirror_file_name%"${additional_file_suffix}"}"
  if [[ ! "${spec_id}" =~ ^[0-9a-f]{32}$ ]]; then
    fail_gate "compiler mirror does not have a lower-case Guid-N specId: ${mirror_file_name}"
  fi

  authoritative_cache="${authoritative_cache_root}/${spec_id}/normalized-v2.json"
  if [[ ! -f "${authoritative_cache}" ]]; then
    fail_gate "authoritative normalized-v2 cache is missing: ${authoritative_cache}"
  fi
  if ! cmp -s "${authoritative_cache}" "${mirror_path}"; then
    fail_gate "authoritative cache and compiler mirror differ for specId ${spec_id}"
  fi
  if ! node - "${authoritative_cache}" "${raw_spec}" <<'NODE'
const fs = require('fs');
const crypto = require('crypto');

const [bundlePath, rawPath] = process.argv.slice(2);
const fail = message => { throw new Error(message); };
const bundle = JSON.parse(fs.readFileSync(bundlePath, 'utf8'));
const raw = fs.readFileSync(rawPath);
const decodeNode = node => {
  if (!node || typeof node !== 'object') fail('Normalized node is not an object');
  switch (node.kind) {
    case 'object': {
      if (!Array.isArray(node.properties)) fail('Normalized object properties are missing');
      const value = {};
      for (const property of node.properties) value[property.name] = decodeNode(property.value);
      return value;
    }
    case 'array':
      if (!Array.isArray(node.items)) fail('Normalized array items are missing');
      return node.items.map(decodeNode);
    case 'string': return node.value;
    case 'number': return Number(node.value);
    case 'boolean': return node.value;
    case 'null': return null;
    default: fail(`Unknown normalized node kind: ${node.kind}`);
  }
};
if (bundle.formatVersion !== 2) fail('Bundle formatVersion must be 2');
if (bundle.rootDocumentId !== 'root') fail('Bundle rootDocumentId must be root');
if (!Array.isArray(bundle.documents) || bundle.documents.length !== 2) {
  fail('Bundle must contain root and exactly one local external document');
}
if (!Array.isArray(bundle.referenceEdges) || bundle.referenceEdges.length !== 1) {
  fail('Bundle must contain exactly one resolved reference edge');
}
const root = bundle.documents[0];
if (root.documentId !== 'root' || root.format !== 'json') fail('Invalid root document identity');
if (root.sourcePath !== 'Assets/OpenApiCodeGen/SourceGeneratorVerification/Specs/openapi.json') {
  fail(`Unexpected root sourcePath: ${root.sourcePath}`);
}
const expectedRawHash = crypto.createHash('sha256').update(raw).digest('hex');
if (root.rawSha256 !== expectedRawHash) fail('Root rawSha256 does not match the fixture bytes');
const external = bundle.documents[1];
if (external.format !== 'yaml' || external.documentId !== external.sourcePath ||
    !external.sourcePath.endsWith('/VerificationItem.yaml')) {
  fail('External document is not the expected project-relative YAML document');
}
const edge = bundle.referenceEdges[0];
if (edge.sourceDocumentId !== 'root' || edge.targetDocumentId !== external.documentId ||
    edge.targetPointer !== '') {
  fail('Reference edge does not resolve root to the whole external Schema document');
}
if (edge.sourcePointer !==
    '/paths/~1items/get/responses/200/content/application~1json/schema/items/$ref') {
  fail(`Reference edge sourcePointer is not the array item node: ${edge.sourcePointer}`);
}
const decodedRoot = decodeNode(root.root);
const decodedExternal = decodeNode(external.root);
const sourceSchema = decodedRoot.paths['/items'].get.responses['200']
  .content['application/json'].schema;
if (sourceSchema.type !== 'array' || !sourceSchema.items ||
    sourceSchema.items.$ref !== 'VerificationItem.yaml') {
  fail('Root fixture does not preserve array/items external ref');
}
if (decodedExternal.type !== 'object' || !decodedExternal.properties ||
    !decodedExternal.properties.id) {
  fail('External YAML document is not a bare VerificationItem Schema');
}
if (JSON.stringify(bundle).includes('?') || JSON.stringify(bundle).includes('@')) {
  fail('Bundle contains query or userinfo material');
}
NODE
  then
    fail_gate "authoritative Bundle v2 structure did not pass strict Unity fixture validation"
  fi
  published_authoritative_cache="${authoritative_cache}"
  if ! grep -Fq "${spec_id}" "${generated_definition}"; then
    fail_gate "generated partial does not reference published specId ${spec_id}"
  fi
  if ! grep -Fq 'partial class Api' "${generated_definition}"; then
    fail_gate "generated partial does not declare partial class Api"
  fi
  if ! grep -Fq 'Rhycol.OpenApiCodeGen.Generated' "${generated_definition}"; then
    fail_gate "generated partial does not use the requested namespace"
  fi
  if grep -Eq 'getItems|getUpdatedItems' "${generated_definition}"; then
    fail_gate "generated partial unexpectedly contains the operation used to prove Analyzer output"
  fi

  printf '%s\n' "${spec_id}"
}

assert_test_passed() {
  local test_results="$1"
  local test_name="$2"
  local result
  result="$(xmllint --xpath "string(//test-case[@fullname='${test_name}']/@result)" "${test_results}")"
  if [[ "${result}" != "Passed" ]]; then
    fail_gate "Unity test did not pass: ${test_name} (${result:-missing})"
  fi
}

assert_test_class_ran() {
  local test_results="$1"
  local class_name="$2"
  local count
  count="$(xmllint --xpath "count(//test-case[contains(@fullname, '.${class_name}.')])" "${test_results}")"
  if [[ "${count}" == "0" ]]; then
    fail_gate "Unity test class was not discovered: ${class_name}"
  fi
}

# Provider selection changes a scripting define and therefore requests a
# compilation. Let that domain reload settle before invoking the real provider;
# otherwise Unity can lose the original execute-method request during recompilation.
bootstrap_unity_project "${bootstrap_log}"
run_source_generator \
  "${generation_log}" \
  'Source Generator cache and definition were published; script compilation was requested.'
initial_spec_id="$(get_published_spec_id)"
if ! grep -Fq "Spec ID: ${initial_spec_id}" "${generation_log}"; then
  fail_gate "Initial Source Generator result did not report published specId ${initial_spec_id}"
fi
initial_mirror="${compiler_mirror_directory}/${initial_spec_id}.${analyzer_assembly_name}.additionalfile"
initial_mirror_hash="$(shasum -a 256 "${initial_mirror}" | cut -d ' ' -f 1)"
initial_definition_hash="$(shasum -a 256 "${generated_definition}" | cut -d ' ' -f 1)"

run_unity_tests "${results}" "${log}"
assert_test_passed \
  "${results}" \
  'Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Tests.SourceGeneratorUnityVerificationTests.GeneratedClientIsAvailableToTargetAssembly'
assert_test_passed \
  "${results}" \
  'Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Tests.SourceGeneratorUnityVerificationTests.AnalyzerAndAdditionalFileFollowAsmdefReferenceScope'
assert_test_class_ran "${results}" 'RawJsonNormalizerTests'
assert_test_class_ran "${results}" 'NormalizedSpecCacheServiceTests'

# A second Generate with unchanged input must reuse the same specId and leave
# both the compiler mirror and owned partial byte-identical.
run_source_generator \
  "${unchanged_generation_log}" \
  'Source Generator cache and definition are already current; no script compilation was requested.'
unchanged_spec_id="$(get_published_spec_id)"
if [[ "${unchanged_spec_id}" != "${initial_spec_id}" ]]; then
  fail_gate "Unchanged generation replaced stable specId ${initial_spec_id} with ${unchanged_spec_id}"
fi
if ! grep -Fq "Spec ID: ${initial_spec_id}" "${unchanged_generation_log}"; then
  fail_gate "Unchanged Source Generator result did not report stable specId ${initial_spec_id}"
fi
if [[ "$(shasum -a 256 "${initial_mirror}" | cut -d ' ' -f 1)" != "${initial_mirror_hash}" ]]; then
  fail_gate "Unchanged generation rewrote the compiler mirror content"
fi
if [[ "$(shasum -a 256 "${generated_definition}" | cut -d ' ' -f 1)" != "${initial_definition_hash}" ]]; then
  fail_gate "Unchanged generation rewrote the generated partial content"
fi
if grep -q '] Csc ' "${unchanged_generation_log}"; then
  fail_gate "Unchanged Source Generator invocation unexpectedly ran a C# compiler action"
fi

rm -f "${no_change_log}"
if run_editor "${no_change_log}" \
    "${unity_executable}" \
    -batchmode \
    -nographics \
    -projectPath "${project_path}" \
    -quit; then
  :
else
  editor_exit_code=$?
  classify_editor_failure "${no_change_log}" "${editor_exit_code}" "Unchanged Unity ${unity_version} reopen"
fi
if grep -q '] Csc ' "${no_change_log}"; then
  fail_gate "An unchanged Unity ${unity_version} reopen unexpectedly ran a C# compiler action"
fi

# Change the raw local OpenAPI document, then invoke Generate again. The source
# identity and target definition are unchanged, so the published specId must be
# stable while the normalized cache, mirror, and Analyzer-generated member change.
before_hash="$(shasum -a 256 "${raw_spec}" | cut -d ' ' -f 1)"
perl -0pi -e \
  's/"operationId": "getItems"/"operationId": "getUpdatedItems"/' \
  "${raw_spec}"
after_hash="$(shasum -a 256 "${raw_spec}" | cut -d ' ' -f 1)"
if [[ "${before_hash}" == "${after_hash}" ]]; then
  fail_gate "Failed to change the temporary raw OpenAPI fixture"
fi

run_source_generator \
  "${regeneration_log}" \
  'Source Generator cache and definition were published; script compilation was requested.'
regenerated_spec_id="$(get_published_spec_id)"
if [[ "${regenerated_spec_id}" != "${initial_spec_id}" ]]; then
  fail_gate "Raw input regeneration replaced stable specId ${initial_spec_id} with ${regenerated_spec_id}"
fi
if ! grep -Fq "Spec ID: ${initial_spec_id}" "${regeneration_log}"; then
  fail_gate "Regeneration result did not report stable specId ${initial_spec_id}"
fi
if [[ "$(shasum -a 256 "${initial_mirror}" | cut -d ' ' -f 1)" == "${initial_mirror_hash}" ]]; then
  fail_gate "Raw input regeneration did not update the compiler mirror content"
fi

run_unity_tests "${regeneration_results}" "${regeneration_test_log}"
assert_test_passed \
  "${regeneration_results}" \
  'Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Tests.SourceGeneratorUnityVerificationTests.GeneratedClientIsAvailableToTargetAssembly'

for assembly in \
  'Unity.OpenApiCodeGen.SourceGenerator.Verification.dll' \
  'Unity.OpenApiCodeGen.SourceGenerator.Verification.Tests.dll'; do
  if ! grep -q "] Csc .*${assembly}" \
    "${regeneration_log}" \
    "${regeneration_test_log}"; then
    fail_gate "Changed raw OpenAPI input did not recompile expected assembly: ${assembly}"
  fi
done

for assembly in \
  'Unity.OpenApiCodeGen.SourceGenerator.Verification.Unrelated.dll' \
  'Assembly-CSharp-Editor.dll'; do
  if grep -q "] Csc .*${assembly}" \
    "${regeneration_log}" \
    "${regeneration_test_log}"; then
    fail_gate "Changed raw OpenAPI input recompiled excluded assembly: ${assembly}"
  fi
done

if grep -q 'CS8785' "${regeneration_log}" "${regeneration_test_log}"; then
  fail_gate "Unity ${unity_version} reported CS8785 after raw input regeneration"
fi

# Remove the add-on from inside the running editor. The verification method
# also creates a #error guard while auto-refresh is suspended; compilation can
# succeed only if registeringPackages removes the define first. Library stays
# intact so the old domain and its package-transition subscription are used.
# Remove the vertical-generation fixture before this editor launch so its
# asmdefs can settle while the add-on is still installed. Deleting those assets
# inside the guarded removal flow can independently start compilation on some
# Unity versions and obscure which transition triggered it.
rm -rf "${verification_assets}" "${verification_assets}.meta"
rm -f "${without_addon_bootstrap_log}"
if run_editor "${without_addon_bootstrap_log}" \
    "${unity_executable}" \
    -batchmode \
    -nographics \
    -projectPath "${project_path}" \
    -executeMethod \
    Rhycol.OpenApiCodeGen.SourceGenerator.PackageRemovalVerification.SourceGeneratorPackageRemovalVerification.RemovePackageInLiveEditorDomain; then
  :
else
  editor_exit_code=$?
  classify_editor_failure "${without_addon_bootstrap_log}" "${editor_exit_code}" "Unity ${unity_version} live package removal"
fi
if ! grep -q \
  'OpenApiCodeGen disabled the Source Generator provider and scripting define' \
  "${without_addon_bootstrap_log}"; then
  fail_gate "Unity did not observe the live Source Generator package removal"
fi
if ! grep -q \
  'OpenApiCodeGen live Source Generator package removal verification passed' \
  "${without_addon_bootstrap_log}"; then
  fail_gate "Unity did not complete the live Source Generator package removal"
fi
if grep -Eq \
  'error CS1029:.*Source Generator define was not removed before compilation' \
  "${without_addon_bootstrap_log}"; then
  fail_gate "Unity compiled the package-removal guard before removing the define"
fi
run_unity_tests "${without_addon_results}" "${without_addon_log}"

gate_status="passed"
gate_reason="Source Generator tarball, Bundle v2, regeneration, and removal gates passed"
echo "Unity ${unity_version} source-generator verification passed."
echo "Results: ${results}"
echo "Log: ${log}"
echo "Bootstrap log: ${bootstrap_log}"
echo "Generation log: ${generation_log}"
echo "Unchanged-generation log: ${unchanged_generation_log}"
echo "No-change log: ${no_change_log}"
echo "Regeneration results: ${regeneration_results}"
echo "Regeneration log: ${regeneration_log}"
echo "Regeneration test log: ${regeneration_test_log}"
echo "Without-add-on results: ${without_addon_results}"
echo "Without-add-on log: ${without_addon_log}"
echo "Without-add-on bootstrap log: ${without_addon_bootstrap_log}"
