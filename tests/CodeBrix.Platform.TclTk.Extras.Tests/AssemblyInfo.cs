using Xunit;

// The interpreter engine keeps significant process-global/static state (GlobalState),
// which is not safe to initialize from several interpreters concurrently. Run the
// tests sequentially so parallel interpreter creation does not race.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
