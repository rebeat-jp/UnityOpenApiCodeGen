#!/usr/bin/env bash

# Single source of truth for the cloud Unity gate time budget. Tests may shorten
# each value through the corresponding SOURCE_GENERATOR_CI_* variable.
readonly CI_TOTAL_BUDGET_SECONDS="${SOURCE_GENERATOR_CI_TOTAL_BUDGET_SECONDS:-12600}"
readonly CI_EDITOR_TIMEOUT_SECONDS="${SOURCE_GENERATOR_CI_EDITOR_TIMEOUT_SECONDS:-300}"
readonly CI_DOCKER_BUILD_TIMEOUT_SECONDS="${SOURCE_GENERATOR_CI_DOCKER_BUILD_TIMEOUT_SECONDS:-600}"
readonly CI_ACTIVATION_TIMEOUT_SECONDS="${SOURCE_GENERATOR_CI_ACTIVATION_TIMEOUT_SECONDS:-300}"
readonly CI_LICENSE_RETURN_TIMEOUT_SECONDS="${SOURCE_GENERATOR_CI_LICENSE_RETURN_TIMEOUT_SECONDS:-120}"
readonly CI_CLEANUP_TIMEOUT_SECONDS="${SOURCE_GENERATOR_CI_CLEANUP_TIMEOUT_SECONDS:-120}"
readonly CI_MAX_CONTAINER_STARTS="${SOURCE_GENERATOR_CI_MAX_CONTAINER_STARTS:-3}"

for ci_budget_value in \
  "${CI_TOTAL_BUDGET_SECONDS}" \
  "${CI_EDITOR_TIMEOUT_SECONDS}" \
  "${CI_DOCKER_BUILD_TIMEOUT_SECONDS}" \
  "${CI_ACTIVATION_TIMEOUT_SECONDS}" \
  "${CI_LICENSE_RETURN_TIMEOUT_SECONDS}" \
  "${CI_CLEANUP_TIMEOUT_SECONDS}" \
  "${CI_MAX_CONTAINER_STARTS}"; do
  if [[ ! "${ci_budget_value}" =~ ^[1-9][0-9]*$ ]]; then
    echo "CI time-budget values must be positive integers: ${ci_budget_value}" >&2
    return 2
  fi
done
unset ci_budget_value

ci_now_epoch() {
  date +%s
}

ci_remaining_seconds() {
  local deadline="$1"
  local remaining=$((deadline - $(ci_now_epoch)))
  if (( remaining > 0 )); then
    printf '%s\n' "${remaining}"
  else
    printf '0\n'
  fi
}

ci_min_seconds() {
  if (( $1 < $2 )); then printf '%s\n' "$1"; else printf '%s\n' "$2"; fi
}
