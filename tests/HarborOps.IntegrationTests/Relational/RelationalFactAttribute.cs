using Microsoft.Data.SqlClient;

namespace HarborOps.IntegrationTests.Relational;

public sealed class RelationalFactAttribute : FactAttribute
{
    public RelationalFactAttribute()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            Skip = SqlServerAvailability.UnavailableReason;
        }
    }
}

internal static class SqlServerAvailability
{
    internal const string MasterConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;"
        + "TrustServerCertificate=true;Connect Timeout=10";

    private const string UnavailableMessage =
        "SQL Server LocalDB is not reachable, so relational guarantees cannot be verified here.";

    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);
    private static readonly Lock ProbeLock = new();
    private static bool available;
    private static DateTime lastFailureUtc = DateTime.MinValue;
    private static string? lastFailureDetail;

    internal static string UnavailableReason
    {
        get
        {
            lock (ProbeLock)
            {
                return lastFailureDetail is null
                    ? UnavailableMessage
                    : $"{UnavailableMessage} {lastFailureDetail}";
            }
        }
    }

    // A successful probe is remembered for the process lifetime. A failed probe is retried
    // after a short delay so a long-lived test host does not keep skipping every relational
    // test after LocalDB was briefly unavailable (for example during its idle auto-shutdown).
    internal static bool IsAvailable
    {
        get
        {
            lock (ProbeLock)
            {
                if (available)
                {
                    return true;
                }

                if (DateTime.UtcNow - lastFailureUtc < RetryAfterFailure)
                {
                    return false;
                }

                available = TryConnect();
                if (!available)
                {
                    lastFailureUtc = DateTime.UtcNow;
                }

                return available;
            }
        }
    }

    internal static string ConnectionStringFor(string databaseName)
    {
        return $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;"
            + "TrustServerCertificate=true;Connect Timeout=30";
    }

    private static bool TryConnect()
    {
        try
        {
            using var connection = new SqlConnection(MasterConnectionString);
            connection.Open();

            return true;
        }
        catch (Exception exception) when (exception
            is SqlException
            or InvalidOperationException
            or PlatformNotSupportedException)
        {
            lastFailureDetail = $"{exception.GetType().Name}: {exception.Message}";

            return false;
        }
    }
}
