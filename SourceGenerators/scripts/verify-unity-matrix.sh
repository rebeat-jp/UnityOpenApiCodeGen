#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

"${scripts_directory}/verify.sh"

for unity_version in 6000.0.23f1 6000.3.2f1; do
  SOURCE_GENERATOR_SKIP_DOTNET_VERIFY=1 \
    "${scripts_directory}/verify-unity.sh" "${unity_version}"
done

echo "Unity source-generator verification matrix passed."
