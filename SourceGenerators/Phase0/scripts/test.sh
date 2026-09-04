#!/usr/bin/env bash
set -euo pipefail

phase0_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tests="${phase0_root}/Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.Tests/Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.Tests.csproj"

cd "${phase0_root}"
dotnet test "${tests}" --configuration Release --nologo
"${phase0_root}/scripts/verify-reproducible-build.sh"
