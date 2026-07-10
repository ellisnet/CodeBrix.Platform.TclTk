# capture_canvas.tcl -- run one canvas scenario in REAL Tk (wish) and dump the
# result of every query line, one output line per "q" command.
#
# Usage:   wish capture_canvas.tcl <scenario-file>   > <fixture-file>
#
# Scenario line format (also parsed by the C# test driver -- keep in sync with
# tests/CodeBrix.Platform.TkCanvas.Tests/Oracle/CanvasOracleScenario.cs):
#
#   # comment                 ignored, as are blank lines
#   canvas ?option value ...? create the canvas (must be the first command);
#                             the canvas is packed and the window mapped
#                             off-screen so its allocated size is real
#   do SUBCMD ARG...          run "$c SUBCMD ARG..." and discard the result
#   q  SUBCMD ARG...          run "$c SUBCMD ARG..." and print the result
#                             (this produces exactly one fixture line)
#
# Every line is a well-formed Tcl list; braces group option values
# (-scrollregion {0 0 400 300}).
#
# IMPORTANT: text items must not appear in scenarios -- their geometry is
# font-dependent and would make fixtures machine-specific. See README.txt.

proc fail {msg} {
    puts stderr $msg
    exit 1
}

if {$argc != 1} {
    fail "usage: wish capture_canvas.tcl <scenario-file>"
}

set f [open [lindex $argv 0] r]
set lines [split [read $f] "\n"]
close $f

# Keep the window invisible to the user: no decorations, far off-screen.
wm withdraw .
wm overrideredirect . 1

set c ""

foreach line $lines {
    set line [string trim $line]
    if {$line eq "" || [string index $line 0] eq "#"} continue
    set verb [lindex $line 0]
    set rest [lrange $line 1 end]
    switch -- $verb {
        canvas {
            if {$c ne ""} { fail "scenario has more than one canvas line" }
            set c [canvas .c {*}$rest]
            pack .c
            update idletasks
            wm geometry . +2500+2500
            wm deiconify .
            update
            update
        }
        do {
            if {$c eq ""} { fail "scenario command before canvas line" }
            $c {*}$rest
        }
        q {
            if {$c eq ""} { fail "scenario query before canvas line" }
            puts [$c {*}$rest]
        }
        default {
            fail "unknown scenario verb: $verb"
        }
    }
}
exit 0
