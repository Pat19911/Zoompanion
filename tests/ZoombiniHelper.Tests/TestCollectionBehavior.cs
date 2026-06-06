// The localization tests mutate the process-wide Loc.Current. Other tests
// (e.g. CliffStateTests) assert German output and assume the default language.
// Disabling parallelization keeps those from interleaving with a temporary
// language switch; the localization tests still restore Loc.Current in a
// finally block as a second line of defense.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
