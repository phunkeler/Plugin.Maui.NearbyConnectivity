#!/bin/sh
# Idempotent setup this repo needs before gates run; harness start runs it.
set -eu
dotnet tool restore
# On a dev machine with a machine-wide SDK, workload restore needs sudo even when nothing is missing; the
# workloads are already installed there, and the smoke build fails loudly if they are not. CI runners succeed.
dotnet workload restore Plugin.Maui.NearbyConnectivity.slnx || echo "workload restore failed; continuing with the installed workloads" >&2
