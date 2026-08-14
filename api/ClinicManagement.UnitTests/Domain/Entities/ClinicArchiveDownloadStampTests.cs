using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain.Entities;

/// <summary>
/// « Quand une archive de ce cabinet a-t-elle vraiment été téléchargée ? » — the fact the archive-staleness alert reads
/// (<c>clinic-recovery-points</c>).
///
/// <para>It is a column rather than a read over the audit ledger because « livrée » and « NON livrée » are both
/// <c>AuditAction.Update</c> rows differing only in French prose, so deriving it would mean matching a sentence — the
/// <c>Contains("déjà facturée")</c> defect this repository deleted.</para>
/// </summary>
public class ClinicArchiveDownloadStampTests
{
    private static Clinic NewClinic() => new(Guid.NewGuid(), "Cabinet Ben Ali");

    [Fact]
    public void A_Cabinet_That_Has_Never_Exported_Holds_No_Moment()
    {
        Assert.Null(NewClinic().LastArchiveDownloadedAtUtc);
    }

    [Fact]
    public void A_Delivered_Archive_Is_Recorded()
    {
        var clinic = NewClinic();
        var delivered = new DateTime(2026, 8, 14, 9, 30, 0, DateTimeKind.Utc);

        clinic.MarkArchiveDownloaded(delivered);

        Assert.Equal(delivered, clinic.LastArchiveDownloadedAtUtc);
    }

    // ⚠️ The load-bearing case. The delivery row is written AFTER the response body completes, outside the request
    // scope and best-effort, so two downloads started together can finish in either order — and the older one
    // landing last must not make the cabinet look staler than it is.
    [Fact]
    public void An_Older_Delivery_Landing_Late_Never_Moves_The_Moment_Backwards()
    {
        var clinic = NewClinic();
        var newer = new DateTime(2026, 8, 14, 11, 0, 0, DateTimeKind.Utc);
        var older = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);

        clinic.MarkArchiveDownloaded(newer);
        clinic.MarkArchiveDownloaded(older);

        Assert.Equal(newer, clinic.LastArchiveDownloadedAtUtc);
    }

    // ⚠️ Nobody EDITED the cabinet — « modifié le » on a settings screen reads as « quelqu'un a changé quelque
    // chose », and a download is not that.
    [Fact]
    public void Recording_A_Download_Is_Not_An_Edit_Of_The_Cabinet()
    {
        var clinic = NewClinic();
        var before = clinic.UpdatedAt;

        clinic.MarkArchiveDownloaded(DateTime.UtcNow);

        Assert.Equal(before, clinic.UpdatedAt);
    }
}
