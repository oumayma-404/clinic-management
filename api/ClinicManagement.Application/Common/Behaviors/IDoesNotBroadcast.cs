namespace ClinicManagement.Application.Common.Behaviors;

/// <summary>
/// Marks a command that must <b>not</b> emit a real-time change signal, in an area that otherwise does.
///
/// <para>The broadcast convention is by <i>area</i>: any command under <c>Features/&lt;Area&gt;/Commands</c>
/// tells every browser in the clinic that <c>&lt;area&gt;</c> changed. That is right for a command which is an
/// edit, and wrong for one which is a <b>step of a longer operation</b> — the resource changes once, at the end,
/// and the steps before it change nothing anybody renders.</para>
///
/// <para>⚠️ <b>Measured, not theorised.</b> A 29,8 Mo file sent in four parts made the patient's file list
/// reload <b>four times</b> before the file existed — on every open tablet in the practice, not just the one
/// uploading — because each accepted chunk looked like an edit to <c>Files</c>. A 300 Mo study is 38 of them,
/// and none of it is visible as an error: the list simply refetches what it already had.</para>
///
/// <para>⚠️ It is <b>not</b> a way to quieten a command that really does change something. If a colleague's
/// screen would render a different thing after this command, it belongs on the bus — the fix for a noisy edit is
/// a narrower resource key, not silence. And it does nothing in an excluded area, which
/// <c>RealtimeResourceResolverTests</c> asserts, because a marker that has no effect reads as one that does.</para>
/// </summary>
public interface IDoesNotBroadcast
{
}
