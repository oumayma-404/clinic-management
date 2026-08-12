# The deployment's own internal certificate authority (hosted-security-hardening Part 2, FR-2.1/FR-2.2).
#
# A ONE-SHOT container, not a service: it runs to completion before postgres, minio, api, backup and pitr
# start, writes the CA and two server leaves into the named `internal_certs` volume, and exits 0. Every
# consumer then mounts that volume read-only. `depends_on: { certs: { condition:
# service_completed_successfully } }` is what orders it — see both compose files.
#
# ⚠️ Why a container and not the application's own CertificateProvisioner, which already mints a CA + SAN
# leaf for the LAN install: that class runs pre-`Build()` inside the API process, has no DI, takes a real
# logger and is shaped for a Windows service. And a console verb would be worse — it would run inside a
# container that starts *after* the database it needs a certificate to reach.
#
# alpine + openssl and nothing else: the image exists to run one script and stop.
FROM alpine:3.20

RUN apk add --no-cache openssl

COPY issue.sh /usr/local/bin/issue.sh
RUN chmod +x /usr/local/bin/issue.sh

ENTRYPOINT ["/usr/local/bin/issue.sh"]
