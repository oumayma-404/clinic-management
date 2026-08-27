using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClinicManagement.API.Filters;

/// <summary>
/// How large an uploaded archive may be, in one place, applied <b>before the form is bound</b>.
///
/// <para><b>Why an attribute and not just the check in the action.</b> Both archive endpoints carry
/// <c>[DisableRequestSizeLimit]</c>, which lifts Kestrel's body cap and nothing else — the form reader keeps its
/// own <c>MultipartBodyLengthLimit</c>, 128 MB by default, and exceeding it throws <c>InvalidDataException</c>
/// during <i>model binding</i>. That is before the action body runs, so
/// <c>BackupController.ValidateUpload</c>'s carefully-worded French refusal was <b>dead code for every archive
/// between 128 MB and the configured ceiling</b>, and the caller got a generic 500 naming no limit — precisely
/// the « Kestrel's own 413 with an empty body » outcome that check exists to avoid. A cabinet with twenty years
/// of radiographs is squarely in that range.</para>
///
/// <para>⚠️ <b>The value is hand-parsed.</b> <c>IConfiguration.GetValue&lt;int&gt;</c> <i>throws</i> on a value it
/// cannot convert, and this one is read on the path a practice uses to recover its own records: a typo in
/// <c>Backup:ArchiveMaxSizeMb</c> must fall back to the default, never take the endpoint off the air. The same
/// reasoning `Subscription:*` is read under.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ArchiveUploadLimitAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        new Filter(serviceProvider.GetRequiredService<IConfiguration>());

    private sealed class Filter : IResourceFilter
    {
        private readonly IConfiguration _configuration;

        public Filter(IConfiguration configuration) => _configuration = configuration;

        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            var limit = ArchiveUploadLimit.MaxBytes(_configuration);

            // Replacing the feature is what makes the limit apply to the reader the binder is about to use; the
            // options are otherwise fixed for the whole application at startup and could not be operator-set.
            context.HttpContext.Features.Set<IFormFeature>(new FormFeature(
                context.HttpContext.Request,
                new FormOptions
                {
                    MultipartBodyLengthLimit = limit,
                    BufferBodyLengthLimit = limit,
                    ValueLengthLimit = int.MaxValue,
                    MultipartHeadersLengthLimit = int.MaxValue,
                }));

            var body = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (body is { IsReadOnly: false })
            {
                body.MaxRequestBodySize = limit;
            }
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}

/// <summary>The configured ceiling, shared by the filter above and by the actions' own French refusal.</summary>
public static class ArchiveUploadLimit
{
    /// <summary>Two orders of magnitude above an ordinary cabinet, and well under what a container can spool.</summary>
    public const int DefaultMaxSizeMb = 1024;

    public const string ConfigurationKey = "Backup:ArchiveMaxSizeMb";

    public static long MaxBytes(IConfiguration configuration) => (long)MaxSizeMb(configuration) * 1024 * 1024;

    public static int MaxSizeMb(IConfiguration configuration) =>
        int.TryParse(configuration[ConfigurationKey], out var megabytes) && megabytes > 0
            ? megabytes
            : DefaultMaxSizeMb;
}
