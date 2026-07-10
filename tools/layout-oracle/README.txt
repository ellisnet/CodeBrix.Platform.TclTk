layout-oracle -- DEV-ONLY tooling for the TkCanvas geometry-manager tests
==========================================================================

The pack/grid layout engines in src/CodeBrix.Platform.TkCanvas are verified
against REAL Tk (wish 8.6) as a behavior oracle. Scenario files describing a
window tree + pack/grid arrangement live in

    tests/CodeBrix.Platform.TkCanvas.Tests/Assets/LayoutOracle/*.scenario

and next to each one a *.expected fixture holds the geometry that real wish
produced for it (one line per window: PATH x y width height reqwidth
reqheight ismapped). The xUnit oracle tests replay every scenario through the
TkCanvas engines headlessly and compare line-by-line -- they never run wish.

Files here:

  capture_layout.tcl        Builds one scenario in wish and dumps geometry.
                            The scenario line format is documented at the top
                            (kept in sync with the C# parser in
                            tests/.../Oracle/OracleScenario.cs).
  generate_fixtures.sh      Regenerates every *.expected from its *.scenario
                            by running wish. Requires tk + a display (X11);
                            windows are created off-screen and unmanaged, so
                            nothing visibly flashes.
  random_pack_scenarios.tcl Deterministic (seeded) random pack scenario
                            generator; 1NN_random.scenario files.
  random_grid_scenarios.tcl Deterministic (seeded) random grid scenario
                            generator; 2NN_random_grid.scenario files.

Regenerating fixtures is only needed when scenarios change or a new Tk
version becomes the reference oracle:

    tools/layout-oracle/generate_fixtures.sh

Scenario-authoring rules:

  * Windows before use; parents before children.
  * Never packpropagate/gridpropagate on "." with no rootsize -- a real Tk
    toplevel then keeps its 200x200 default, which the headless engine does
    not model. Use interior fixed-size frames to test propagate-off, or give
    the root an explicit rootsize.
  * Oracle version: Tk 8.6.16 (tclsh/wish from Debian; patchlevel is echoed
    by generate_fixtures.sh when capturing).
