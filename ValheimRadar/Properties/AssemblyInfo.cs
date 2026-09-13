using System.Runtime.CompilerServices;

// Extends the module's *test* visibility only, not its public API - lets ValheimRadar.Tests
// exercise pure internal helpers (e.g. ObjectEvaluator's private-by-default name/lookup logic)
// directly instead of only through the live-GameObject entry points that wrap them.
[assembly: InternalsVisibleTo("ValheimRadar.Tests")]
