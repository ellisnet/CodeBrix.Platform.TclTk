using Xunit;

// The toolkit hosts CodeBrix.Platform.TclTk interpreters, whose engine keeps
// significant process-global/static state (GlobalState) that is not safe to
// initialize from several interpreters concurrently. Run the tests
// sequentially so parallel interpreter creation does not race.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
