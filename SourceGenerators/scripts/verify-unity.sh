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
normalized_bundle_fixture="${source_generators_root}/Rhycol.OpenApiCodeGen.SourceGenerator.Tests/TestAssets/unity-minimal.normalized-v1.json"
additional_file_name="0123456789abcdef0123456789abcdef.${analyzer_assembly_name}.additionalfile"

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
require_file "${normalized_bundle_fixture}"
require_file "${verification_fixture}/Target/GeneratedClientProbe.cs"
require_file "${package_removal_fixture}/Editor/SourceGeneratorPackageRemovalVerification.cs"

if [[ "${SOURCE_GENERATOR_SKIP_DOTNET_VERIFY:-0}" != "1" ]]; then
  "${scripts_directory}/verify.sh"
fi

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/UnityOpenApiCodeGen-SourceGenerator-${unity_version}.XXXXXX")"
project_path="${temporary_root}/UnityProject"
results="${temporary_root}/source-generator-results.xml"
log="${temporary_root}/source-generator.log"
bootstrap_log="${temporary_root}/source-generator-bootstrap.log"
no_change_log="${temporary_root}/source-generator-no-change.log"
changed_log="${temporary_root}/source-generator-changed.log"
without_addon_results="${temporary_root}/without-addon-results.xml"
without_addon_log="${temporary_root}/without-addon.log"
without_addon_bootstrap_log="${temporary_root}/without-addon-bootstrap.log"
trap 'rm -rf "${temporary_root}"' EXIT

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
additional_file="${verification_assets}/Target/${additional_file_name}"
cp "${normalized_bundle_fixture}" "${additional_file}"

# Exercise the Phase 2 provider/define path. The add-on run must enable the
# active target define, while the final base-only run must remove it without
# falling back to Docker.
printf '%s\n' \
  '{"GenerateProvider":1,"ApiDocumentFilePathOrUrl":"","ApiClientOutputFolderPath":""}' \
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
# compilation. Let that domain reload settle before starting the test runner;
# otherwise Unity can lose the original -runTests request during recompilation.
bootstrap_unity_project "${bootstrap_log}"
run_unity_tests "${results}" "${log}"
assert_test_passed \
  "${results}" \
  'Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Tests.SourceGeneratorUnityVerificationTests.GeneratedClientIsAvailableToTargetAssembly'
assert_test_passed \
  "${results}" \
  'Rhycol.OpenApiCodeGen.SourceGenerator.Verification.Tests.SourceGeneratorUnityVerificationTests.AnalyzerAndAdditionalFileFollowAsmdefReferenceScope'
assert_test_class_ran "${results}" 'RawJsonNormalizerTests'
assert_test_class_ran "${results}" 'NormalizedSpecCacheServiceTests'

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

before_hash="$(shasum -a 256 "${additional_file}" | cut -d ' ' -f 1)"
perl -0pi -e \
  'if (/"rawSha256": "([0-9a-f])/) { my $r = $1 eq "f" ? "e" : "f"; s/("rawSha256": ")[0-9a-f]/$1$r/; }' \
  "${additional_file}"
after_hash="$(shasum -a 256 "${additional_file}" | cut -d ' ' -f 1)"
if [[ "${before_hash}" == "${after_hash}" ]]; then
  echo "Failed to change the temporary normalized bundle fixture." >&2
  exit 1
fi

rm -f "${changed_log}"
"${unity_executable}" \
  -batchmode \
  -nographics \
  -projectPath "${project_path}" \
  -quit \
  -logFile "${changed_log}"

for assembly in \
  'Unity.OpenApiCodeGen.SourceGenerator.Verification.dll' \
  'Unity.OpenApiCodeGen.SourceGenerator.Verification.Tests.dll'; do
  if ! grep -q "] Csc .*${assembly}" "${changed_log}"; then
    echo "Changed AdditionalFile did not recompile expected assembly: ${assembly}" >&2
    exit 1
  fi
done

for assembly in \
  'Unity.OpenApiCodeGen.SourceGenerator.Verification.Unrelated.dll' \
  'Assembly-CSharp-Editor.dll'; do
  if grep -q "] Csc .*${assembly}" "${changed_log}"; then
    echo "Changed AdditionalFile recompiled excluded assembly: ${assembly}" >&2
    exit 1
  fi
done

if grep -q 'CS8785' "${changed_log}"; then
  echo "Unity ${unity_version} reported CS8785 after the AdditionalFile change." >&2
  grep 'CS8785' "${changed_log}" >&2
  exit 1
fi

# Remove the add-on from inside the running editor. The verification method
# also creates a #error guard while auto-refresh is suspended; compilation can
# succeed only if registeringPackages removes the define first. Library stays
# intact so the old domain and its package-transition subscription are used.
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
echo "No-change log: ${no_change_log}"
echo "Changed-input log: ${changed_log}"
echo "Without-add-on results: ${without_addon_results}"
echo "Without-add-on log: ${without_addon_log}"
echo "Without-add-on bootstrap log: ${without_addon_bootstrap_log}"
