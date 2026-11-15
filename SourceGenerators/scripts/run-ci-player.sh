#!/usr/bin/env bash
set -euo pipefail
# Container entrypoint of the manual Player gate (source-generator-player.yml). It runs only inside the
# disposable GameCI container: the Unity license is activated here, the Player gate runs, and the license is
# returned. No license or credential file is written to the mounted checkout, and the evidence logs are redacted.
#
# Usage: run-ci-player.sh <unity-version> <cells> <phase>
#   cells: standalone | webgl | all | a comma-separated list of cell ids (see verify-player.js --list)
#   phase: all | build | run
scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly scripts_directory
readonly version="${1:?Unity version required}"
readonly cells="${2:?Cells required}"
readonly phase="${3:?Phase required}"
readonly output_root="${PLAYER_GATE_OUTPUT:?Player gate output directory required}"
readonly work_directory="${PLAYER_GATE_WORK:-/tmp/player-gate-work}"
readonly activation_timeout_seconds=300
readonly return_timeout_seconds=120
private_directory="$(mktemp -d /tmp/unity-auth.XXXXXX)"
readonly private_directory
umask 077
mkdir -p "${private_directory}/BlankProject/Assets" "${output_root}"
activated=false
export UNITY_EXECUTABLE=/opt/unity/Editor/Unity

# shellcheck disable=SC2329 # Invoked by the EXIT trap.
cleanup() {
  local result=$?
  trap - EXIT
  if [[ "${activated}" == true ]]; then
    if ! perl "${scripts_directory}/run-with-timeout.pl" "${return_timeout_seconds}" \
        "${UNITY_EXECUTABLE}" -batchmode -nographics -quit \
        -returnlicense -username "${UNITY_EMAIL}" -password "${UNITY_PASSWORD}" \
        -projectPath "${private_directory}/BlankProject" \
        -logFile "${private_directory}/return.log" >"${private_directory}/return-console.log" 2>&1; then
      echo '::warning::Unity license return did not complete; disposable container is being removed.'
    fi
    rm -rf /root/.local/share/unity3d/Unity /root/.config/unity3d/Unity || true
  fi
  # Redact before the logs leave the container (the mounted checkout is uploaded as an artifact).
  if [[ -d "${output_root}" ]]; then
    while IFS= read -r -d '' log_file; do
      node "${scripts_directory}/ci-evidence.js" sanitize-file "${log_file}" "${log_file}" || rm -f "${log_file}"
    done < <(find "${output_root}" -type f \( -name '*.log' -o -name '*.json' \) -print0)
  fi
  rm -rf "${private_directory}" || true
  exit "${result}"
}
trap cleanup EXIT

if [[ "${phase}" != run ]]; then
  for secret_name in UNITY_LICENSE UNITY_EMAIL UNITY_PASSWORD; do
    if [[ -z "${!secret_name:-}" ]]; then
      echo "::error::${secret_name} is missing from Environment UNITY_LICENSE"
      exit 2
    fi
  done
  if ! UNITY_SERIAL="$(node "${scripts_directory}/unity-license.js")"; then
    echo '::error::Personal license parsing failed; confirm UNITY_LICENSE contains the complete ULF file'
    exit 2
  fi
  export UNITY_SERIAL
  dbus-uuidgen > /etc/machine-id
  mkdir -p /var/lib/dbus
  ln -sf /etc/machine-id /var/lib/dbus/machine-id
  if ! perl "${scripts_directory}/run-with-timeout.pl" "${activation_timeout_seconds}" \
      "${UNITY_EXECUTABLE}" -batchmode -nographics -quit \
      -serial "${UNITY_SERIAL}" -username "${UNITY_EMAIL}" -password "${UNITY_PASSWORD}" \
      -projectPath "${private_directory}/BlankProject" \
      -logFile "${private_directory}/activation.log" >"${private_directory}/activation-console.log" 2>&1; then
    echo '::error::Unity Personal activation failed or timed out; verify Unity credentials and license eligibility'
    exit 2
  fi
  activated=true
fi

status=0
node "${scripts_directory}/verify-player.js" \
  --unity "${version}" --cells "${cells}" --phase "${phase}" \
  --work-dir "${work_directory}" --output "${output_root}" || status=$?
exit "${status}"
