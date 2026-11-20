#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "[verify] Checking package.json..."
python3 - "$ROOT/package.json" <<'PY'
import json, sys
with open(sys.argv[1], "r", encoding="utf-8") as f:
    data = json.load(f)
assert data["name"] == "com.the-cattail.afk-motion-patcher"
assert data["version"] == "2.0.0"
assert "k-neco-lab" not in json.dumps(data)
print(f"[verify] package.json OK: {data['name']}@{data['version']}")
PY

echo "[verify] Checking public copy hygiene..."
for doc in "$ROOT/README.md" "$ROOT/CHANGELOG.md" "$ROOT/FEATURE_MAP.md"; do
  if grep -Eqi "k-neco-lab|from 1\.0|1\.x|兼容 1\." "$doc"; then
    echo "[verify] ERROR: forbidden legacy/stale reference in $doc" >&2
    exit 1
  fi
done
echo "[verify] Public copy hygiene OK."
