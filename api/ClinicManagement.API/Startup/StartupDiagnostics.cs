using System.Net.Sockets;
using Microsoft.AspNetCore.Connections;
using Serilog;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Classifies and reports operator-facing server-startup failures (FR-F5 / AC-8.3, Phase 5 S2). Two
/// failure modes must produce clear, distinct messages instead of a silent crash: the database being
/// unreachable (service down / wrong host) and the HTTP(S) port already being in use. The classification
/// is a pure, unit-testable function; <see cref="ReportFatal"/> fans a message out to the console, the
/// Serilog log, and — best-effort, Windows-only — the Windows Event Log.
/// </summary>
/// <remarks>Only invoked on the Local-mode startup path; Cloud keeps its prior fatal-rethrow (R-9).</remarks>
public static class StartupDiagnostics
{
    private const string EventLogSource = "Clinic Management";
    private const string EventLogName = "Application";

    /// <summary>
    /// True when <paramref name="ex"/> (or an inner exception) indicates the database could not be reached
    /// — a transport-level failure (socket / timeout), not a server error like bad credentials.
    /// </summary>
    public static bool IsDatabaseConnectionFailure(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is SocketException || e is TimeoutException)
            {
                return true;
            }

            // Npgsql raises a bare NpgsqlException (inner SocketException) when it cannot connect; a
            // PostgresException means the server answered (auth/permissions) and is NOT "DB down".
            var typeName = e.GetType().FullName;
            if (typeName == "Npgsql.NpgsqlException")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="ex"/> (or an inner exception) indicates the bind port is already in use.
    /// </summary>
    public static bool IsAddressInUse(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is AddressInUseException)
            {
                return true;
            }

            if (e is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Clear operator message for an unreachable database.</summary>
    public static string DatabaseUnreachableMessage() =>
        "La base de données n'est pas joignable. Vérifiez que le service PostgreSQL est démarré " +
        "et que la chaîne de connexion (ConnectionStrings:DefaultConnection) est correcte.";

    /// <summary>Clear operator message for a port already in use, naming the port.</summary>
    public static string PortInUseMessage(int port) =>
        $"Le port {port} est déjà utilisé par un autre programme. Arrêtez le programme qui l'utilise " +
        $"ou configurez un autre port (Hosting:HttpsPort / Hosting:HttpPort), puis redémarrez le serveur.";

    /// <summary>
    /// Fans a fatal startup message out to the console, Serilog, and (best-effort, Windows-only) the
    /// Windows Event Log so the operator sees it however they are watching the server.
    /// </summary>
    public static void ReportFatal(string message, Exception? ex = null)
    {
        Console.Error.WriteLine(message);

        if (ex != null)
        {
            Log.Fatal(ex, "{StartupFailure}", message);
        }
        else
        {
            Log.Fatal("{StartupFailure}", message);
        }

        WriteToEventLog(message);
    }

    private static void WriteToEventLog(string message)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (!System.Diagnostics.EventLog.SourceExists(EventLogSource))
            {
                System.Diagnostics.EventLog.CreateEventSource(EventLogSource, EventLogName);
            }

            System.Diagnostics.EventLog.WriteEntry(
                EventLogSource, message, System.Diagnostics.EventLogEntryType.Error);
        }
        catch
        {
            // Writing to (or creating) an Event Log source can require elevation. The message is already
            // on the console and in the Serilog log, so a failure here must never mask the real error.
        }
    }
}
