# capture_theming.tcl -- run a B.12 theming scenario in REAL Tk (wish) and
# dump the query results: the option database (option add/get), ttk::style
# (configure/map/lookup/theme), and tk_setPalette/tk_bisque derived shades.
#
# Usage:   wish capture_theming.tcl <scenario-file>   > <fixture-file>
#
# Scenario line format (each line is a Tcl list; also parsed by the C# test
# driver -- keep in sync with
# tests/CodeBrix.Platform.TkCanvas.Tests/Oracle/ThemingOracleScenario.cs):
#
#   appname NAME                         tk appname NAME (root-level matching)
#   frame PATH CLASS                     create frame PATH -class CLASS
#   add PATTERN VALUE PRIORITY           option add
#   optclear                             option clear
#   get PATH NAME CLASS                  option get -> "get PATH NAME CLASS => v"
#   configure STYLE OPTION VALUE         ttk::style configure
#   map STYLE OPTION PAIRS               ttk::style map (PAIRS is one list word)
#   lookup STYLE OPTION ?STATES? ?DEF?   ttk::style lookup -> "lookup ... => v"
#   theme_create NAME ?PARENT?           ttk::style theme create
#   theme_use NAME                       ttk::style theme use
#   palette ARG ?ARG ...?                tk_setPalette (one color or pairs)
#   bisque                               tk_bisque
#   query OPTION                         option get . OPTION Class -> "query OPTION => v"
#
# NOTE: fixture scenarios must only read back values the scenario itself
# configures (never wish's built-in ttk theme settings, which our engine does
# not replicate), and must stick to hex colors / the shared TkColor name set.
# Ambient X resources (RESOURCE_MANAGER) could in principle leak into option
# queries; regenerate on a clean session if a capture looks polluted.

proc fail {msg} {
    puts stderr $msg
    exit 1
}

if {$argc != 1} {
    fail "usage: wish capture_theming.tcl <scenario-file>"
}

set f [open [lindex $argv 0] r]
set lines [split [read $f] "\n"]
close $f

wm withdraw .

proc ucfirst {s} {
    return [string toupper [string index $s 0]][string range $s 1 end]
}

foreach line $lines {
    set line [string trim $line]
    if {$line eq "" || [string index $line 0] eq "#"} { continue }
    set words [lrange $line 0 end]
    set cmd [lindex $words 0]
    switch -- $cmd {
        appname      { tk appname [lindex $words 1] }
        frame        { frame [lindex $words 1] -class [lindex $words 2] }
        add          { option add [lindex $words 1] [lindex $words 2] [lindex $words 3] }
        optclear     { option clear }
        get          {
            lassign $words - path name class
            puts "get $path $name $class => [option get $path $name $class]"
        }
        configure    { ttk::style configure [lindex $words 1] [lindex $words 2] [lindex $words 3] }
        map          { ttk::style map [lindex $words 1] [lindex $words 2] [lindex $words 3] }
        lookup       {
            lassign $words - style option states default
            if {[llength $words] == 3} {
                puts "lookup $style $option => [ttk::style lookup $style $option]"
            } elseif {[llength $words] == 4} {
                puts "lookup $style $option $states => [ttk::style lookup $style $option $states]"
            } else {
                puts "lookup $style $option $states $default => [ttk::style lookup $style $option $states $default]"
            }
        }
        theme_create {
            if {[llength $words] == 3} {
                ttk::style theme create [lindex $words 1] -parent [lindex $words 2]
            } else {
                ttk::style theme create [lindex $words 1]
            }
        }
        theme_use    { ttk::style theme use [lindex $words 1] }
        palette      { tk_setPalette {*}[lrange $words 1 end] }
        bisque       { tk_bisque }
        query        {
            set opt [lindex $words 1]
            puts "query $opt => [option get . $opt [ucfirst $opt]]"
        }
        default      { fail "unknown scenario command: $cmd" }
    }
}
exit
