# random_canvas_scenarios.tcl -- deterministically generate random canvas
# scenario files for the canvas oracle. Each file index seeds Tcl's RNG, so
# regeneration always produces the same scenarios (fixtures stay stable).
#
# DEV-ONLY tooling. Usage:
#
#   tclsh random_canvas_scenarios.tcl <output-dir> <count>
#
# Generated files are named 3NN_random_canvas.scenario, offset by 320 so they
# sort after the hand-authored canvas scenarios.
#
# Items are lines, rectangles, and polygons only (never text: font-dependent
# geometry would make fixtures machine-specific).

proc irand {n} {
    return [expr {int(rand() * $n)}]
}

proc pick {items} {
    return [lindex $items [irand [llength $items]]]
}

proc coord {} {
    # Coordinates in a -60..440 space; half integral, half with .5 fractions.
    set base [expr {-60 + [irand 501]}]
    if {[irand 2]} { return $base }
    return [expr {$base + 0.5}]
}

if {$argc != 2} {
    puts stderr "usage: tclsh random_canvas_scenarios.tcl <output-dir> <count>"
    exit 1
}
lassign $argv outDir count

for {set index 1} {$index <= $count} {incr index} {
    expr {srand($index * 6151)}

    set lines [list "# Auto-generated random canvas scenario (seed [expr {$index * 6151}])."]
    lappend lines "canvas -width 400 -height 300 -scrollregion {-100 -100 500 400}"

    set nItems [expr {5 + [irand 11]}]
    set ids {}
    for {set i 1} {$i <= $nItems} {incr i} {
        set kind [pick {line line rectangle rectangle polygon}]
        switch -- $kind {
            line {
                set nPts [expr {2 + [irand 4]}]
                set coords {}
                for {set p 0} {$p < $nPts} {incr p} {
                    lappend coords [coord] [coord]
                }
                set opts {}
                set width [pick {1 1 2 3 4.5 7 10}]
                lappend opts -width $width
                lappend opts -capstyle [pick {butt butt round projecting}]
                lappend opts -joinstyle [pick {round round miter bevel}]
                if {$nPts >= 2 && [irand 100] < 30} {
                    lappend opts -arrow [pick {first last both}]
                    if {[irand 2]} { lappend opts -arrowshape {{10 12 5}} }
                }
                if {$nPts > 2 && [irand 100] < 25} {
                    lappend opts -smooth 1
                    lappend opts -splinesteps [pick {4 8 12}]
                }
                lappend lines "do create line $coords [join $opts]"
            }
            rectangle {
                set opts {}
                lappend opts -width [pick {1 1 2 3.5 5 8}]
                if {[irand 100] < 30} { lappend opts -fill red }
                if {[irand 100] < 20} { lappend opts -outline {{}} }
                lappend lines "do create rectangle [coord] [coord] [coord] [coord] [join $opts]"
            }
            polygon {
                set nPts [expr {3 + [irand 3]}]
                set coords {}
                for {set p 0} {$p < $nPts} {incr p} {
                    lappend coords [coord] [coord]
                }
                set opts {}
                if {[irand 2]} { lappend opts -outline black -width [pick {1 2 4 6.5}] }
                if {[irand 100] < 25} { lappend opts -fill {{}} }
                if {[irand 100] < 25} {
                    lappend opts -smooth 1
                    lappend opts -splinesteps [pick {4 8 12}]
                }
                lappend lines "do create polygon $coords [join $opts]"
            }
        }
        lappend ids $i
    }

    # A few display-order shuffles.
    for {set s 0} {$s < 3} {incr s} {
        set id [pick $ids]
        if {[irand 2]} {
            if {[irand 2]} {
                lappend lines "do raise $id"
            } else {
                lappend lines "do raise $id [pick $ids]"
            }
        } else {
            if {[irand 2]} {
                lappend lines "do lower $id"
            } else {
                lappend lines "do lower $id [pick $ids]"
            }
        }
    }
    lappend lines "q find all"

    # Geometry queries for every item.
    foreach id $ids {
        lappend lines "q bbox $id"
        lappend lines "q coords $id"
    }

    # Point searches on a coarse grid plus random probe points.
    foreach x {-50 50 150 250 350} {
        foreach y {-50 100 250} {
            lappend lines "q find closest $x $y"
        }
    }
    for {set p 0} {$p < 6} {incr p} {
        lappend lines "q find closest [coord] [coord] [pick {0 0 2 5 10}]"
    }

    # Area searches.
    for {set p 0} {$p < 6} {incr p} {
        set which [pick {overlapping enclosed}]
        lappend lines "q find $which [coord] [coord] [coord] [coord]"
    }

    # A move/scale pass, then re-query a few boxes.
    lappend lines "do move all [pick {5 -7 12.5}] [pick {3 -4 6.5}]"
    lappend lines "do scale all 0 0 [pick {0.5 2 1.25}] [pick {0.5 2 0.75}]"
    foreach id $ids {
        lappend lines "q bbox $id"
    }
    lappend lines "q bbox all"

    set path [file join $outDir [format "3%02d_random_canvas.scenario" [expr {19 + $index}]]]
    set f [open $path w]
    puts $f [join $lines "\n"]
    close $f
    puts "generated: [file tail $path]"
}
