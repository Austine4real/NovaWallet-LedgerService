using Xunit;

// All of these tests hit the same shared SQL Server instance with real
// connections and real row/advisory locks. Letting xUnit run different test
// classes in parallel (its default) means multiple test classes compete for
// the same connection pool at once - on top of the concurrency tests already
// opening dozens of simultaneous connections themselves - which is exactly
// what produced the intermittent "wait operation timed out" connection-pool
// exhaustion seen when running the full suite. Disabling cross-class
// parallelization trades a few extra seconds of total run time for
// consistently reproducible results.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
