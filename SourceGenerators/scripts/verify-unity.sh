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

case "${unity_version}" in
  6000.0.23f1)
    unity_revision="1c4764c07fb4"
    ;;
  6000.3.2f1)
    unity_revision="a9779f353c9b"
    ;;
  *)
    echo "Add the installed editor revision for ${unity_version} before using this script." >&2
    exit 2
    ;;
esac

if [[ ! -x "${unity_executable}" ]]; then
  echo "Unity ${unity_version} is not installed at ${unity_executable}." >&2
  exit 2
fi
if ! command -v xmllint >/dev/null 2>&1; then
  echo "xmllint is required to verify Unity test results." >&2
  exit 2
fi

require_file "${package_analyzer}"
require_file "${package_analyzer}.meta"
require_file "${verification_fixture}/Specs/openapi.json"
require_file "${verification_fixture}/Target/GeneratedClientProbe.cs"
require_file "${verification_fixture}/Editor/SourceGeneratorVerticalGeneration.cs"
require_file "${package_removal_fixture}/Editor/SourceGeneratorPackageRemovalVerification.cs"

if [[ "${SOURCE_GENERATOR_SKIP_DOTNET_VERIFY:-0}" != "1" ]]; then
  "${scripts_directory}/verify.sh"
fi

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
cleanup_temporary_root() {
  if [[ "${SOURCE_GENERATOR_KEEP_TEMP:-0}" == "1" ]]; then
    echo "Preserved verification workspace: ${temporary_root}"
    return
  fi

  rm -rf "${temporary_root}"
}
trap cleanup_temporary_root EXIT

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

if [[ "${unity_version}" == "6000.0.23f1" ]]; then
  printf '%s\n' \
    '{' \
    '  "dependencies": {' \
    '    "com.unity.test-framework": "1.4.5"' \
    '  }' \
    '}' \
    > "${project_path}/Packages/manifest.json"
  rm -f "${project_path}/Packages/packages-lock.json"
fi

# Register the add-on as a direct local dependency so the live-removal check
# can use the same Package Manager Client.Remove path as a consumer project.
local_packages="${temporary_root}/LocalPackages"
mkdir -p "${local_packages}"
mv \
  "${project_path}/Packages/OpenApiCodeGen.SourceGenerator" \
  "${local_packages}/OpenApiCodeGen.SourceGenerator"
perl -0pi -e \
  's/"dependencies": \{/"dependencies": {\n    "jp.rhycol.openapicodegen.source-generator": "file:..\/..\/LocalPackages\/OpenApiCodeGen.SourceGenerator",/' \
  "${project_path}/Packages/manifest.json"
if grep -q '"scopedRegistries"' "${project_path}/Packages/manifest.json"; then
  perl -0pi -e \
    's/\n  "scopedRegistries":/\n  "testables": ["jp.rhycol.openapicodegen.source-generator"],\n  "scopedRegistries":/' \
    "${project_path}/Packages/manifest.json"
else
  perl -0pi -e \
    's/\n}$/\n  ,"testables": ["jp.rhycol.openapicodegen.source-generator"]\n}/' \
    "${project_path}/Packages/manifest.json"
fi
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

run_unity_tests() {
  local test_results="$1"
  local test_log="$2"

  rm -f "${test_results}" "${test_log}"
  "${unity_executable}" \
    -batchmode \
    -nographics \
    -projectPath "${project_path}" \
    -runTests \
    -testPlatform EditMode \
    -testResults "${test_results}" \
    -logFile "${test_log}"

  require_file "${test_results}"
  if [[ "$(xmllint --xpath 'string(/test-run/@result)' "${test_results}")" != "Passed" ]]; then
    echo "Unity ${unity_version} EditMode tests did not pass." >&2
    exit 1
  fi
  if grep -q 'CS8785' "${test_log}"; then
    echo "Unity ${unity_version} reported CS8785 while loading or running the analyzer." >&2
    grep 'CS8785' "${test_log}" >&2
    exit 1
  fi
}

bootstrap_unity_project() {
  local bootstrap_output="$1"

  rm -f "${bootstrap_output}"
  if ! "${unity_executable}" \
      -batchmode \
      -nographics \
      -projectPath "${project_path}" \
      -executeMethod \
      Rhycol.OpenApiCodeGen.Core.SourceGeneratorDefineSynchronization.SynchronizeActiveBuildTargetForBatchMode \
      -logFile "${bootstrap_output}"; then
    echo "Unity ${unity_version} bootstrap failed." >&2
    tail -100 "${bootstrap_output}" >&2
    exit 1
  fi
}

