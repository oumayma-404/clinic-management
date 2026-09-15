# Drop the Velopack client setup here (built by .github/workflows/client-installer.yml).
#
# The legacy `ClinicManagementClientSetup-<version>.exe` name is still recognised by
# `ClientUpdatePackage` for servers deployed before the move, but the Inno client installer no longer
# exists: `packaging/client/clinic-client.iss` was merged into `packaging/setup/clinic-setup.iss`, whose
# poste role downloads the Velopack setup instead.
# The API serves the newest one at GET /api/meta/client-download, hashing the bytes it will serve, so the
# desktop shell's « Mettre à jour maintenant » can verify the download. See deploy/README.md.
