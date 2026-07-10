# capture_layout.tcl -- build a layout scenario in REAL Tk (wish) and dump the
# resulting geometry, one line per window:
#
#     PATH  x  y  width  height  reqwidth  reqheight  ismapped
#
# The root window "." reports x=0 y=0 (its screen position is irrelevant).
#
# Usage:   wish capture_layout.tcl <scenario-file>   > <fixture-file>
#
# Scenario line format (also parsed by the C# test driver -- keep in sync with
# tests/CodeBrix.Platform.TkCanvas.Tests/Oracle/OracleScenario.cs):
#
#   # comment                              ignored, as are blank lines
#   window PATH REQW REQH                  create frame PATH requesting REQW x REQH
#   border PATH PIXELS                     give PATH an internal border (frame -borderwidth)
#   pack PATH ?-opt val ...?               pack PATH; -padx/-pady accept N or N:M (left:right / top:bottom)
#   packforget PATH                        pack forget PATH
#   packpropagate PATH 0|1                 pack propagate PATH (never use on "." -- see README)
#   grid PATH ?-opt val ...?               grid PATH; -padx/-pady accept N or N:M; -sticky nsew...
#   gridforget PATH                        grid forget PATH
#   gridpropagate PATH 0|1                 grid propagate PATH (never use on "." -- see README)
#   gridanchor PATH ANCHOR                 grid anchor PATH ANCHOR
#   gridcolumn PATH INDEX ?-opt val ...?   grid columnconfigure (-minsize -weight -pad -uniform)
#   gridrow PATH INDEX ?-opt val ...?      grid rowconfigure (-minsize -weight -pad -uniform)
#   rootsize W H                           force the root window size (wm geometry WxH)
#
# Windows must be created before they are referenced; parents before children.

proc fail {msg} {
    puts stderr $msg
    exit 1
}

if {$argc != 1} {
    fail "usage: wish capture_layout.tcl <scenario-file>"
}

set f [open [lindex $argv 0] r]
set lines [split [read $f] "\n"]
close $f

# Keep the window invisible and out of the window manager's hands: no
# decorations, no WM size negotiation, positioned far off-screen.
wm withdraw .
wm overrideredirect . 1

set rootW ""
set rootH ""
set order [list .]

foreach line $lines {
    set line [string trim $line]
    if {$line eq "" || [string index $line 0] eq "#"} continue
    set words $line
    set cmd [lindex $words 0]
    switch -- $cmd {
        window {
            lassign $words -> path w h
            frame $path -width $w -height $h -borderwidth 0 -highlightthickness 0
            lappend order $path
        }
        border {
            lassign $words -> path bw
            $path configure -borderwidth $bw
        }
        pack {
            set path [lindex $words 1]
            set args {}
            for {set i 2} {$i < [llength $words]} {incr i 2} {
                set opt [lindex $words $i]
                set val [lindex $words [expr {$i + 1}]]
                switch -- $opt {
                    -padx - -pady {
                        if {[string first ":" $val] >= 0} {
                            lassign [split $val ":"] a b
                            lappend args $opt [list $a $b]
                        } else {
                            lappend args $opt $val
                        }
                    }
                    default {
                        lappend args $opt $val
                    }
                }
            }
            pack $path {*}$args
        }
        packforget {
            pack forget [lindex $words 1]
        }
        packpropagate {
            pack propagate [lindex $words 1] [lindex $words 2]
        }
        grid {
            set path [lindex $words 1]
            set args {}
            for {set i 2} {$i < [llength $words]} {incr i 2} {
                set opt [lindex $words $i]
                set val [lindex $words [expr {$i + 1}]]
                switch -- $opt {
                    -padx - -pady {
                        if {[string first ":" $val] >= 0} {
                            lassign [split $val ":"] a b
                            lappend args $opt [list $a $b]
                        } else {
                            lappend args $opt $val
                        }
                    }
                    default {
                        lappend args $opt $val
                    }
                }
            }
            grid $path {*}$args
        }
        gridforget {
            grid forget [lindex $words 1]
        }
        gridpropagate {
            grid propagate [lindex $words 1] [lindex $words 2]
        }
        gridanchor {
            grid anchor [lindex $words 1] [lindex $words 2]
        }
        gridcolumn {
            grid columnconfigure [lindex $words 1] [lindex $words 2] {*}[lrange $words 3 end]
        }
        gridrow {
            grid rowconfigure [lindex $words 1] [lindex $words 2] {*}[lrange $words 3 end]
        }
        rootsize {
            lassign $words -> rootW rootH
        }
        default {
            fail "unknown scenario command: $cmd"
        }
    }
}

update idletasks
if {$rootW ne ""} {
    wm geometry . ${rootW}x${rootH}
}
wm geometry . +2500+2500
wm deiconify .
update
update

foreach path $order {
    if {![winfo exists $path]} continue
    if {$path eq "."} {
        set x 0
        set y 0
    } else {
        set x [winfo x $path]
        set y [winfo y $path]
    }
    puts "$path $x $y [winfo width $path] [winfo height $path] [winfo reqwidth $path] [winfo reqheight $path] [winfo ismapped $path]"
}
exit 0
