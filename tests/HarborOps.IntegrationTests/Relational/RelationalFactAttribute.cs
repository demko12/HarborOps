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

    internal const string UnavailableReason =
        "SQL Server LocalDB is not reachable, so relational guarantees cannot be verified here.";

    private static readonly Lazy<bool> Probe = new(TryConnect, isThreadSafe: true);

    internal static bool IsAvailable => Probe.Value;

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
        catch (SqlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
