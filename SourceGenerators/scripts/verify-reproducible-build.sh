#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

"${scripts_directory}/build.sh"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/UnityOpenApiCodeGen-SourceGenerator-Reproducible.XXXXXX")"
trap 'rm -rf "${temporary_root}"' EXIT

mkdir -p "${temporary_root}/SourceGenerators"
rsync -a \
  --exclude 'artifacts' \
  --exclude 'bin' \
  --exclude 'obj' \
  "${source_generators_root}/" \
  "${temporary_root}/SourceGenerators/"

temporary_project_directory="${temporary_root}/SourceGenerators/Rhycol.OpenApiCodeGen.SourceGenerator"
temporary_project="${temporary_project_directory}/Rhycol.OpenApiCodeGen.SourceGenerator.csproj"
temporary_assembly="${temporary_project_directory}/bin/Release/netstandard2.0/${analyzer_assembly}"

require_file "${temporary_project}"

cd "${temporary_root}/SourceGenerators"
SOURCE_DATE_EPOCH=0 dotnet build "${temporary_project}" \
  --configuration Release \
  --nologo \
  -p:Deterministic=true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:PathMap="${temporary_project_directory}=${canonical_analyzer_source_path}" \
  -p:SuppressImplicitGitSourceLink=true \
  -p:IncludeSourceRevisionInInformationalVersion=false

require_file "${temporary_assembly}"

if ! cmp -s "${analyzer_output}" "${temporary_assembly}"; then
  echo "Analyzer build is not byte-for-byte reproducible across checkout paths." >&2
  shasum -a 256 "${analyzer_output}" "${temporary_assembly}" >&2
  exit 1
fi

for forbidden_path in "${repository_root}" "${temporary_root}"; do
  if LC_ALL=C grep -a -F -q "${forbidden_path}" "${analyzer_output}"; then
    echo "Analyzer assembly contains an absolute local path: ${forbidden_path}" >&2
    exit 1
  fi
done

if LC_ALL=C grep -E -q \
  '(/Users/|/home/|/private/var/|/tmp/|[A-Za-z]:[/\\])' \
  < <(strings "${analyzer_output}"); then
  echo "Analyzer assembly contains an absolute machine-local path." >&2
  exit 1
fi

echo "Verified byte-for-byte reproducible analyzer build across checkout paths."
shasum -a 256 "${analyzer_output}"
