#!/usr/bin/env bash
# tools/build.sh <WebRelease|WebDev|MacRelease|WinRelease> [--probe]
# Runs Desync.Build.Editor.BuildScript.<Profile> through tools/unity.sh (serialised; holds the Unity lock for the whole build).
# Output: Builds/<Profile>/.   --probe = build a throw-away camera+cube scene when Build Settings has no scenes yet.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
profile=${1:-}
case $profile in WebRelease|WebDev|MacRelease|WinRelease) ;; *) echo "usage: $0 <WebRelease|WebDev|MacRelease|WinRelease> [--probe]" >&2; exit 2 ;; esac
[ "${2:-}" = "--probe" ] && export DESYNC_BUILD_PROBE=1
out=$(mktemp); start=$(date +%s)
"$ROOT/tools/unity.sh" exec build "Desync.Build.Editor.BuildScript.$profile" 2>&1 | tee "$out"
code=${PIPESTATUS[0]}
echo "[build.sh] $profile exit=$code in $(( $(date +%s) - start ))s"
if [ "$code" -ne 0 ]; then
  log=$(sed -n 's/.*log=//p' "$out" | tail -1)
  [ -n "$log" ] && { echo "--- last lines of $log"; grep -v "Start importing" "$log" | tail -40; }
fi
rm -f "$out"
[ "$code" -eq 0 ] && [ -d "$ROOT/Builds/$profile" ] && du -sh "$ROOT/Builds/$profile"
exit "$code"
