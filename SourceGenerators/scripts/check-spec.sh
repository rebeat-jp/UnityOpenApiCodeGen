#!/usr/bin/env bash
#
# Runs a real OpenAPI document through the editor loader, the source generator and the compiler and lists
# every diagnostic (local pre-release check, no Unity needed).
#
# Usage: check-spec.sh <spec-path> [--output <dir>]

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

usage() {
  echo "Usage: $(basename "$0") <spec-path> [--output <dir>]" >&2
  exit 2
}

spec_path=""
output_directory=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --output)
      [[ $# -ge 2 ]] || usage
      output_directory="$2"
      shift 2
      ;;
    -h|--help)
      usage
      ;;
    -*)
      echo "Unknown option: $1" >&2
      usage
      ;;
    *)
      [[ -z "${spec_path}" ]] || usage
      spec_path="$1"
      shift
      ;;
  esac
done

[[ -n "${spec_path}" ]] || usage
if [[ ! -f "${spec_path}" ]]; then
  echo "The spec does not exist: ${spec_path}" >&2
  exit 1
fi
spec_path="$(cd "$(dirname "${spec_path}")" && pwd)/$(basename "${spec_path}")"

if [[ -n "${output_directory}" ]]; then
  reject_output() {
    echo "--output $1: ${output_directory}" >&2
    exit 1
  }
  [[ "${output_directory}" == /* ]] || output_directory="${PWD}/${output_directory}"

  # Split into the part that exists and the part that does not. The missing part must be plain names:
  # "." and ".." there could not be resolved (and could lead back into the repository) before it is created.
  existing_ancestor="${output_directory}"
  missing_suffix=""
  while [[ ! -d "${existing_ancestor}" ]]; do
    name="$(basename "${existing_ancestor}")"
    if [[ "${name}" == "." || "${name}" == ".." ]]; then
      reject_output "must not contain '.' or '..' after the part that already exists"
    fi
    missing_suffix="/${name}${missing_suffix}"
    existing_ancestor="$(dirname "${existing_ancestor}")"
  done
  existing_ancestor="$(cd "${existing_ancestor}" && pwd -P)"

  # Compare by inode (-ef), not by text: a case-insensitive file system (macOS) or a symbolic link spells the
  # same directory differently. Walk from the nearest existing ancestor up to / and refuse the repository root.
  walk="${existing_ancestor}"
  while :; do
    if [[ "${walk}" -ef "${repository_root}" ]]; then
      reject_output "must be outside the repository working tree"
    fi
    [[ "${walk}" == "/" ]] && break
    walk="$(dirname "${walk}")"
  done
  output_directory="${existing_ancestor}${missing_suffix}"
  mkdir -p "${output_directory}"
fi

require_file "${tests_project}"

status_before="$(git -C "${repository_root}" status --porcelain)"

test_status=0
cd "${source_generators_root}"
OPENAPI_CODEGEN_CHECK_SPEC="${spec_path}" \
OPENAPI_CODEGEN_CHECK_OUTPUT="${output_directory}" \
  dotnet test "${tests_project}" \
    --configuration Release \
    --nologo \
    --filter "FullyQualifiedName~ExternalSpecCheckTests" \
    --logger "console;verbosity=detailed" || test_status=$?

status_after="$(git -C "${repository_root}" status --porcelain)"
if [[ "${status_before}" != "${status_after}" ]]; then
  echo "git status changed while checking the spec (nothing may be written to the repository):" >&2
  diff <(printf '%s\n' "${status_before}") <(printf '%s\n' "${status_after}") >&2 || true
  exit 1
fi

exit "${test_status}"
