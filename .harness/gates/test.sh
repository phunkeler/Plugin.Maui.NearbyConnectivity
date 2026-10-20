#!/bin/sh
# The repo's one test entry point. Plain `dotnet test` exits 0 on zero tests here, and Microsoft.Testing.Platform
# exits 10 when nothing ran, which the harness reads as Skip: so never exec the script or pass its exit code through.
set -eu
out=$(mktemp)
trap 'rm -f "$out"' EXIT
if ! pwsh scripts/Invoke-Tests.ps1 >"$out" 2>&1; then
  cat "$out"
  echo "scripts/Invoke-Tests.ps1 failed" >&2
  exit 1
fi
cat "$out"
# Invoke-Tests.ps1 prints "Unit tests: N tests, P passed, F failed in Ns".
n=$(awk '/ tests, [0-9]+ passed, / { for (i = 1; i < NF; i++) if ($(i + 1) == "passed,") s += $i; f = 1 } END { if (f) print s }' "$out")
summary="tests passed"
[ -n "$n" ] && summary="$n passed"
printf '{"summary":"%s"}\n' "$summary" >"${HARNESS_VERDICT:-/dev/null}"
