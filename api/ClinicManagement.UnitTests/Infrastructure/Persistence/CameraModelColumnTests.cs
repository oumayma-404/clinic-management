using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// The camera model's column is as long as the domain's own clamp (<see cref="PhotoCaptureFacts.MaxCameraModelLength"/>),
/// on both tables that carry it, in the live model AND in the migrations' snapshot (<c>features/camera-photo-import</c>).
///
/// <para>The domain truncates to the constant and the column was configured with a literal <c>100</c> beside it: equal
/// today, and the day the constant moved the domain would hand PostgreSQL a value its column refuses, at the insert,
/// on the photo import. Reading the snapshot too is what makes a constant changed without its migration fail here
/// rather than in <c>verify-schema</c> on a deployed database. Model-only — no connection is opened.</para>
/// </summary>
public class CameraModelColumnTests
{
    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none")
            .Options, null);

    [Theory]
    [InlineData(typeof(PatientFile))]
    [InlineData(typeof(FileUploadSession))]
    public void The_Camera_Model_Column_Holds_What_The_Domain_Keeps(Type entity)
    {
        using var db = Context();

        var live = db.Model.FindEntityType(entity)!.FindProperty("CameraModel")!.GetMaxLength();
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model
            .FindEntityType(entity.FullName!)!.FindProperty("CameraModel")!.GetMaxLength();

        Assert.Equal(PhotoCaptureFacts.MaxCameraModelLength, live);
        Assert.Equal(PhotoCaptureFacts.MaxCameraModelLength, snapshot);
    }
}
