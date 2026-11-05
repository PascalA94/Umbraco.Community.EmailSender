using NUnit.Framework;

/// <summary>
/// Umbraco's integration test framework reads its configuration in a <c>[SetUpFixture]</c> that lives in its own
/// assembly, which NUnit does not run for ours. This fixture (global namespace, so it covers every test here)
/// runs it. Pattern from the Umbraco docs on integration testing packages.
/// </summary>
[SetUpFixture]
public class EmailSenderGlobalSetup
{
    private readonly GlobalSetupTeardown _inner = new();

    [OneTimeSetUp]
    public void SetUp() => _inner.SetUp();

    [OneTimeTearDown]
    public void TearDown() => _inner.TearDown();
}
