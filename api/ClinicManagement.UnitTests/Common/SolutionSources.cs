using System.Runtime.CompilerServices;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// Locating and reading the solution's own C# sources, for the guards that are derived by scanning them.
///
/// <para>Shared rather than copied: the <c>bin</c>/<c>obj</c> rule below is a lesson each copy would have to
/// learn again, and a guard that crashes while enumerating is a guard that never asserts.</para>
/// </summary>
internal static class SolutionSources
{
    /// <summary>
    /// The solution directory, found from this file's own compile-time path. Deliberately NOT
    /// <c>AppContext.BaseDirectory</c>: the suite is routinely built to an output directory outside the
    /// repository (the Smart App Control workaround), which would make a walk-up from the binary fail.
    /// </summary>
    public static DirectoryInfo Root([CallerFilePath] string thisFile = "")
    {
        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClinicManagement.sln")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate ClinicManagement.sln by walking up from '{thisFile}'. Guards that read sources must "
            + "fail rather than silently pass when they cannot find them.");
    }

    /// <summary>
    /// Every C# source under <paramref name="root"/>, skipping build output.
    ///
    /// <para>⚠️ <c>bin</c>/<c>obj</c> are skipped by <b>not descending into them</b>, not by filtering the
    /// results: <c>obj/</c> holds generated copies of the sources (which would double every hit), and
    /// <c>EnumerateFiles(…, AllDirectories)</c> uses the legacy options where <c>IgnoreInaccessible</c> is
    /// <c>false</c> — so on a machine where <c>harden-permissions</c> has ACL'd a backup folder inside
    /// <c>bin/</c>, enumerating first and filtering after throws <see cref="UnauthorizedAccessException"/>
    /// before any assertion runs.</para>
    /// </summary>
    public static IEnumerable<string> CsFiles(DirectoryInfo root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var child in directory.EnumerateDirectories())
            {
                if (child.Name is "bin" or "obj")
                {
                    continue;
                }

                pending.Push(child);
            }

            foreach (var file in directory.EnumerateFiles("*.cs"))
            {
                yield return file.FullName;
            }
        }
    }
}
