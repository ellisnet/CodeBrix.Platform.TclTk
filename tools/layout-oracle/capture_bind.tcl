# capture_bind.tcl -- run a bind-dispatch scenario in REAL Tk (wish) and dump
# the firing log, one line per fired binding.
#
# Usage:   wish capture_bind.tcl <scenario-file>   > <fixture-file>
#
# Scenario line format (also parsed by the C# test driver -- keep in sync with
# tests/CodeBrix.Platform.TkCanvas.Tests/Oracle/BindOracleScenario.cs):
#
#   window PATH                          create frame PATH (size irrelevant here)
#   bind TAG PATTERN LABEL ?break?       logging binding; LABEL identifies it in
#                                        the log; a trailing "break" makes it
#                                        stop further bind-tag processing
#   bindtags PATH TAG ?TAG ...?          replace PATH's bind tags
#   unbind TAG PATTERN                   remove a binding
#   event PATH buttonpress B X Y ?MODS?  event generate <ButtonPress> -button B
#   event PATH buttonrelease B X Y ?MODS?
#   event PATH motion X Y ?MODS?
#   event PATH keypress KEYSYM ?MODS?
#   event PATH keyrelease KEYSYM ?MODS?
#   event PATH enter | leave
#   event PATH wheel DELTA
#   event PATH virtual NAME
#   event PATH focusin | focusout
#   destroywin PATH
#
# MODS is a comma list of: shift lock control mod1 meta b1 b2 b3 b4 b5.
#
# Log line formats (typed by pattern; identical in the C# driver):
#   key events:            LABEL %W %K
#   buttonpress/release:   LABEL %W %x %y %b
#   motion:                LABEL %W %x %y
#   mousewheel:            LABEL %W %D
#   everything else:       LABEL %W

proc fail {msg} {
    puts stderr $msg
    exit 1
}

if {$argc != 1} {
    fail "usage: wish capture_bind.tcl <scenario-file>"
}

set f [open [lindex $argv 0] r]
set lines [split [read $f] "\n"]
close $f

wm withdraw .
set ::log {}

proc logScript {pattern label brk} {
    # Field template decided by pattern content:
    if {[string match "<<*" $pattern]} {
        set tpl "%W"
    } elseif {[string match "*Key*" $pattern]} {
        set tpl "%W %K"
    } elseif {[string match "*Button*" $pattern] || [regexp {<(([A-Za-z0-9]+-)*)[1-5]>} $pattern]} {
        set tpl "%W %x %y %b"
    } elseif {[string match "*Motion*" $pattern]} {
        set tpl "%W %x %y"
    } elseif {[string match "*MouseWheel*" $pattern]} {
        set tpl "%W %D"
    } elseif {[regexp {^<((Shift|Lock|Control|Alt|Mod1|Meta|Command|Double|Triple|B[1-5])-)*(Enter|Leave|FocusIn|FocusOut|Configure|Destroy|Map|Unmap)>$} $pattern]} {
        set tpl "%W"
    } else {
        # Bare keysym pattern like <Escape>, <Down>, <a>.
        set tpl "%W %K"
    }
    set script "lappend ::log \"$label $tpl\""
    if {$brk} {
        append script "\nbreak"
    }
    return $script
}

proc modsToState {mods} {
    set state 0
    foreach m [split $mods ","] {
        switch -- $m {
            "" {}
            shift   { incr state 1 }
            lock    { incr state 2 }
            control { incr state 4 }
            mod1    { incr state 8 }
            meta    { incr state 16 }
            b1      { incr state 256 }
            b2      { incr state 512 }
            b3      { incr state 1024 }
            b4      { incr state 2048 }
            b5      { incr state 4096 }
            default { fail "unknown modifier: $m" }
        }
    }
    return $state
}

foreach line $lines {
    set line [string trim $line]
    if {$line eq "" || [string index $line 0] eq "#"} continue
    set words $line
    set cmd [lindex $words 0]
    switch -- $cmd {
        window {
            frame [lindex $words 1] -width 50 -height 50 -borderwidth 0 -highlightthickness 0
            # Force the X window into existence: "event generate" silently
            # drops events for windows that have not been created yet.
            winfo id [lindex $words 1]
        }
        bind {
            lassign $words -> tag pattern label brk
            bind $tag $pattern [logScript $pattern $label [expr {$brk eq "break"}]]
        }
        unbind {
            lassign $words -> tag pattern
            bind $tag $pattern {}
        }
        bindtags {
            bindtags [lindex $words 1] [lrange $words 2 end]
        }
        event {
            set path [lindex $words 1]
            set kind [lindex $words 2]
            switch -- $kind {
                buttonpress {
                    lassign $words -> -> -> b x y mods
                    event generate $path <ButtonPress> -button $b -x $x -y $y -state [modsToState $mods]
                }
                buttonrelease {
                    lassign $words -> -> -> b x y mods
                    event generate $path <ButtonRelease> -button $b -x $x -y $y -state [modsToState $mods]
                }
                motion {
                    lassign $words -> -> -> x y mods
                    event generate $path <Motion> -x $x -y $y -state [modsToState $mods]
                }
                keypress {
                    lassign $words -> -> -> keysym mods
                    event generate $path <KeyPress> -keysym $keysym -state [modsToState $mods]
                }
                keyrelease {
                    lassign $words -> -> -> keysym mods
                    event generate $path <KeyRelease> -keysym $keysym -state [modsToState $mods]
                }
                enter {
                    event generate $path <Enter>
                }
                leave {
                    event generate $path <Leave>
                }
                wheel {
                    event generate $path <MouseWheel> -delta [lindex $words 3]
                }
                virtual {
                    event generate $path <<[lindex $words 3]>>
                }
                focusin {
                    event generate $path <FocusIn>
                }
                focusout {
                    event generate $path <FocusOut>
                }
                default {
                    fail "unknown event kind: $kind"
                }
            }
        }
        destroywin {
            destroy [lindex $words 1]
        }
        default {
            fail "unknown scenario command: $cmd"
        }
    }
}

foreach entry $::log {
    puts $entry
}
exit 0
