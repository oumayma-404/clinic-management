using System.Reflection;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Storage;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Storage;

/// <summary>
/// <see cref="ClinicStorageKey"/> — the single composer of a new blob's key (multi-tenant-cloud US-5), and the
/// derived guard that keeps it single.
///
/// <para>The guard matters more than the format cases: what US-5 buys is that an <b>unprefixed key is not
/// something a caller can write</b>, and that property lives in <see cref="IFileStorage"/>'s signatures rather
/// than in anybody's discipline. A third upload overload added without a clinic id would restore the old defect
/// silently, so the assertion is reflected off the interface — never a list of the overloads that exist today.</para>
/// </summary>
public class ClinicStorageKeyTests
{
    private static readonly Guid Clinic = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // [US-5] Every upload the interface offers names its clinic — derived from IFileStorage, not from a list.
    [Fact]
    public void Every_Upload_Overload_Requires_A_Clinic_Id()
    {
        var uploads = typeof(IFileStorage)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(IFileStorage.UploadAsync))
            .ToList();

        Assert.NotEmpty(uploads);

        foreach (var upload in uploads)
        {
            Assert.Contains(upload.GetParameters(), p => p.ParameterType == typeof(Guid));
        }
    }

    // [US-5] The generated key: clinics/{clinicId}/ then a unique leaf.
    [Fact]
    public void A_Generated_Key_Is_Prefixed_And_Unique()
    {
        var first = ClinicStorageKey.Compose(Clinic);
        var second = ClinicStorageKey.Compose(Clinic);

        Assert.StartsWith($"clinics/{Clinic}/", first);
        Assert.NotEqual(first, second);
    }

    // [US-5] A caller's path is placed under its clinic, never beside it — the callers pass a clinic-RELATIVE
    // path now, so a leading clinic segment would produce clinics/{id}/{id}/logo.
    [Theory]
    [InlineData("logo")]
    [InlineData("e-invoices/2026-0001-signed.xml")]
    public void A_Relative_Path_Is_Placed_Under_Its_Clinic(string relativePath)
    {
        Assert.Equal($"clinics/{Clinic}/{relativePath}", ClinicStorageKey.Compose(Clinic, relativePath));
    }

    // [US-5] Blank means « give me a unique key », which is what the four callers with no path of their own pass.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Path_Falls_Back_To_A_Generated_Leaf(string? relativePath)
    {
        var key = ClinicStorageKey.Compose(Clinic, relativePath);

        Assert.StartsWith($"clinics/{Clinic}/", key);
        Assert.NotEqual($"clinics/{Clinic}/", key);
    }

    // [US-5] An empty clinic id fails HERE. clinics/00000000-…/ is a folder nothing would ever look in again,
    // and the read that discovers it happens months later with no way back to the write that caused it.
    [Fact]
    public void An_Empty_Clinic_Id_Is_Refused()
    {
        Assert.Throws<InvalidOperationException>(() => ClinicStorageKey.Compose(Guid.Empty, "logo"));
    }

    // [US-5] A path cannot climb out of its own clinic. MinIO has no traversal semantics and would have stored
    // the literal name, so refusing in the composer is what keeps the two backends agreeing on what a key means.
    [Theory]
    [InlineData("../other-clinic/logo")]
    [InlineData("doctors/../../escape")]
    [InlineData("/absolute")]
    [InlineData("..\\windows-style")]
    public void A_Path_That_Escapes_Its_Clinic_Is_Refused(string relativePath)
    {
        Assert.Throws<InvalidOperationException>(() => ClinicStorageKey.Compose(Clinic, relativePath));
    }
}
