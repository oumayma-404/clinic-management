using ClinicManagement.Infrastructure.Relay;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Prints this build's identity — the string a PC de secours and its cloud must share to copy (<c>clinic-pc-copy</c>
/// D10). CI runs it on the API inside a freshly built installer and writes the answer into that installer's manifest,
/// so the cloud serves an installer only to the build it was made from. Usage:
///   ClinicManagement.API.exe relay-build
/// </summary>
/// <remarks>
/// Reads no configuration and touches no database: the identity is the newest migration and the assembly's own
/// version, both compiled in. ASCII only, so no console code page can garble what CI captures.
/// </remarks>
public static class RelayBuildConsoleCommand
{
    public const string CommandName = "relay-build";

    public static int Run()
    {
        Console.Out.Write(new RelayBuildInfo().Current);
        return 0;
    }
}
