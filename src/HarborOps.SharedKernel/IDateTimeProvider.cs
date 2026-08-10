namespace HarborOps.SharedKernel;

/// <summary>
/// Supplies the current UTC instant. Exists so application code depends on an
/// abstraction instead of <see cref="System.DateTime.UtcNow"/> directly, keeping a
/// single consistent "now" per request and allowing it to be substituted in tests.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
