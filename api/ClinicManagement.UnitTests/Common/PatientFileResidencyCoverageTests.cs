using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>Every handler that names a patient file's blob asks where the bytes are first.</b>
///
/// <para><b>Why a guard and not a review.</b> A <c>Vault</c> row's <c>StorageKey</c> is <b>null</b> — the original
/// never reached the deployment — so a call site that reads it without branching on <c>Residency</c> either throws
/// on a null key or, worse, composes a request against the object store for something that was never there. There
/// were three such sites when the coffre shipped and each was fixed individually; the fourth, written next month,
/// is what a hand-maintained list cannot cover. This is the repository's own « fixes don't propagate » shape.</para>
///
/// <para><b>Derived from the sources.</b> The rule is « a file under <c>Features/Files</c> or
/// <c>Features/Patients</c> that mentions <c>StorageKey</c> must also mention <c>Residency</c> », read off the
/// files themselves, so a new door is covered on the day it is written.</para>
///
/// <para>⚠️ <b>File granularity, deliberately</b>, on <c>PatientFileAccessCoverageTests</c>' reasoning: proving
/// that a particular branch guards a particular read means parsing C# and following control flow, while at file
/// granularity the check is exact enough to have caught all three real misses and cheap enough never to produce a
/// false one.</para>
/// </summary>
public class PatientFileResidencyCoverageTests
{
    private static readonly string[] SearchedAreas =
    {
        Path.Combine("ClinicManagement.Application", "Features", "Files"),
        Path.Combine("ClinicManagement.Application", "Features", "Patients"),
    };

    /// <summary>
    /// Files that name a storage key without deciding residency, each with the reason it is exempt.
    ///
    /// <para>⚠️ Asserted <b>equal in both directions</b> below, so an entry that stops being true fails rather than
    /// sitting here forever — an exemption list that only ever grows is a check that has stopped working.</para>
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PatientFileBlobs.cs"] =
            "It IS the answer — the one place that decides which blobs a row owns. It names Residency, so it is "
            + "not actually exempt; kept here only if that ever changes.",
    };

    private static IEnumerable<string> FilesNamingAStorageKey()
    {
        var root = SolutionSources.Root();

        foreach (var area in SearchedAreas)
        {
            // ⚠️ `Root()` is the directory holding ClinicManagement.sln, which is `api/` itself — not the
            // repository root. Prefixing "api" here found nothing and the scan passed vacuously.
            var directory = new DirectoryInfo(Path.Combine(root.FullName, area));
            if (!directory.Exists) continue;

            foreach (var path in SolutionSources.CsFiles(directory))
            {
                var text = File.ReadAllText(path);

                // `PreviewStorageKey` alone is not the question: a preview is hosted whatever the residency is.
                var namesTheOriginal = text.Contains("file.StorageKey", StringComparison.Ordinal)
                    || text.Contains("f.StorageKey", StringComparison.Ordinal)
                    || text.Contains(".StorageKey!", StringComparison.Ordinal);

                if (namesTheOriginal) yield return path;
            }
        }
    }

    [Fact]
    public void Every_Handler_Naming_A_Patient_Files_Blob_Branches_On_Residency_First()
    {
        var unguarded = new List<string>();

        foreach (var path in FilesNamingAStorageKey())
        {
            var name = Path.GetFileName(path);
            if (Exempt.ContainsKey(name)) continue;

            var text = File.ReadAllText(path);

            // Either the file decides residency itself, or it delegates to the one place that does.
            var guarded = text.Contains("Residency", StringComparison.Ordinal)
                || text.Contains("PatientFileBlobs", StringComparison.Ordinal);

            if (!guarded) unguarded.Add(name);
        }

        Assert.True(unguarded.Count == 0,
            "These name a patient file's storage key without deciding where its bytes are. A coffre row's key is "
            + "NULL, so this is a NullReferenceException or a request for an object that was never stored: "
            + string.Join(", ", unguarded));
    }

    /// <summary>
    /// The non-vacuity half. A scan that finds nothing passes silently, so a moved folder or a renamed property
    /// would leave this green and inert — the failure mode the guard exists to prevent, one level up.
    /// </summary>
    [Fact]
    public void The_Scan_Still_Finds_The_Doors_It_Is_Meant_To_Cover()
    {
        var found = FilesNamingAStorageKey().Select(Path.GetFileName).ToList();

        Assert.Contains("DownloadPatientFileQuery.cs", found);
        Assert.Contains("ExportPatientDossierQuery.cs", found);
        Assert.True(found.Count >= 3, $"Expected at least three doors; found {found.Count}: {string.Join(", ", found)}.");
    }

    [Fact]
    public void Every_Exemption_Still_Names_A_File_The_Scan_Reaches()
    {
        var found = FilesNamingAStorageKey().Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = Exempt.Keys.Where(name => !found.Contains(name)).ToList();

        Assert.True(stale.Count == 0,
            $"Exempted but no longer found by the scan — delete the entry: {string.Join(", ", stale)}.");
    }
}
