namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// Writes a small file so a crash never leaves half of it: a temporary file, then a move over the old one.
///
/// <para>⚠️ The move is retried. On Windows a file written a moment ago is often still held by an antivirus or the
/// indexer, and replacing it is refused with « Access denied » for a few milliseconds — hit by this feature's own
/// tests on the dev machine. Refusing would leave the PC de secours's position unrecorded after a step that
/// moved it.</para>
/// </summary>
internal static class AtomicFile
{
    private const int Attempts = 8;

    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException && attempt < Attempts)
            {
                Thread.Sleep(25 * attempt);
            }
        }
    }
}
