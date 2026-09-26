#!/usr/bin/env bash
set -euo pipefail
# This runs only inside the disposable GameCI container. No license/credential
# files or raw activation/console logs are written to the mounted checkout.
scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly scripts_directory
# shellcheck source=ci-time-budget.sh
source "${scripts_directory}/ci-time-budget.sh"
readonly version="${1:?Unity version required}"
readonly release_output="${SOURCE_GENERATOR_RELEASE_OUTPUT:?Candidate output required}"
private_directory="$(mktemp -d /tmp/unity-auth.XXXXXX)"
readonly private_directory
readonly evidence_directory="${release_output}/unity-gate/${version}"
umask 077
mkdir -p "${private_directory}/BlankProject/Assets" "${evidence_directory}"
activated=false
gate_pid=""
export UNITY_EXECUTABLE=/opt/unity/Editor/Unity
export SOURCE_GENERATOR_UNITY_TIMEOUT_SECONDS="${CI_EDITOR_TIMEOUT_SECONDS}"
# shellcheck disable=SC2329 # Invoked by the EXIT trap.
cleanup() {
  local result=$?
  trap - EXIT
  if [[ "${activated}" == true ]]; then
    if ! perl "${scripts_directory}/run-with-timeout.pl" "${CI_LICENSE_RETURN_TIMEOUT_SECONDS}" \
        "${UNITY_EXECUTABLE}" -batchmode -nographics -quit \
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
emit_gate_console() {
  if [[ -f "${private_directory}/gate-console.log" ]]; then
    perl "${scripts_directory}/run-with-timeout.pl" "${CI_EVIDENCE_TIMEOUT_SECONDS}" \
      node - "${private_directory}/gate-console.log" "${scripts_directory}/ci-evidence.js" <<'NODE'
const fs = require('fs');
const { redact } = require(process.argv[3]);
process.stdout.write(redact(fs.readFileSync(process.argv[2], 'utf8')));
NODE
  fi
}
# shellcheck disable=SC2329 # Invoked by the TERM/INT traps.
terminate_gate() {
  trap - TERM INT
  if [[ -n "${gate_pid}" ]]; then
    kill -TERM -- "-${gate_pid}" 2>/dev/null || kill -TERM "${gate_pid}" 2>/dev/null || true
    gate_stop_deadline=$(( $(ci_now_epoch) + CI_GATE_STOP_TIMEOUT_SECONDS ))
    while kill -0 "${gate_pid}" 2>/dev/null &&
          (( $(ci_remaining_seconds "${gate_stop_deadline}") > 0 )); do
      sleep 1
    done
    if kill -0 "${gate_pid}" 2>/dev/null; then
      kill -KILL -- "-${gate_pid}" 2>/dev/null || kill -KILL "${gate_pid}" 2>/dev/null || true
    fi
    wait "${gate_pid}" 2>/dev/null || true
  fi
  emit_gate_console || echo '::warning::Partial Unity gate console evidence could not be emitted within its time limit.'
  exit 143
}
trap terminate_gate TERM INT
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
if ! perl "${scripts_directory}/run-with-timeout.pl" "${CI_ACTIVATION_TIMEOUT_SECONDS}" \
    "${UNITY_EXECUTABLE}" -batchmode -nographics -quit \
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
setsid env SOURCE_GENERATOR_SKIP_DOTNET_VERIFY=1 SOURCE_GENERATOR_EVIDENCE_ROOT="${evidence_directory}" \
  "${scripts_directory}/${gate_script}" "${version}" >"${private_directory}/gate-console.log" 2>&1 &
gate_pid=$!
wait "${gate_pid}" || status=$?
gate_pid=""
emit_gate_console
exit "${status}"
