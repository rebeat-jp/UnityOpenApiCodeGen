#!/usr/bin/env bash
set -euo pipefail

phase0_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repo_root="$(cd "${phase0_root}/../.." && pwd)"
project_directory="Rhycol.OpenApiCodeGen.SourceGenerator.Phase0"
project_file="Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.csproj"
assembly="Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.dll"
package_assembly="${repo_root}/Packages/OpenApiCodeGen.SourceGenerator.Phase0/Runtime/Analyzers/${assembly}"

cd "${phase0_root}"
"${phase0_root}/scripts/build.sh"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Phase0-Reproducible.XXXXXX")"
trap 'rm -rf "${temporary_root}"' EXIT

rsync -a --exclude 'bin' --exclude 'obj' \
  "${phase0_root}/${project_directory}/" "${temporary_root}/${project_directory}/"
dotnet build "${temporary_root}/${project_directory}/${project_file}" \
  --configuration Release \
  --nologo

rebuilt="${temporary_root}/${project_directory}/bin/Release/netstandard2.0/${assembly}"
if ! cmp -s "${package_assembly}" "${rebuilt}"; then
  echo "Analyzer build is not byte-for-byte reproducible across checkout paths." >&2
  shasum -a 256 "${package_assembly}" "${rebuilt}" >&2
  exit 1
fi

if strings "${package_assembly}" | grep -E -q '/Users/|/private/var/|/tmp/'; then
  echo "Analyzer assembly contains an absolute local path." >&2
  exit 1
fi

echo "Verified byte-for-byte reproducible analyzer build."
shasum -a 256 "${package_assembly}"
