using Xunit;

// Avalonia.Headless spins up an isolated Application per test on its own thread. When several test
// collections initialize concurrently they race on the render-loop dispatcher, and one throws
// "The calling thread cannot access this object because a different thread owns it" from
// AvaloniaHeadlessPlatform.Initialize — failing a random, unrelated test during its setup. Serializing
// the collections makes each headless application initialize alone, which removes the flake. The suite
// still runs in a few seconds; headless UI work is single-threaded anyway.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