run_source_generator() {
  local generation_output="$1"
  local expected_result="$2"

  rm -f "${generation_output}"
  if ! "${unity_executable}" \
      -batchmode \
      -nographics \
      -projectPath "${project_path}" \
      -executeMethod \
      Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Editor.SourceGeneratorVerticalGeneration.Generate \
      -quit \
      -logFile "${generation_output}"; then
    echo "Unity ${unity_version} Source Generator invocation failed." >&2
    tail -100 "${generation_output}" >&2
    exit 1
  fi

  if ! grep -q \
    'OpenApiCodeGen Unity vertical generation passed with provider SourceGenerator' \
    "${generation_output}"; then
    echo "Unity did not invoke the registered Source Generator provider." >&2
    exit 1
  fi
  if ! grep -Fq "${expected_result}" "${generation_output}"; then
    echo "Source Generator did not report the expected result: ${expected_result}" >&2
    exit 1
  fi
}

get_published_spec_id() {
  local additional_file_suffix=".${analyzer_assembly_name}.additionalfile"
  local mirror_count
  local mirror_path
  local mirror_file_name
  local spec_id
  local authoritative_cache

  require_file "${generated_definition}"
  if [[ ! -d "${compiler_mirror_directory}" ]]; then
    echo "Compiler mirror directory does not exist: ${compiler_mirror_directory}" >&2
    exit 1
  fi

  mirror_count="$(find \
    "${compiler_mirror_directory}" \
    -maxdepth 1 \
    -type f \
    -name "*${additional_file_suffix}" \
    | wc -l \
    | tr -d '[:space:]')"
  if [[ "${mirror_count}" != "1" ]]; then
    echo "Expected exactly one compiler mirror, found ${mirror_count}." >&2
    exit 1
  fi

  mirror_path="$(find \
    "${compiler_mirror_directory}" \
    -maxdepth 1 \
    -type f \
    -name "*${additional_file_suffix}")"
  mirror_file_name="$(basename "${mirror_path}")"
  spec_id="${mirror_file_name%"${additional_file_suffix}"}"
  if [[ ! "${spec_id}" =~ ^[0-9a-f]{32}$ ]]; then
    echo "Compiler mirror does not have a lower-case Guid-N specId: ${mirror_file_name}" >&2
    exit 1
  fi

  authoritative_cache="${authoritative_cache_root}/${spec_id}/normalized-v1.json"
  require_file "${authoritative_cache}"
  if ! cmp -s "${authoritative_cache}" "${mirror_path}"; then
    echo "Authoritative cache and compiler mirror differ for specId ${spec_id}." >&2
    exit 1
  fi
  if ! grep -Fq "${spec_id}" "${generated_definition}"; then
    echo "Generated partial does not reference published specId ${spec_id}." >&2
    exit 1
  fi
  if ! grep -Fq 'partial class Api' "${generated_definition}"; then
    echo "Generated partial does not declare partial class Api." >&2
    exit 1
  fi
  if ! grep -Fq 'Rhycol.OpenApiCodeGen.Generated' "${generated_definition}"; then
    echo "Generated partial does not use the requested namespace." >&2
    exit 1
  fi
  if grep -Eq 'getItems|getUpdatedItems' "${generated_definition}"; then
    echo "Generated partial unexpectedly contains the operation used to prove Analyzer output." >&2
    exit 1
  fi

  printf '%s\n' "${spec_id}"
}

