using Xunit;

// Store and Actions are static, so tests must not run at the same time.
// The UI tests set the same in Ui/UiHost.cs (their own file, because they use another xUnit version).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
