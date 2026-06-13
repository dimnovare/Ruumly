using Xunit;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Groups every test class that mutates Hangfire's process-wide static
/// <see cref="Hangfire.JobStorage.Current"/> into one xUnit collection so they never run
/// concurrently.
///
/// xUnit parallelizes distinct test classes (each class is its own collection by default).
/// Because JobStorage.Current is a single static, parallel classes raced it: one class would
/// reassign JobStorage.Current between another class enqueuing a job and asserting on it,
/// producing intermittent "Expected jobs to contain a single item, but the collection is empty"
/// failures that only surfaced under the full suite, never in isolation.
///
/// Sharing one named collection serializes these classes relative to each other;
/// DisableParallelization also keeps the collection from overlapping any other collection,
/// guarding against future classes that touch the same global.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HangfireCollection
{
    public const string Name = "Hangfire global JobStorage";
}
