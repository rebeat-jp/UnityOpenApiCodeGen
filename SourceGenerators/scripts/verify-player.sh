#!/usr/bin/env bash

# Player gate for Issue #57: builds the verification fixture as IL2CPP Standalone and WebGL players and runs them.
# Usage: verify-player.sh [verify-player.js options]   (see verify-player.js for the options and environment)
#
# The work is done by verify-player.js (Node core modules only) so that the same entry point also works on
# Windows, where Unity is started from Git Bash or a plain Node shell.

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v node >/dev/null 2>&1; then
  echo "node is unavailable; the Player gate needs Node.js 18 or later." >&2
  exit 2
fi

exec node "${scripts_directory}/verify-player.js" "$@"
