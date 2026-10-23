#!/usr/bin/env bash
# Runs the tests, then publishes and packages the Windows and Linux apps into dist/.
#   ./build.sh [--skip-tests] [--out <folder>]
#
# In WSL with the repo on a Windows drive, the tests run here on Linux first,
# then the rest runs on Windows so the exe gets its icon and manifest. Without
# working Windows interop everything runs here, and the exe has no icon.
set -euo pipefail
cd "$(dirname "$0")"

windows_interop() {
    grep -qi microsoft /proc/version 2>/dev/null && [[ "$PWD" == /mnt/* ]] \
        && powershell.exe -NoProfile -Command 'exit 0' >/dev/null 2>&1
}

if windows_interop; then
    if [[ " $* " != *" --skip-tests "* ]]; then
        dotnet test --configuration Release
    fi

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w ./build.ps1)" "$@"
else
    dotnet run scripts/build.cs -- "$@"
fi
