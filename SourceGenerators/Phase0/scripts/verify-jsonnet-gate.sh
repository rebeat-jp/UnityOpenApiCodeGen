#!/usr/bin/env bash
set -euo pipefail

unity_version="${1:-6000.3.2f1}"
phase0_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repo_root="$(cd "${phase0_root}/../.." && pwd)"
unity="/Applications/Unity/Hub/Editor/${unity_version}/Unity.app/Contents/MacOS/Unity"
fixture="${phase0_root}/JsonNetGateUnityFixture"
gate_assembly="Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.JsonNetGate.dll"

if [[ ! -x "${unity}" ]]; then
  echo "Unity ${unity_version} is not installed at ${unity}." >&2
  exit 2
fi

"${phase0_root}/scripts/build-jsonnet-gate.sh"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Phase0-JsonNetGate.XXXXXX")"
project_path="${temporary_root}/UnityProject"
log_path="${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Phase0-JsonNetGate-${unity_version}.log"
trap 'rm -rf "${temporary_root}"' EXIT

mkdir -p "${project_path}"
rsync -a \
  --exclude '.git' \
  --exclude 'Library' \
  --exclude 'Temp' \
  --exclude 'Logs' \
  --exclude 'obj' \
  --exclude 'bin' \
  "${repo_root}/" "${project_path}/"

case "${unity_version}" in
  6000.0.23f1)
    revision="1c4764c07fb4"
    ;;
  6000.3.2f1)
    revision="a9779f353c9b"
    ;;
  *)
    echo "Add the installed editor revision for ${unity_version} before using this script." >&2
    exit 2
    ;;
esac

printf 'm_EditorVersion: %s\nm_EditorVersionWithRevision: %s (%s)\n' \
  "${unity_version}" "${unity_version}" "${revision}" \
  > "${project_path}/ProjectSettings/ProjectVersion.txt"

if [[ "${unity_version}" == "6000.0.23f1" ]]; then
  printf '%s\n' '{' '  "dependencies": {}' '}' \
    > "${project_path}/Packages/manifest.json"
  rm -f "${project_path}/Packages/packages-lock.json"
fi

analyzers="${project_path}/Packages/OpenApiCodeGen.SourceGenerator.Phase0/Runtime/Analyzers"
runtime="${project_path}/Packages/OpenApiCodeGen.SourceGenerator.Phase0/Runtime"
assets="${project_path}/Assets/SourceGeneratorPhase0"
cp "${phase0_root}/artifacts/jsonnet-gate/${gate_assembly}" "${analyzers}/${gate_assembly}"
cp "${fixture}/${gate_assembly}.meta" "${analyzers}/${gate_assembly}.meta"
cp "${fixture}/JsonNetGateProbe.cs" "${runtime}/JsonNetGateProbe.cs"
cp "${fixture}/JsonNetGateProbe.cs.meta" "${runtime}/JsonNetGateProbe.cs.meta"
cp "${fixture}/Probe.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.JsonNetGate.additionalfile" "${assets}/"
cp "${fixture}/Probe.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.JsonNetGate.additionalfile.meta" "${assets}/"

rm -f "${log_path}"
set +e
"${unity}" \
  -batchmode \
  -nographics \
  -projectPath "${project_path}" \
  -quit \
  -logFile "${log_path}"
unity_status=$?
set -e

if ! grep -q "CS8785: Generator 'JsonNetGateIncrementalGenerator' failed to generate source" "${log_path}" \
  || ! grep -q "FileNotFoundException" "${log_path}" \
  || ! grep -q "Newtonsoft.Json, Version=13.0.0.0" "${log_path}"; then
  echo "Expected Json.NET analyzer dependency failure was not found (Unity status ${unity_status})." >&2
  tail -n 120 "${log_path}" >&2
  exit 1
fi

newtonsoft_package="$(find "${project_path}/Library/PackageCache" -path '*/com.unity.nuget.newtonsoft-json*/package.json' -print -quit)"
if [[ -z "${newtonsoft_package}" ]] \
  || ! grep -q '"version": "3.2.2"' "${newtonsoft_package}" \
  || [[ ! -f "$(dirname "${newtonsoft_package}")/Runtime/Newtonsoft.Json.dll" ]]; then
  echo "Expected com.unity.nuget.newtonsoft-json@3.2.2 was not resolved." >&2
  exit 1
fi

response_file="$(find "${project_path}/Library/Bee/artifacts" -name 'Unity.OpenApiCodeGen.SourceGenerator.Phase0.rsp' -print -quit)"
if [[ -z "${response_file}" || ! -f "${response_file}" ]] \
  || ! grep -q 'com.unity.nuget.newtonsoft-json.*Runtime/Newtonsoft.Json.dll' "${response_file}" \
  || ! grep -q -- "-analyzer:.*${gate_assembly}" "${response_file}"; then
  echo "Compiler response did not contain the expected target reference and analyzer input." >&2
  exit 1
fi

echo "Confirmed Json.NET analyzer dependency gate failure on Unity ${unity_version}."
grep -m 1 "CS8785" "${log_path}"
echo "Log: ${log_path}"
