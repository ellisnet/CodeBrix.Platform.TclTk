# random_grid_scenarios.tcl -- deterministically generate random grid-layout
# scenario files for the layout oracle (see random_pack_scenarios.tcl; same
# rules: fixed per-index seeds keep fixtures stable).
#
# DEV-ONLY tooling. Usage:
#
#   tclsh random_grid_scenarios.tcl <output-dir> <count>
#
# Generated files are named 2NN_random_grid.scenario.

proc irand {n} {
    return [expr {int(rand() * $n)}]
}

proc pick {items} {
    return [lindex $items [irand [llength $items]]]
}

if {$argc != 2} {
    puts stderr "usage: tclsh random_grid_scenarios.tcl <output-dir> <count>"
    exit 1
}
lassign $argv outDir count

for {set index 1} {$index <= $count} {incr index} {
    expr {srand($index * 104729)}

    set lines [list "# Auto-generated random grid scenario (seed [expr {$index * 104729}])."]

    # Root-level grid, optionally with one nested gridded container.
    set nCols [expr {2 + [irand 4]}]
    set nRows [expr {2 + [irand 3]}]
    set nWindows [expr {3 + [irand 6]}]
    set nested [expr {[irand 100] < 40}]

    set paths {}
    for {set w 1} {$w <= $nWindows} {incr w} {
        set path ".w$w"
        lappend lines "window $path [expr {10 + [irand 91]}] [expr {10 + [irand 71]}]"
        lappend paths $path
    }
    if {$nested} {
        lappend lines "window .w1.n1 [expr {10 + [irand 61]}] [expr {10 + [irand 41]}]"
        lappend lines "window .w1.n2 [expr {10 + [irand 61]}] [expr {10 + [irand 41]}]"
    }

    # Occupy random cells (duplicates allowed: Tk stacks them in the same
    # cell; geometry stays well-defined).
    foreach path $paths {
        set opts [list -row [irand $nRows] -column [irand $nCols]]
        if {[irand 100] < 25} { lappend opts -columnspan [expr {2 + [irand 2]}] }
        if {[irand 100] < 20} { lappend opts -rowspan 2 }
        if {[irand 100] < 70} {
            set sticky ""
            foreach f {n s e w} { if {[irand 100] < 45} { append sticky $f } }
            if {$sticky ne ""} { lappend opts -sticky $sticky }
        }
        if {[irand 100] < 40} { lappend opts -padx "[irand 8]:[irand 8]" }
        if {[irand 100] < 40} { lappend opts -pady [irand 8] }
        if {[irand 100] < 25} { lappend opts -ipadx [expr {1 + [irand 5]}] }
        if {[irand 100] < 25} { lappend opts -ipady [expr {1 + [irand 5]}] }
        lappend lines "grid $path $opts"
    }
    if {$nested} {
        lappend lines "grid .w1.n1 -row 0 -column 0 -sticky nsew"
        lappend lines "grid .w1.n2 -row 0 -column 1 -pady 2"
        if {[irand 100] < 50} { lappend lines "gridcolumn .w1 0 -weight 1" }
    }

    # Random slot constraints.
    for {set c 0} {$c < $nCols} {incr c} {
        set opts {}
        if {[irand 100] < 45} { lappend opts -weight [irand 4] }
        if {[irand 100] < 30} { lappend opts -minsize [expr {10 + [irand 60]}] }
        if {[irand 100] < 15} { lappend opts -pad [irand 10] }
        if {[irand 100] < 20} { lappend opts -uniform [pick {ua ub}] }
        if {[llength $opts]} { lappend lines "gridcolumn . $c $opts" }
    }
    for {set r 0} {$r < $nRows} {incr r} {
        set opts {}
        if {[irand 100] < 45} { lappend opts -weight [irand 4] }
        if {[irand 100] < 30} { lappend opts -minsize [expr {10 + [irand 40]}] }
        if {[irand 100] < 15} { lappend opts -pad [irand 8] }
        if {[irand 100] < 20} { lappend opts -uniform [pick {ua ub}] }
        if {[llength $opts]} { lappend lines "gridrow . $r $opts" }
    }

    if {[irand 100] < 30} {
        lappend lines "gridanchor . [pick {n ne e se s sw w nw center}]"
    }

    # Most random grids force a root size (grow AND shrink paths both hit).
    if {[irand 100] < 75} {
        lappend lines "rootsize [expr {60 + [irand 361]}] [expr {60 + [irand 261]}]"
    }

    set name [format "2%02d_random_grid.scenario" $index]
    set f [open [file join $outDir $name] w]
    puts $f [join $lines "\n"]
    close $f
    puts "generated: $name"
}
