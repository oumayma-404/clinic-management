namespace ClinicManagement.Application.Features.Clinics;

/// <summary>« Mode discret »: its step-up action and its refusals, in one place.</summary>
public static class ClinicMoneyMask
{
    /// <summary>The step-up action « Afficher l'argent » spends. A password never mints it — only an authenticator code.</summary>
    public const string ShowStepUpAction = "show-money";

    /// <summary>Code of the refusal a guarded money read answers while the mode is on.</summary>
    public const string HiddenCode = "money_hidden";

    public const string Hidden = "Cette section est masquée pour le moment.";

    public const string NotEnrolled =
        "Activez l'authentificateur dans « Sécurité » pour utiliser le mode discret.";
}

/// <summary>What both toggles answer: the state they left the cabinet in.</summary>
public record ClinicMoneyMaskDto(bool IsMoneyHidden);
