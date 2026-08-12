using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// AC-9: a restore's rows are attributed to <b>a restore</b>, without losing the person who ran it
/// (<c>clinic-data-archive-and-restore</c>).
///
/// <para><b>Why a decoration and not a fourth kind of actor.</b> <c>RunAs</c> is deliberately ignored while a real
/// user is in scope — a helper running inside somebody's request must not claim their work — and a restore
/// <i>always</i> has one: an admin clicked it, or a console account did. Recording it as a bare process would
/// answer « ces trois mille fiches ont-elles été saisies ? » correctly and « qui a restauré ? » not at all, and the
/// ledger needs both. Hence <see cref="AuditActor.AsRestore"/> wrapping whoever was there.</para>
///
/// <para>⚠️ The case with real teeth is <see cref="A_Restore_Is_Honoured_Even_After_The_Actor_Has_Been_Read"/>:
/// both providers cache the identity on first read, so a declaration copied from <c>RunAs</c>'s first-read-wins
/// shape would be a silent no-op for a restorer that declares itself once and then writes in batches.</para>
/// </summary>
public class AuditActorRestoreTests
{
    private static readonly Guid ConsoleAccount = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static AuditActorProvider RequestProvider(string? userId, Guid? consoleAccountId = null)
    {
        var clinic = new Mock<IClinicContext>();
        clinic.Setup(c => c.GetUserId()).Returns(userId);
        clinic.Setup(c => c.GetUserEmail()).Returns("admin@cabinet.tn");

        var session = new Mock<IPlatformSessionContext>();
        session.Setup(s => s.GetAccountId()).Returns(consoleAccountId);
        session.Setup(s => s.GetEmail()).Returns((string?)null);

        return new AuditActorProvider(clinic.Object, session.Object);
    }

    // [AC-9] The signed-in admin is still named — the prefix is added, the identity is not replaced.
    [Fact]
    public void A_Restore_Decorates_The_Caller_Rather_Than_Replacing_Them()
    {
        var provider = RequestProvider("local|9c1f");

        provider.RestoringAnArchive();

        Assert.True(provider.Current.IsRestore);
        Assert.Equal($"{AuditActor.RestorePrefix}local|9c1f", provider.Current.UserId);
        Assert.Equal("admin@cabinet.tn", provider.Current.Email);
    }

    // [AC-9] The console path keeps its own kind too: a vendor restore is answerable in the cabinet's own journal
    // AND stays excluded from that cabinet's activity counters, which read the `console|` prefix.
    [Fact]
    public void A_Console_Restore_Is_Still_Recognisable_As_The_Console()
    {
        var provider = RequestProvider(userId: null, consoleAccountId: ConsoleAccount);

        provider.RestoringAnArchive();

        Assert.True(provider.Current.IsRestore);
        Assert.Contains(AuditActor.ConsolePrefix, provider.Current.UserId, StringComparison.Ordinal);
        Assert.Contains(ConsoleAccount.ToString(), provider.Current.UserId, StringComparison.Ordinal);
    }

    // ⚠️ Unlike RunAs, which first-read-wins. A restorer declares itself once and then writes table by table, so a
    // declaration honoured only before the first read would be inert on exactly the operation it exists for.
    [Fact]
    public void A_Restore_Is_Honoured_Even_After_The_Actor_Has_Been_Read()
    {
        var provider = RequestProvider("local|9c1f");

        Assert.False(provider.Current.IsRestore);

        provider.RestoringAnArchive();

        Assert.True(provider.Current.IsRestore);
    }

    // The same, for the console verbs' floor provider — the one a container built from AddInfrastructure alone
    // resolves. `provision-clinic`'s neighbour restoring a cabinet must not read as ordinary data entry either.
    [Fact]
    public void The_Process_Provider_Honours_A_Restore_After_Its_First_Read_Too()
    {
        var provider = new ProcessAuditActorProvider();
        provider.RunAs("restore-clinic");

        Assert.False(provider.Current.IsRestore);

        provider.RestoringAnArchive();

        Assert.True(provider.Current.IsRestore);
        Assert.Contains("restore-clinic", provider.Current.UserId, StringComparison.Ordinal);
    }

    // Idempotent: a nested declaration must not produce restore|restore|…, which no reader would match and the
    // ledger would render as an unknown kind of actor.
    [Fact]
    public void Declaring_A_Restore_Twice_Marks_It_Once()
    {
        var provider = RequestProvider("local|9c1f");

        provider.RestoringAnArchive();
        provider.RestoringAnArchive();

        Assert.Equal($"{AuditActor.RestorePrefix}local|9c1f", provider.Current.UserId);
        Assert.Equal(new AuditActor("local|9c1f", null).AsRestore().UserId,
            new AuditActor("local|9c1f", null).AsRestore().AsRestore().UserId);
    }

    // The three prefixes are distinct, and a restore is not mistaken for a background job: `job|` is what the
    // journal renders as « une tâche automatique », which a restore a person ran is not.
    [Fact]
    public void A_Restore_Is_Not_A_Process_And_Not_The_Console()
    {
        var restored = new AuditActor("local|9c1f", null).AsRestore();

        Assert.True(restored.IsRestore);
        Assert.False(restored.IsProcess);
        Assert.False(restored.IsConsole);
    }
}
