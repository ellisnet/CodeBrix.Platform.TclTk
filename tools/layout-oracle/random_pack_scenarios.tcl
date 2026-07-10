# random_pack_scenarios.tcl -- deterministically generate random pack-layout
# scenario files for the layout oracle. Each file index seeds Tcl's RNG, so
# regeneration always produces the same scenarios (fixtures stay stable).
#
# DEV-ONLY tooling. Usage:
#
#   tclsh random_pack_scenarios.tcl <output-dir> <count>
#
# Generated files are named 1NN_random.scenario (offset so they sort after the
# hand-authored scenarios).

proc irand {n} {
    return [expr {int(rand() * $n)}]
}

proc pick {items} {
    return [lindex $items [irand [llength $items]]]
}

if {$argc != 2} {
    puts stderr "usage: tclsh random_pack_scenarios.tcl <output-dir> <count>"
    exit 1
}
lassign $argv outDir count

for {set index 1} {$index <= $count} {incr index} {
    expr {srand($index * 7919)}

    set lines [list "# Auto-generated random pack scenario (seed [expr {$index * 7919}])."]
    set windows [list .]
    set containers [list .]
    set nWindows [expr {4 + [irand 6]}]

    # Create windows: each parented to a random existing window of depth < 3.
    for {set w 1} {$w <= $nWindows} {incr w} {
        set candidates {}
        foreach c $containers {
            set depth [expr {[llength [split $c .]] - 1}]
            if {$c eq "." || $depth < 3} { lappend candidates $c }
        }
        set parent [pick $candidates]
        if {$parent eq "."} { set path ".w$w" } else { set path "$parent.w$w" }
        set reqW [expr {10 + [irand 111]}]
        set reqH [expr {10 + [irand 111]}]
        lappend lines "window $path $reqW $reqH"
        lappend windows $path
        lappend containers $path
    }

    # Give some windows an internal border.
    foreach path $windows {
        if {$path ne "." && [irand 100] < 25} {
            lappend lines "border $path [expr {1 + [irand 6]}]"
        }
    }

    # Occasionally turn propagation off for an interior container that has
    # children (its own -width/-height request then stands).
    foreach path $windows {
        if {$path eq "."} continue
        set hasChild 0
        foreach other $windows {
            if {$other ne $path && [string first "$path." $other] == 0} {
                set hasChild 1
                break
            }
        }
        if {$hasChild && [irand 100] < 20} {
            lappend lines "packpropagate $path 0"
        }
    }

    # Pack every window into its parent, in creation order.
    foreach path $windows {
        if {$path eq "."} continue
        set opts [list -side [pick {top bottom left right}]]
        if {[irand 100] < 40} { lappend opts -fill [pick {x y both}] }
        if {[irand 100] < 30} { lappend opts -expand 1 }
        if {[irand 100] < 45} { lappend opts -anchor [pick {n ne e se s sw w nw center}] }
        if {[irand 100] < 50} {
            if {[irand 100] < 30} {
                lappend opts -padx "[irand 10]:[irand 10]"
            } else {
                lappend opts -padx [irand 10]
            }
        }
        if {[irand 100] < 50} {
            if {[irand 100] < 30} {
                lappend opts -pady "[irand 10]:[irand 10]"
            } else {
                lappend opts -pady [irand 10]
            }
        }
        if {[irand 100] < 25} { lappend opts -ipadx [expr {1 + [irand 6]}] }
        if {[irand 100] < 25} { lappend opts -ipady [expr {1 + [irand 6]}] }
        lappend lines "pack $path $opts"
    }

    # Half the scenarios force a root size (sometimes squeezing content out).
    if {[irand 100] < 50} {
        lappend lines "rootsize [expr {80 + [irand 341]}] [expr {80 + [irand 241]}]"
    }

    set name [format "1%02d_random.scenario" $index]
    set f [open [file join $outDir $name] w]
    puts $f [join $lines "\n"]
    close $f
    puts "generated: $name"
}