assert_test_passed() {
  local test_results="$1"
  local test_name="$2"
  local result
  result="$(xmllint --xpath "string(//test-case[@fullname='${test_name}']/@result)" "${test_results}")"
  if [[ "${result}" != "Passed" ]]; then
    echo "Unity test did not pass: ${test_name} (${result:-missing})" >&2
    exit 1
  fi
}

assert_test_class_ran() {
  local test_results="$1"
  local class_name="$2"
  local count
  count="$(xmllint --xpath "count(//test-case[contains(@fullname, '.${class_name}.')])" "${test_results}")"
  if [[ "${count}" == "0" ]]; then
    echo "Unity test class was not discovered: ${class_name}" >&2
    exit 1
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
  echo "Initial Source Generator result did not report published specId ${initial_spec_id}." >&2
  exit 1
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
  echo "Unchanged generation replaced stable specId ${initial_spec_id} with ${unchanged_spec_id}." >&2
  exit 1
fi
if ! grep -Fq "Spec ID: ${initial_spec_id}" "${unchanged_generation_log}"; then
  echo "Unchanged Source Generator result did not report stable specId ${initial_spec_id}." >&2
  exit 1
fi
if [[ "$(shasum -a 256 "${initial_mirror}" | cut -d ' ' -f 1)" != "${initial_mirror_hash}" ]]; then
  echo "Unchanged generation rewrote the compiler mirror content." >&2
  exit 1
fi
if [[ "$(shasum -a 256 "${generated_definition}" | cut -d ' ' -f 1)" != "${initial_definition_hash}" ]]; then
  echo "Unchanged generation rewrote the generated partial content." >&2
  exit 1
fi
if grep -q '] Csc ' "${unchanged_generation_log}"; then
  echo "Unchanged Source Generator invocation unexpectedly ran a C# compiler action." >&2
  grep '] Csc ' "${unchanged_generation_log}" >&2
  exit 1
fi

rm -f "${no_change_log}"
"${unity_executable}" \
  -batchmode \
  -nographics \
  -projectPath "${project_path}" \
  -quit \
  -logFile "${no_change_log}"
if grep -q '] Csc ' "${no_change_log}"; then
  echo "An unchanged Unity ${unity_version} reopen unexpectedly ran a C# compiler action." >&2
  grep '] Csc ' "${no_change_log}" >&2
  exit 1
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
  echo "Failed to change the temporary raw OpenAPI fixture." >&2
  exit 1
fi

run_source_generator \
  "${regeneration_log}" \
  'Source Generator cache and definition were published; script compilation was requested.'
regenerated_spec_id="$(get_published_spec_id)"
if [[ "${regenerated_spec_id}" != "${initial_spec_id}" ]]; then
  echo "Raw input regeneration replaced stable specId ${initial_spec_id} with ${regenerated_spec_id}." >&2
  exit 1
fi
if ! grep -Fq "Spec ID: ${initial_spec_id}" "${regeneration_log}"; then
  echo "Regeneration result did not report stable specId ${initial_spec_id}." >&2
  exit 1
fi
if [[ "$(shasum -a 256 "${initial_mirror}" | cut -d ' ' -f 1)" == "${initial_mirror_hash}" ]]; then
  echo "Raw input regeneration did not update the compiler mirror content." >&2
  exit 1
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
    echo "Changed raw OpenAPI input did not recompile expected assembly: ${assembly}" >&2
    exit 1
  fi
done

for assembly in \
  'Unity.OpenApiCodeGen.SourceGenerator.Verification.Unrelated.dll' \
  'Assembly-CSharp-Editor.dll'; do
  if grep -q "] Csc .*${assembly}" \
    "${regeneration_log}" \
    "${regeneration_test_log}"; then
    echo "Changed raw OpenAPI input recompiled excluded assembly: ${assembly}" >&2
    exit 1
  fi
done

if grep -q 'CS8785' "${regeneration_log}" "${regeneration_test_log}"; then
  echo "Unity ${unity_version} reported CS8785 after raw input regeneration." >&2
  grep -h 'CS8785' "${regeneration_log}" "${regeneration_test_log}" >&2
  exit 1
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
"${unity_executable}" \
  -batchmode \
  -nographics \
  -projectPath "${project_path}" \
  -executeMethod \
  Rhycol.OpenApiCodeGen.SourceGenerator.PackageRemovalVerification.SourceGeneratorPackageRemovalVerification.RemovePackageInLiveEditorDomain \
  -logFile "${without_addon_bootstrap_log}"
if ! grep -q \
  'OpenApiCodeGen disabled the Source Generator provider and scripting define' \
  "${without_addon_bootstrap_log}"; then
  echo "Unity did not observe the live Source Generator package removal." >&2
  exit 1
fi
if ! grep -q \
  'OpenApiCodeGen live Source Generator package removal verification passed' \
  "${without_addon_bootstrap_log}"; then
  echo "Unity did not complete the live Source Generator package removal." >&2
  exit 1
fi
if grep -Eq \
  'error CS1029:.*Source Generator define was not removed before compilation' \
  "${without_addon_bootstrap_log}"; then
  echo "Unity compiled the package-removal guard before removing the define." >&2
  exit 1
fi
run_unity_tests "${without_addon_results}" "${without_addon_log}"

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
