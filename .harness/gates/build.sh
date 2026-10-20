#!/bin/sh
set -eu
exec dotnet build src/Plugin.Maui.NearbyConnectivity -nologo -v q -warnaserror
