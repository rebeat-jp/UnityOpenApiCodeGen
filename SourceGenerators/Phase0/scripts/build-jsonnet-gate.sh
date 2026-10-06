#!/usr/bin/env bash
set -euo pipefail

phase0_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="${phase0_root}/Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.JsonNetGate/Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.JsonNetGate.csproj"
output="${phase0_root}/Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.JsonNetGate/bin/Release/netstandard2.0"
artifacts="${phase0_root}/artifacts/jsonnet-gate"
assembly="Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.JsonNetGate.dll"

cd "${phase0_root}"
dotnet build "${project}" --configuration Release --nologo

if [[ -e "${output}/Newtonsoft.Json.dll" ]]; then
  echo "Json.NET gate output must not contain Newtonsoft.Json.dll." >&2
  exit 1
fi

mkdir -p "${artifacts}"
cp "${output}/${assembly}" "${artifacts}/${assembly}"
echo "Built ${artifacts}/${assembly} without copying Newtonsoft.Json.dll."
