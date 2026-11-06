namespace Yeelight.Tests.Chaos
{
    using Xunit;

    /// <summary>
    /// The soak tests run one at a time, and alone: they count the threads and the handles of the process, and the exceptions that nobody
    /// observed, which the tests that run beside them would change.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class SoakCollection
    {
        public const string Name = "Soak";
    }
}
