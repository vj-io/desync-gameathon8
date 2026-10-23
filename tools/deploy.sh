#!/usr/bin/env bash
# tools/deploy.sh [--prod]
# Copies Builds/WebRelease into web/site (which holds vercel.json + the .vercel link), deploys it to Vercel project "desync"
# (preview by default, production only with --prod), prints the URL, then curls index.html + every build file and checks
# status 200 and Content-Encoding. Previews sit behind Vercel login: the check goes through `vercel curl` (auto protection bypass).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/Builds/WebRelease"; SITE="$ROOT/web/site"
[ -f "$SRC/index.html" ] || { echo "no web build in $SRC: run tools/build.sh WebRelease first" >&2; exit 1; }

echo "[deploy] vercel account: $(vercel whoami 2>/dev/null | tail -1)"
mkdir -p "$SITE"
rsync -a --delete --exclude .vercel --exclude vercel.json "$SRC"/ "$SITE"/
cd "$SITE"
[ -f .vercel/project.json ] || { vercel link --yes --project desync >/dev/null 2>&1; rm -f .env.local; }

args=(deploy --yes --format json); target=preview
[ "${1:-}" = "--prod" ] && { args+=(--prod); target=production; }
err=$(mktemp); start=$(date +%s)
out=$(vercel "${args[@]}" 2>"$err") || { cat "$err" >&2; echo "$out" >&2; exit 1; }
rm -f "$err"
url=$(printf '%s' "$out" | grep -o '"url": *"[^"]*"' | head -1 | cut -d'"' -f4)
echo "[deploy] $target deployment ready in $(( $(date +%s) - start ))s: $url"

bad=0
for f in index.html $(cd "$SRC" && find Build TemplateData -type f 2>/dev/null | sort); do
  r=$(vercel curl "/$f" --deployment "$url" -- -s -o /dev/null \
        -w "%{http_code} enc=%header{content-encoding} type=%{content_type} cache=%header{cache-control} bytes=%{size_download}" 2>/dev/null | tail -1)
  printf '  %-58s %s\n' "$f" "$r"
  case $r in 200*) ;; *) bad=1 ;; esac
done
[ "$bad" -eq 0 ] && echo "[deploy] OK: $url" || { echo "[deploy] FAILED checks: $url" >&2; exit 1; }
