#!/usr/bin/env bash

set -euo pipefail

readonly source_generators_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly repository_root="$(cd "${source_generators_root}/.." && pwd)"

readonly analyzer_project_directory="${source_generators_root}/Rhycol.OpenApiCodeGen.SourceGenerator"
readonly analyzer_project="${analyzer_project_directory}/Rhycol.OpenApiCodeGen.SourceGenerator.csproj"
readonly analyzer_assembly_name="Rhycol.OpenApiCodeGen.SourceGenerator"
readonly analyzer_assembly="${analyzer_assembly_name}.dll"
readonly analyzer_output="${analyzer_project_directory}/bin/Release/netstandard2.0/${analyzer_assembly}"

readonly tests_project="${source_generators_root}/Rhycol.OpenApiCodeGen.SourceGenerator.Tests/Rhycol.OpenApiCodeGen.SourceGenerator.Tests.csproj"
readonly inspector_project="${source_generators_root}/BuildTools/AnalyzerInspector/AnalyzerInspector.csproj"

readonly package_root="${repository_root}/Packages/OpenApiCodeGen.SourceGenerator"
readonly package_analyzers="${package_root}/Runtime/Analyzers"
readonly package_analyzer="${package_analyzers}/${analyzer_assembly}"

readonly canonical_analyzer_source_path="/_/SourceGenerators/Rhycol.OpenApiCodeGen.SourceGenerator"

require_file() {
  local path="$1"
  if [[ ! -f "${path}" ]]; then
    echo "Required file does not exist: ${path}" >&2
    exit 1
  fi
}

build_analyzer_project() {
  local project="$1"
  local project_directory
  project_directory="$(cd "$(dirname "${project}")" && pwd)"

  SOURCE_DATE_EPOCH=0 dotnet build "${project}" \
    --configuration Release \
    --nologo \
    -p:Deterministic=true \
    -p:ContinuousIntegrationBuild=true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -p:PathMap="${project_directory}=${canonical_analyzer_source_path}" \
    -p:SuppressImplicitGitSourceLink=true \
    -p:IncludeSourceRevisionInInformationalVersion=false
}

inspect_analyzer() {
  local assembly_path="$1"
  require_file "${inspector_project}"
  require_file "${assembly_path}"

  dotnet run \
    --project "${inspector_project}" \
    --configuration Release \
    -- \
    "${assembly_path}" \
    "${analyzer_assembly_name}" \
    ".NETStandard,Version=v2.0"
}
