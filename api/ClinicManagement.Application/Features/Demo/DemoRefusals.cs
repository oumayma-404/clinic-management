namespace ClinicManagement.Application.Features.Demo;

/// <summary>The public demo's refusals: each French sentence beside the code a client branches on.</summary>
public static class DemoRefusals
{
    public const string ReadOnlyCode = "demo_read_only";

    public const string ReadOnlyMessage =
        "Démonstration : rien n'est enregistré. Créez votre cabinet pour essayer avec vos données.";

    public const string RebuildingCode = "demo_rebuilding";

    public const string RebuildingMessage = "La démo se met à jour, revenez dans une minute.";
}
