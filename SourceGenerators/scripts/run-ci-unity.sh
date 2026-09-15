#!/usr/bin/env bash
set -euo pipefail
# This runs only inside the disposable GameCI container. No license/credential
# files or raw activation/console logs are written to the mounted checkout.
scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly scripts_directory
readonly version="${1:?Unity version required}"
readonly release_output="${SOURCE_GENERATOR_RELEASE_OUTPUT:?Candidate output required}"
private_directory="$(mktemp -d /tmp/unity-auth.XXXXXX)"
readonly private_directory
readonly evidence_directory="${release_output}/unity-gate/${version}"
umask 077
mkdir -p "${private_directory}/BlankProject/Assets" "${evidence_directory}"
activated=false
export UNITY_EXECUTABLE=/opt/unity/Editor/Unity
# shellcheck disable=SC2329 # Invoked by the EXIT trap.
cleanup() {
  local result=$?
  trap - EXIT
  if [[ "${activated}" == true ]]; then
    if ! timeout 120 "${UNITY_EXECUTABLE}" -batchmode -nographics -quit \
        -returnlicense -username "${UNITY_EMAIL}" -password "${UNITY_PASSWORD}" \
        -projectPath "${private_directory}/BlankProject" \
        -logFile "${private_directory}/return.log" >"${private_directory}/return-console.log" 2>&1; then
      echo '::warning::Unity license return did not complete; disposable container is being removed.'
    fi
  fi
  # Cleanup must not override the original gate result. License paths are only
  # touched after actual activation inside the disposable root-owned container.
  if [[ "${activated}" == true ]]; then
    rm -rf /root/.local/share/unity3d/Unity /root/.config/unity3d/Unity || true
  fi
  rm -rf "${private_directory}" || true
  exit "${result}"
}
trap cleanup EXIT
failure_evidence() {
  local reason="$1"
  node "${scripts_directory}/ci-evidence.js" write-gate \
    "${evidence_directory}/gate.json" not-run 2 "${version}" "${reason}" \
    "${evidence_directory}" "${release_output}"
  echo "::error::${reason}"
  exit 2
}
for secret_name in UNITY_LICENSE UNITY_EMAIL UNITY_PASSWORD; do
  if [[ -z "${!secret_name:-}" ]]; then failure_evidence "${secret_name} is missing from Environment UNITY_LICENSE"; fi
 done
if ! UNITY_SERIAL="$(node "${scripts_directory}/unity-license.js")"; then
  failure_evidence 'Personal license parsing failed; confirm UNITY_LICENSE contains the complete ULF file'
fi
export UNITY_SERIAL
# Do not echo the decoded serial: console masking does not protect artifacts.
dbus-uuidgen > /etc/machine-id
mkdir -p /var/lib/dbus
ln -sf /etc/machine-id /var/lib/dbus/machine-id
if ! timeout 300 "${UNITY_EXECUTABLE}" -batchmode -nographics -quit \
    -serial "${UNITY_SERIAL}" -username "${UNITY_EMAIL}" -password "${UNITY_PASSWORD}" \
    -projectPath "${private_directory}/BlankProject" \
    -logFile "${private_directory}/activation.log" >"${private_directory}/activation-console.log" 2>&1; then
  failure_evidence 'Unity Personal activation failed or timed out; verify Unity credentials and license eligibility'
fi
activated=true
case "${version}" in
  2021.3.19f1) gate_script=verify-base-unity.sh ;;
  6000.0.23f1|6000.3.2f1) gate_script=verify-unity.sh ;;
  *) failure_evidence 'Unsupported Unity version' ;;
esac
status=0
SOURCE_GENERATOR_SKIP_DOTNET_VERIFY=1 SOURCE_GENERATOR_EVIDENCE_ROOT="${evidence_directory}" \
  "${scripts_directory}/${gate_script}" "${version}" >"${private_directory}/gate-console.log" 2>&1 || status=$?
node - "${private_directory}/gate-console.log" "${scripts_directory}/ci-evidence.js" <<'NODE'
const fs = require('fs');
const { redact } = require(process.argv[3]);
process.stdout.write(redact(fs.readFileSync(process.argv[2], 'utf8')));
NODE
exit "${status}"
