#!/usr/bin/env bash

# Host-side cloud gate controller. The 210-minute work deadline is deliberately
# shorter than the workflow job timeout so license return and container cleanup
# still have bounded time after a gate is stopped.
set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=ci-time-budget.sh
source "${scripts_directory}/ci-time-budget.sh"

readonly release_output="${SOURCE_GENERATOR_RELEASE_OUTPUT:?Candidate output required}"
readonly release_version="${SOURCE_GENERATOR_RELEASE_VERSION:?Candidate version required}"
readonly workspace="${GITHUB_WORKSPACE:?GitHub workspace required}"
readonly started_epoch="${SOURCE_GENERATOR_CI_STARTED_AT_EPOCH:?Unity job start time required}"
if [[ ! "${started_epoch}" =~ ^[1-9][0-9]*$ ]]; then
  echo 'Unity job start time must be a positive epoch value.' >&2
  exit 2
fi
readonly deadline_epoch=$((started_epoch + CI_TOTAL_BUDGET_SECONDS))
readonly versions=(2021.3.19f1 6000.0.23f1 6000.3.2f1)
temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/OpenApiCodeGen-CI-Matrix.XXXXXX")"
readonly temporary_root
trap 'rm -rf "${temporary_root}"' EXIT

failed=0
not_run=0
container_starts=0

host_log_path() {
  printf '%s/%s-host.log\n' "${temporary_root}" "$1"
}

record_gate() {
  local version="$1"
  local policy="$2"
  local status="$3"
  local exit_code="$4"
  local reason="$5"
  local host_log="$6"
  local evidence_root="${release_output}/unity-gate/${version}"

  mkdir -p "${evidence_root}"
  if [[ "${policy}" == preserve && -f "${evidence_root}/gate.json" ]]; then
    printf 'Finalizing gate evidence written by the Unity container.\n' >> "${host_log}"
  else
    printf '%s\n' "${reason}" >> "${host_log}"
  fi
  node "${scripts_directory}/ci-evidence.js" finalize-gate \
    "${host_log}" "${evidence_root}" "${release_output}" "${version}" \
    "${policy}" "${status}" "${exit_code}" "${reason}"
}

mark_not_started() {
  local start_index="$1"
  local reason="$2"
  local index
  local version
  local host_log
  for ((index = start_index; index < ${#versions[@]}; index += 1)); do
    version="${versions[index]}"
    host_log="$(host_log_path "${version}")"
    record_gate "${version}" replace not-run 2 "${reason}; Unity ${version} was not started" "${host_log}"
    not_run=1
  done
}

run_with_alarm() {
  local seconds="$1"
  local output_log="$2"
  shift 2
  perl "${scripts_directory}/run-with-timeout.pl" "${seconds}" "$@" >> "${output_log}" 2>&1
}

bounded_cleanup() {
  local gate_version="$1"
  local container_name="$2"
  local image="$3"
  local digest="$4"
  local output_log="$5"
  local cleanup_deadline=$(( $(ci_now_epoch) + CI_CLEANUP_TIMEOUT_SECONDS ))
  local remaining

  for cleanup_command in ownership container image builder; do
    remaining="$(ci_remaining_seconds "${cleanup_deadline}")"
    if (( remaining == 0 )); then
      printf 'Cleanup deadline reached before %s cleanup.\n' "${cleanup_command}" >> "${output_log}"
      break
    fi
    case "${cleanup_command}" in
      ownership) run_with_alarm "${remaining}" "${output_log}" sudo chown -R "$(id -u):$(id -g)" "${release_output}" || true ;;
      container) run_with_alarm "${remaining}" "${output_log}" docker rm --force "${container_name}" || true ;;
      image) run_with_alarm "${remaining}" "${output_log}" docker image rm "${image}" "unityci/editor:ubuntu-${gate_version}-base-3@${digest}" || true ;;
      builder) run_with_alarm "${remaining}" "${output_log}" docker builder prune --force || true ;;
    esac
  done
}

terminate_container() {
  local container_name="$1"
  local docker_pid="$2"
  local output_log="$3"
  local cleanup_deadline=$(( $(ci_now_epoch) + CI_CLEANUP_TIMEOUT_SECONDS ))
  local remaining
  local term_limit
  local term_status=0

  printf 'Work deadline reached; sending TERM to container %s.\n' "${container_name}" >> "${output_log}"
  remaining="$(ci_remaining_seconds "${cleanup_deadline}")"
  if (( remaining > 1 )); then term_limit=$(( remaining / 2 )); else term_limit=1; fi
  run_with_alarm "${term_limit}" "${output_log}" docker kill --signal TERM "${container_name}" || term_status=$?
  while (( term_status == 0 )) && kill -0 "${docker_pid}" 2>/dev/null; do
    remaining="$(ci_remaining_seconds "${cleanup_deadline}")"
    if (( remaining <= 2 )); then break; fi
    sleep 1
  done
  if kill -0 "${docker_pid}" 2>/dev/null; then
    printf 'Container did not stop after TERM; sending KILL.\n' >> "${output_log}"
    remaining="$(ci_remaining_seconds "${cleanup_deadline}")"
    if (( remaining > 0 )); then
      run_with_alarm "${remaining}" "${output_log}" docker kill --signal KILL "${container_name}" || true
    else
      printf 'Cleanup deadline reached before the Docker KILL command completed.\n' >> "${output_log}"
    fi
    kill -KILL "${docker_pid}" 2>/dev/null || true
  fi
  wait "${docker_pid}" 2>/dev/null || true
}

