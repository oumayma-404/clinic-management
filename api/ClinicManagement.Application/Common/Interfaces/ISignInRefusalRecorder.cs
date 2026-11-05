using ClinicManagement.Application.Common.SignIn;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Writes a refused sign-in down and decides whether the vendor is told. Implemented in Infrastructure, which knows
/// the caller's address. It never throws and never waits on the e-mail it may send.
/// </summary>
public interface ISignInRefusalRecorder
{
    void Record(SignInRefusal refusal);
}
