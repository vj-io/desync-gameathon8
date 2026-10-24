#!/usr/bin/env bash
# Local merge gate (no hosted Unity CI yet, see docs/DECISIONS.md D-007).
#   tools/test.sh [all|edit|play] [filter]   run the suites through tools/unity.sh (serialised), print a summary read
#                                            from the NUnit XML, exit 1 on any failure
#   tools/test.sh --report <results.xml>     summarise an existing result file with the same rule, no Unity run
# A suite counts as failed when a test failed, zero tests ran, Unity exited non-zero, or no result file was written
# (compile errors and crashes write none). `filter` is passed to Unity's -testFilter (e.g. "Desync.QA" or "MeltTests;ScoringTests").
# Results: logs/gate-edit-results.xml, logs/gate-play-results.xml. Inconclusive/skipped tests are reported, not failures.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# summarise <label> <xml> <unity exit code>; returns 0 when the suite is clean
summarise() {
  local label=$1 xml=$2 code=$3 head total passed failed skipped inconc secs
  if [ ! -s "$xml" ]; then
    echo "[$label] NO RESULTS (compile error or crash before the tests ran; unity exit=$code)"
    return 1
  fi
  head=$(grep -m1 '<test-run ' "$xml")
  attr() { printf '%s' "$head" | sed -n "s/.* $1=\"\([^\"]*\)\".*/\1/p"; }
  total=$(attr total); passed=$(attr passed); failed=$(attr failed)
  skipped=$(attr skipped); inconc=$(attr inconclusive); secs=$(attr duration)
  printf '[%s] %s tests: %s passed, %s failed, %s inconclusive, %s skipped (%.1f s)\n' \
    "$label" "${total:-?}" "${passed:-?}" "${failed:-?}" "${inconc:-?}" "${skipped:-?}" "${secs:-0}"
  # one line per failed test: full name, then the first line of its message
  awk '
    /<test-case / {
      name = ""; want = 0
      if (match($0, /fullname="[^"]*"/)) name = substr($0, RSTART + 10, RLENGTH - 11)
      if ($0 ~ /result="(Failed|Error)"/) want = 1
    }
    want && /<message>/ {
      msg = $0
      sub(/.*<message>(<!\[CDATA\[)?/, "", msg)
      if (msg == "") { getline msg }
      sub(/\]\]>.*$/, "", msg)
      printf "    FAIL %s\n         %s\n", name, msg
      want = 0
    }' "$xml"
  [ -n "$total" ] && [ "$total" -gt 0 ] || { echo "[$label] no tests ran"; return 1; }
  [ "${failed:-1}" -eq 0 ] || return 1
  [ "$code" -eq 0 ] || { echo "[$label] tests passed but unity exited with $code (see the log named above)"; return 1; }
  return 0
}

# run_suite <EditMode|PlayMode> <tag> <filter>
run_suite() {
  local platform=$1 tag=$2 filter=$3 xml="$ROOT/logs/$2-results.xml" code
  rm -f "$xml"   # a stale file from an earlier run must never pass this run
  "$ROOT/tools/unity.sh" test "$tag" "$platform" ${filter:+"$filter"}
  code=$?
  summarise "$platform" "$xml" "$code"
}

if [ "${1:-}" = "--report" ]; then
  [ -n "${2:-}" ] || { echo "usage: $0 --report <results.xml>" >&2; exit 2; }
  summarise "$(basename "$2")" "$2" 0
  exit $?
fi

mode=${1:-all}; filter=${2:-}
case $mode in all|edit|play) ;; *) echo "usage: $0 [all|edit|play] [filter] | --report <results.xml>" >&2; exit 2 ;; esac

start=$(date +%s); rc=0
if [ "$mode" != play ]; then run_suite EditMode gate-edit "$filter" || rc=1; fi
if [ "$mode" != edit ]; then run_suite PlayMode gate-play "$filter" || rc=1; fi
echo "[gate] $([ $rc -eq 0 ] && echo PASS || echo FAIL) in $(( $(date +%s) - start )) s"
exit $rc