run_container_until_deadline() {
  local version="$1"
  local image="$2"
  local container_name="$3"
  local output_log="$4"
  local docker_pid

  docker run --rm --name "${container_name}" --shm-size=2g \
    --volume "${workspace}:/workspace" \
    --env UNITY_LICENSE --env UNITY_EMAIL --env UNITY_PASSWORD \
    --env SOURCE_GENERATOR_CI --env SOURCE_GENERATOR_CI_COMMIT --env SOURCE_GENERATOR_CANDIDATE_ATTEMPT \
    --env GITHUB_REPOSITORY --env GITHUB_RUN_ID --env GITHUB_RUN_ATTEMPT \
    --env "SOURCE_GENERATOR_RELEASE_OUTPUT=/workspace/artifacts/upm/${release_version}" \
    "${image}" "${version}" >> "${output_log}" 2>&1 &
  docker_pid=$!
  while kill -0 "${docker_pid}" 2>/dev/null; do
    if (( $(ci_remaining_seconds "${deadline_epoch}") == 0 )); then
      terminate_container "${container_name}" "${docker_pid}" "${output_log}"
      return 124
    fi
    sleep 1
  done
  wait "${docker_pid}"
}

for secret_name in UNITY_LICENSE UNITY_EMAIL UNITY_PASSWORD; do
  if [[ -z "${!secret_name:-}" ]]; then
    mark_not_started 0 'Unity authentication Secrets are unavailable; check Environment UNITY_LICENSE and caller secret inheritance'
    echo '::error::Required Unity authentication Secrets are unavailable; no Unity images were downloaded.'
    exit 2
  fi
done

for ((index = 0; index < ${#versions[@]}; index += 1)); do
  version="${versions[index]}"
  host_log="$(host_log_path "${version}")"
  image="openapi-unity-gate:${version}"

  digest=""
  if ! digest="$(node -p "require('./SourceGenerators/BuildTools/CI/unity-images.json')['${version}']" 2>> "${host_log}")" ||
      [[ ! "${digest}" =~ ^sha256:[0-9a-f]{64}$ ]]; then
    record_gate "${version}" replace failed 1 \
      "Unity ${version} image digest could not be loaded from the trusted image map" "${host_log}"
    failed=1
    continue
  fi

  remaining="$(ci_remaining_seconds "${deadline_epoch}")"
  if (( remaining == 0 )); then
    mark_not_started "${index}" 'Unity matrix work deadline reached'
    break
  fi

  build_limit="$(ci_min_seconds "${CI_DOCKER_BUILD_TIMEOUT_SECONDS}" "${remaining}")"
  printf 'Building Unity %s image with a %s-second limit.\n' "${version}" "${build_limit}" >> "${host_log}"
  build_status=0
  run_with_alarm "${build_limit}" "${host_log}" docker build \
    --build-arg "UNITY_VERSION=${version}" --build-arg "UNITY_IMAGE_DIGEST=${digest}" \
    --build-arg "NODE_VERSION=$(cat .node-version)" \
    --tag "${image}" SourceGenerators/BuildTools/CI || build_status=$?
  if (( build_status != 0 )); then
    bounded_cleanup "${version}" "source-generator-unity-unused" "${image}" "${digest}" "${host_log}"
    record_gate "${version}" replace failed 1 \
      "Unity ${version} image build failed or timed out after ${build_limit} seconds" "${host_log}"
    failed=1
    continue
  fi

  remaining="$(ci_remaining_seconds "${deadline_epoch}")"
  if (( remaining == 0 )); then
    bounded_cleanup "${version}" "source-generator-unity-unused" "${image}" "${digest}" "${host_log}"
    record_gate "${version}" replace failed 1 \
      "Unity matrix work deadline reached after building ${version}; its container was not started" "${host_log}"
    failed=1
    mark_not_started "$((index + 1))" 'Unity matrix work deadline reached'
    break
  fi
  if (( container_starts >= CI_MAX_CONTAINER_STARTS )); then
    bounded_cleanup "${version}" "source-generator-unity-unused" "${image}" "${digest}" "${host_log}"
    record_gate "${version}" replace failed 1 'Unity container start limit reached for the current gate' "${host_log}"
    failed=1
    mark_not_started "$((index + 1))" 'Unity container start limit reached'
    break
  fi

  container_starts=$((container_starts + 1))
  container_name="source-generator-unity-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}-${version//./-}"
  printf 'Starting Unity %s container %s of %s with %s seconds remaining.\n' \
    "${version}" "${container_starts}" "${CI_MAX_CONTAINER_STARTS}" "${remaining}" >> "${host_log}"
  container_status=0
  run_container_until_deadline "${version}" "${image}" "${container_name}" "${host_log}" || container_status=$?
  bounded_cleanup "${version}" "${container_name}" "${image}" "${digest}" "${host_log}"
  if (( container_status == 124 )); then
    record_gate "${version}" replace failed 1 \
      "Unity ${version} container exceeded the matrix work deadline and was terminated" "${host_log}"
    failed=1
  elif (( container_status != 0 )); then
    record_gate "${version}" replace failed 1 \
      "Unity ${version} container exited ${container_status} before writing usable gate evidence" "${host_log}"
  else
    record_gate "${version}" preserve failed 1 \
      "Unity ${version} container exited without writing usable gate evidence" "${host_log}"
  fi
  gate_status="$(node -p "require('${release_output}/unity-gate/${version}/gate.json').status")"
  if [[ "${gate_status}" != passed ]]; then
    if [[ "${gate_status}" == not-run ]]; then not_run=1; else failed=1; fi
  fi
  if (( $(ci_remaining_seconds "${deadline_epoch}") == 0 && index + 1 < ${#versions[@]} )); then
    mark_not_started "$((index + 1))" 'Unity matrix work deadline reached'
    break
  fi
done

if (( failed != 0 )); then exit 1; fi
if (( not_run != 0 )); then exit 2; fi
exit 0
