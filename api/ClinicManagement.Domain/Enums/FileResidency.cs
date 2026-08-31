namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Where a stored file's bytes actually live.
///
/// <para>The distinction exists because a hosted deployment pays for every byte more than once — the object
/// store holds one copy, the nightly backup tars the whole store onto the same disk, and an off-site copy
/// leaves every night — while a CBCT study is measured in hundreds of megabytes and a raw scanner export in
/// tens of gigabytes. At Tunisia's median uplink such a file also takes hours to leave the cabinet, so no
/// storage price makes hosting it workable.</para>
/// </summary>
public enum FileResidency
{
    /// <summary>
    /// The bytes are in the deployment's own object store, at <c>StorageKey</c>. Everything that fitted before
    /// this enum existed, and the value every pre-existing row carries.
    /// </summary>
    Hosted = 1,

    /// <summary>
    /// The bytes are in the cabinet's own coffre folder and have never reached the vendor. What the deployment
    /// holds is the row, a content hash and — when one could be made — a small preview; <c>StorageKey</c> is
    /// <b>null</b>, and the original's path is derived by <see cref="Services.VaultPath"/> rather than stored.
    /// </summary>
    Vault = 2
}
