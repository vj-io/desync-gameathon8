#!/usr/bin/env bash
# Serialised Unity batchmode runner (one Unity at a time per project; a lock dir guards it).
#   tools/unity.sh compile <tag>                 import + compile; prints compiler errors
#   tools/unity.sh exec <tag> <Class.Method>     compile then -executeMethod (editor static method)
#   tools/unity.sh test <tag> [EditMode|PlayMode] [filter]
# <tag> = your workstream name; logs go to logs/<tag>-*.log. Never open the project in the Unity GUI while a batch run is active.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/DESYNC"
UNITY=/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity
LOCK="$ROOT/.unity.lock"
mode=${1:?mode}; tag=${2:-anon}
mkdir -p "$ROOT/logs"
log="$ROOT/logs/$tag-$(date +%H%M%S)-$$.log"
waited=0
until mkdir "$LOCK" 2>/dev/null; do
  pid=$(cat "$LOCK/pid" 2>/dev/null)
  if [ -n "$pid" ] && ! kill -0 "$pid" 2>/dev/null; then rm -rf "$LOCK"; continue; fi
  sleep 2; waited=$((waited+2))
  [ $((waited % 60)) -eq 0 ] && echo "[unity.sh] waiting for lock held by $(cat "$LOCK/tag" 2>/dev/null) (${waited}s)" >&2
done
echo $$ > "$LOCK/pid"; echo "$tag" > "$LOCK/tag"
trap 'rm -rf "$LOCK"' EXIT
args=(-batchmode -projectPath "$PROJ" -logFile "$log")
case $mode in
  compile) args+=(-nographics -quit) ;;
  exec)    args+=(-quit -executeMethod "${3:?method}") ;;
  test)    args+=(-runTests -testPlatform "${3:-EditMode}" -testResults "$ROOT/logs/$tag-results.xml"); [ -n "${4:-}" ] && args+=(-testFilter "$4") ;;
  *) echo "unknown mode $mode"; exit 2 ;;
esac
"$UNITY" "${args[@]}"; code=$?
grep -v "Start importing" "$log" | grep -E "error CS[0-9]+|warning CS0(108|114|618)|Exception:|executeMethod|\[DESYNC\]|Build (succeeded|failed)|Tests? (passed|failed)" | grep -v Licensing | sort -u | head -80
echo "[unity.sh] exit=$code log=$log"
exit $code
