namespace ClinicManagement.API.Maintenance;

/// <summary>
/// The one gate the database-reading console verbs share: <b>is there a database to connect to?</b>
///
/// <para><b>Why this replaced a deployment-profile check</b> (multi-tenant-cloud US-6, amendment M3).
/// <c>verify-schema</c>, <c>reconcile-money</c> and <c>reset-admin-password</c> used to refuse unless
/// <c>HasLocalDbTooling</c> — a capability about <c>pg_dump</c>/<c>pg_restore</c> being installed on the box,
/// which none of them runs. Their refusal messages already said « needs a direct database connection », so the
/// gate and the message disagreed, and the gate was the one that was wrong: it made <c>verify-schema</c>
/// unreachable in <see cref="Infrastructure.Deployment.DeploymentKind.HostedMultiTenant"/>, and that verb is the
/// <b>only</b> gate a schema change has in this product — nothing in the test project touches a database. The
/// same refusal locked a hosted clinic's admin out with no recovery once <c>provision-clinic</c> could create
/// one (US-3).</para>
///
/// <para>These verbs run in a hosted deployment as
/// <c>docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema</c>; the container's environment
/// is inherited, so <c>AddInstallLayers()</c> resolves the same connection string as the running app.</para>
///
/// <para>⚠️ <c>restore-backup</c> is deliberately <b>not</b> here and keeps its profile gate — see the reasoning
/// at its own call site.</para>
/// </summary>
public static class MaintenanceDatabase
{
    /// <summary>
    /// True when a connection string is configured; otherwise writes the operator message and returns false.
    /// </summary>
    public static bool HasConnectionString(IConfiguration configuration, string utilityDescription)
    {
        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
        {
            return true;
        }

        Console.Error.WriteLine(
            $"{utilityDescription} needs a database connection, and none is configured "
            + "(ConnectionStrings:DefaultConnection). Supply it through the environment "
            + "(ConnectionStrings__DefaultConnection) or the environment's appsettings file.");
        return false;
    }
}
