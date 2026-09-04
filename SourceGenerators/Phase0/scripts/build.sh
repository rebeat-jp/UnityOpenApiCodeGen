#!/usr/bin/env bash
set -euo pipefail

phase0_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repo_root="$(cd "${phase0_root}/../.." && pwd)"
project="${phase0_root}/Rhycol.OpenApiCodeGen.SourceGenerator.Phase0/Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.csproj"
output="${phase0_root}/Rhycol.OpenApiCodeGen.SourceGenerator.Phase0/bin/Release/netstandard2.0"
package_analyzers="${repo_root}/Packages/OpenApiCodeGen.SourceGenerator.Phase0/Runtime/Analyzers"
assembly="Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.dll"

cd "${phase0_root}"
dotnet build "${project}" --configuration Release --nologo
mkdir -p "${package_analyzers}"
cp "${output}/${assembly}" "${package_analyzers}/${assembly}"

if grep -a -q "Newtonsoft.Json" "${package_analyzers}/${assembly}"; then
  echo "Baseline analyzer unexpectedly references Newtonsoft.Json." >&2
  exit 1
fi

echo "Copied ${assembly} into the embedded Phase 0 package."
