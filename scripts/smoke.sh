#!/bin/sh
# Fast check that the library still builds for the plain .NET target; harness start runs it.
set -eu
exec dotnet build src/Plugin.Maui.NearbyConnectivity -f net10.0 -nologo -v q
