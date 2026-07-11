# =============================================================================
# DRAKON.Brix bootstrap
#
# This is a NEW file, authored for the DRAKON.Brix port. It is NOT a modified
# copy of any DRAKON-Editor file (stock DRAKON has no bootstrap.tcl). It is the
# ONLY Tcl that runs before the UNMODIFIED drakon_editor.tcl is sourced, and it
# exists purely to reproduce the environment that stock DRAKON assumes so that
# drakon_editor.tcl's own (unchanged) startup code succeeds.
#
# Everything below is glue that was ADDED for this port. Each block is tagged
#   "Added for DRAKON.Brix - because <reason>"
# and shows, as a "Stock DRAKON / Tcl:" reference comment, the mechanism it
# stands in for. The invariant marker text "for DRAKON.Brix" is searchable:
# grep it (and its sibling "Removed for DRAKON.Brix", used when we ever have to
# edit a genuinely-vendored original file) to find every port-specific change.
#
# Already handled in C# before this file runs, so nothing is needed here for
# them:
#   * Tk / Img packages + ::tcl_version / ::tk_version / ::tk_patchLevel
#       -> CodeBrix.Platform.TkCanvas.TkBootstrap.Register
#   * sqlite3 and pdf4tcl commands + "package provide"
#       -> CodeBrix.Platform.TclTk.Extras.TclTkExtras.RegisterAll
# =============================================================================


# Added for DRAKON.Brix - because stock DRAKON's [package require snit] loads
#   the ~3000-line Tcl "snit" object package from tcllib. snit's ONLY consumer
#   was pdf4tcl, whose implementation is now the managed CodeBrix Extras shim,
#   so the package only has to be PRESENT to satisfy drakon_editor.tcl's line-1
#   [require snit] gate — never actually used. An empty provide is enough, and
#   it keeps tcllib out of the app.
#
# Stock DRAKON / Tcl:
#   package require snit          ;# pulls the real snit implementation from tcllib
package provide snit 2.3.2


# Added for DRAKON.Brix - because stock DRAKON's [package require msgcat] loads
#   tcllib's message-catalog package, then drakon_editor.tcl does
#   [namespace import ::msgcat::mc], [::msgcat::mclocale ru] and
#   [::msgcat::mcload msgs]. DRAKON ships an English source UI plus a Russian
#   catalog (msgs/ru.msg, 342 [mcset ru ...] entries); its mc2 helper returns
#   the source text verbatim when the language is "English" and routes through
#   [mc] otherwise. To honor DRAKON's Russian heritage we ship BOTH languages,
#   so this is a real (if minimal) message-catalog implementation -- exactly
#   the msgcat surface DRAKON touches -- rather than an English-only stub. It
#   stays self-contained (no tcllib). Locale/preference/lookup semantics follow
#   tcllib msgcat: a lookup walks the locale preference list (e.g. ru then the
#   root ""), falling back to the source string when no translation exists, and
#   [mc] applies [format] only when substitution args are supplied (DRAKON
#   never passes any -- it does its own [$var] substitution in mc2 -- so a
#   literal "%" or "$count" in a message is left untouched).
#
# Stock DRAKON / Tcl:
#   package require msgcat        ;# pulls the real msgcat implementation from tcllib
namespace eval ::msgcat {
    variable Locale ""
    variable Loclist {}
    variable Msgs
    array set Msgs {}

    # Set (or query) the locale and rebuild the preference list. mclocale ru
    # yields the preferences {ru ""}: try Russian, then the root catalog.
    proc mclocale { args } {
        variable Locale
        variable Loclist
        if { [llength $args] == 0 } { return $Locale }
        set new [string tolower [lindex $args 0]]
        set Loclist {}
        set accum ""
        foreach part [split $new _] {
            set accum [string trimleft "${accum}_${part}" _]
            set Loclist [linsert $Loclist 0 $accum]
        }
        lappend Loclist ""
        set Locale [lindex $Loclist 0]
        return $Locale
    }

    proc mcpreferences {} { variable Loclist; return $Loclist }

    # Store a translation. Called ~342 times while sourcing msgs/ru.msg.
    proc mcset { locale src {dst ""} } {
        variable Msgs
        if { [llength [info level 0]] < 4 } { set dst $src }
        set Msgs([string tolower $locale],$src) $dst
        return $dst
    }

    # Translate: walk the preference list; fall back to the source string.
    proc mc { src args } {
        variable Msgs
        variable Loclist
        foreach loc $Loclist {
            if { [info exists Msgs($loc,$src)] } {
                if { [llength $args] } { return [format $Msgs($loc,$src) {*}$args] }
                return $Msgs($loc,$src)
            }
        }
        if { [llength $args] } { return [format $src {*}$args] }
        return $src
    }

    # Source every catalog file matching the locale preference list. The root
    # locale "" maps to ROOT.msg (tcllib's convention). Files are UTF-8.
    proc mcload { dir } {
        variable Loclist
        set count 0
        foreach loc $Loclist {
            if { $loc eq "" } {
                set file [file join $dir ROOT.msg]
            } else {
                set file [file join $dir $loc.msg]
            }
            if { [file exists $file] } {
                incr count
                uplevel #0 [list source -encoding utf-8 $file]
            }
        }
        return $count
    }

    # Longest of the translated forms of the given source strings (used to
    # size widgets). Mirrors tcllib: translate first, then measure.
    proc mcmax { args } {
        set max 0
        foreach s $args {
            set n [string length [mc $s]]
            if { $n > $max } { set max $n }
        }
        return $max
    }

    namespace export mc mcmax mclocale mcpreferences mcset mcload
}
package provide msgcat 1.6


# Added for DRAKON.Brix - because stock DRAKON is launched as
#   "tclsh8.6 drakon_editor.tcl ?FILE?", so tclsh populates ::argc / ::argv /
#   ::argv0 from the process command line, and drakon_editor.tcl's
#   [start_up $argc $argv] opens FILE (or shows the intro when there is none).
#   The port has no command line, so the host passes the file to open via the
#   DRAKONBRIX_OPEN environment variable and we build the same globals here.
#
# Stock DRAKON / Tcl:
#   (::argc / ::argv / ::argv0 are set automatically by tclsh from argv;
#    no code in drakon_editor.tcl sets them.)
if { [info exists ::env(DRAKONBRIX_OPEN)] && $::env(DRAKONBRIX_OPEN) ne "" } {
    set ::argc 1
    set ::argv [list $::env(DRAKONBRIX_OPEN)]
} else {
    set ::argc 0
    set ::argv {}
}
set ::argv0 drakon_editor.tcl


# Added for DRAKON.Brix - because stock DRAKON's File > Quit menu item (and its
#   file-error paths) run the bare [exit] command, which under tclsh terminates
#   the process. The managed engine's own [exit] only marks the interpreter as
#   exited; in this hosted UI app the interpreter runs on a dedicated Tcl thread
#   while the UI thread keeps the process alive, so a bare [exit] would hang.
#   This proc shadows [exit] onto the host's __drakonbrix_quit command (see
#   Drakon/QuitCommand.cs), which calls Environment.Exit -- matching tclsh's
#   process-terminating exit semantics.
#
# Stock DRAKON / Tcl:
#   exit                          ;# tclsh's built-in, terminates the process
proc exit { args } {
    __drakonbrix_quit {*}$args
}
