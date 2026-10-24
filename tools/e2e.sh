#!/usr/bin/env bash
# tools/e2e.sh [playwright args]
# Browser acceptance tests E2E-01..05 (tests/e2e, Playwright) against a local static serve of a WebGL build.
#   BUILD_DIR=<dir>   the build to serve (default Builds/WebRelease; it is only read)
#   BASE_URL=<url>    test a deployment instead of a local build (no local server)
#   E2E_GPU=0|1       1 = the full Chromium on the real GPU (default on macOS), 0 = software WebGL. The frame-bound tests E2E-03
#                     (restart < 300 ms) and E2E-05 (frame budget) skip themselves on software WebGL; E2E_ANGLE picks the
#                     backend (metal | d3d11 | vulkan)
#   E2E_STRICT=1      a build without the autoplay / analytics page hooks fails instead of skipping (use on a fresh build)
#   E2E_RETRIES=n     retry a failed test n times (default 1: the timing budgets wobble on a busy machine; a pass after a retry
#                     is reported as flaky, not hidden). E2E_READY_MS / E2E_AUTOPLAY_MS override the E2E-01 / E2E-04 budgets
#   PORT              local server port (default 8080)
# Examples:  tools/e2e.sh                       tools/e2e.sh -g "E2E-0[1-3]"       E2E_STRICT=1 tools/e2e.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/tests/e2e"

if [ -z "${BASE_URL:-}" ]; then
  export BUILD_DIR="${BUILD_DIR:-$ROOT/Builds/WebRelease}"
  if [ ! -f "$BUILD_DIR/index.html" ]; then
    echo "[e2e] no WebGL build at $BUILD_DIR (index.html missing). Build it first: tools/build.sh WebRelease, or set BUILD_DIR / BASE_URL." >&2
    exit 2
  fi
  echo "[e2e] build: $BUILD_DIR"
else
  echo "[e2e] deployment: $BASE_URL"
fi

[ -d node_modules ] || npm install --no-audit --no-fund
npx playwright install chromium >/dev/null 2>&1 || echo "[e2e] could not check the Playwright browser (offline?); using the one already installed" >&2

export E2E_RETRIES="${E2E_RETRIES:-1}"
[ "$(uname)" = "Darwin" ] && export E2E_GPU="${E2E_GPU:-1}"
exec npx playwright test "$@"
