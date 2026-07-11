#!/usr/bin/env bash
# Regenerates the layout-oracle fixtures: runs every *.scenario file under the
# test project's Assets/LayoutOracle/ through REAL Tk (wish 8.6) and writes the
# captured geometry next to it as *.expected.
#
# DEV-ONLY tooling: requires a host wish and a display (X11). The xUnit tests
# never run this -- they replay the committed fixtures headlessly.
#
# Usage:  tools/layout-oracle/generate_fixtures.sh

set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
assets="$here/../../tests/CodeBrix.Platform.TkCanvas.Tests/Assets/LayoutOracle"

if ! command -v wish >/dev/null; then
    echo "error: wish (Tk) is not installed" >&2
    exit 1
fi

count=0
for scenario in "$assets"/*.scenario; do
    [ -e "$scenario" ] || { echo "no scenario files found in $assets" >&2; exit 1; }
    expected="${scenario%.scenario}.expected"
    wish "$here/capture_layout.tcl" "$scenario" > "$expected"
    count=$((count + 1))
    echo "captured: $(basename "$expected")"
done

bindassets="$here/../../tests/CodeBrix.Platform.TkCanvas.Tests/Assets/BindOracle"
if [ -d "$bindassets" ]; then
    for scenario in "$bindassets"/*.scenario; do
        [ -e "$scenario" ] || break
        expected="${scenario%.scenario}.expected"
        wish "$here/capture_bind.tcl" "$scenario" > "$expected"
        count=$((count + 1))
        echo "captured: $(basename "$expected")"
    done
fi

canvasassets="$here/../../tests/CodeBrix.Platform.TkCanvas.Tests/Assets/CanvasOracle"
if [ -d "$canvasassets" ]; then
    for scenario in "$canvasassets"/*.scenario; do
        [ -e "$scenario" ] || break
        expected="${scenario%.scenario}.expected"
        wish "$here/capture_canvas.tcl" "$scenario" > "$expected"
        count=$((count + 1))
        echo "captured: $(basename "$expected")"
    done
fi

themingassets="$here/../../tests/CodeBrix.Platform.TkCanvas.Tests/Assets/ThemingOracle"
if [ -d "$themingassets" ]; then
    for scenario in "$themingassets"/*.scenario; do
        [ -e "$scenario" ] || break
        expected="${scenario%.scenario}.expected"
        wish "$here/capture_theming.tcl" "$scenario" > "$expected"
        count=$((count + 1))
        echo "captured: $(basename "$expected")"
    done
fi

echo "done: $count fixture(s) regenerated with $(echo 'puts [info patchlevel]' | tclsh)"
