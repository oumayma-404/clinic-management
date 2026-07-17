using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Features.AI.Commands;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Backup.Commands;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.Notifications.Commands;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.ProcedureTypes.Commands;
using ClinicManagement.Application.Features.Stock.Commands;
using ClinicManagement.Application.Features.Users.Commands;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Behaviors;

/// <summary>
/// Pins the real-time resource-key contract. The keys asserted here are the exact strings the frontend
/// listens for in <c>web/lib/realtime/clinic-hub.ts</c> <c>RealtimeResource</c>; a client only refetches
/// when its key matches the broadcast key. Because the key is derived from the command's
/// <c>Features/&lt;Area&gt;</c> folder, renaming a folder silently changes the broadcast key with no compile
/// error — this test fails instead, forcing the frontend map to be updated in lock-step. It also documents
/// which command areas are intentionally excluded from broadcasting.
/// </summary>
public class RealtimeResourceResolverTests
{
    // Mutating commands → the resource key broadcast to clinic clients (MUST equal the frontend
    // RealtimeResource values). Sub-entities of a patient (dental record, medical history) map to
    // "patients" because they live under Features/Patients/Commands.
    [Theory]
    [InlineData(typeof(CreateAppointmentCommand), "appointments")]
    [InlineData(typeof(UpdateAppointmentCommand), "appointments")]
    [InlineData(typeof(CreatePatientCommand), "patients")]
    [InlineData(typeof(CreateDentalRecordCommand), "patients")]
    [InlineData(typeof(CreatePatientMedicalHistoryCommand), "patients")]
    [InlineData(typeof(CreateProcedureTypeCommand), "proceduretypes")]
    [InlineData(typeof(CreateMedicalDocumentCommand), "documents")]
    [InlineData(typeof(UploadPatientFileCommand), "files")]
    [InlineData(typeof(UpdateClinicCommand), "clinics")]
    [InlineData(typeof(SetUserActiveCommand), "users")]
    [InlineData(typeof(CreateStockItemCommand), "stock")]
    [InlineData(typeof(MarkNotificationReadCommand), "notifications")]
    [InlineData(typeof(CreateInvoiceCommand), "invoices")]
    public void Resolve_Maps_MutatingCommand_To_ExpectedResourceKey(Type command, string expected)
        => Assert.Equal(expected, RealtimeResourceResolver.Resolve(command));

    // Non-data command areas → null: a login, AI chat, or backup must not emit a refetch signal.
    [Theory]
    [InlineData(typeof(LoginCommand))]
    [InlineData(typeof(ChatCommand))]
    [InlineData(typeof(BackupNowCommand))]
    public void Resolve_Returns_Null_For_Excluded_Area(Type command)
        => Assert.Null(RealtimeResourceResolver.Resolve(command));

    // A query (not a .Commands namespace) is a read — it never broadcasts.
    [Fact]
    public void Resolve_Returns_Null_For_Query()
        => Assert.Null(RealtimeResourceResolver.Resolve(typeof(GetAppointmentsQuery)));
}
