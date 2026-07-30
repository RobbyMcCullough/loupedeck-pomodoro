#!/usr/bin/env sh
# Builds the plugin and refreshes the .link file that Logi Plugin Service loads it through.
#
# Falls back to a per-user .NET SDK in ~/.dotnet when dotnet is not already on PATH, which is how
# the SDK ends up if you installed it with dotnet-install.sh rather than the system installer.
#
# Pass -p:ReloadPlugin=false to skip the "loupedeck:" reload deep link -- needed in headless or
# sandboxed shells, where launching a URL handler can take the build down with it.
set -e

if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
    DOTNET_ROOT="$HOME/.dotnet"
    export DOTNET_ROOT
    export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
fi

exec dotnet build "$(dirname "$0")/PomodoroClockPlugin.sln" "$@"
