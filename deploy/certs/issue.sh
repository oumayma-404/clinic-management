#!/bin/sh
# Mints the deployment's internal CA and the two server leaves every internal hop is verified against
# (hosted-security-hardening Part 2, FR-2.1/FR-2.2/FR-2.6). Runs once, before anything that needs them.
#
# What lands in the volume:
#
#   /certs/ca.crt                        the internal root — the ONE trust anchor for every hop
#   /certs/postgres/server.crt|.key      PostgreSQL's leaf (SAN: postgres, localhost, 127.0.0.1)
#   /certs/minio/public.crt|private.key  MinIO's leaf, in the file names MinIO's --certs-dir expects
#   /certs/minio/CAs/internal-ca.crt     the root, where MinIO looks for peers' CAs
#
# ⚠️ TEN YEARS, deliberately (FR-2.6). Nobody outside these containers evaluates these certificates, so a
# short lifetime buys almost nothing — and it adds a failure mode the deployment cannot survive: expiry plus
# the fail-loud startup check turns every later restart into a crash loop. The remaining life is reported by
# `verify-schema`'s `internal-certificate-days-remaining`, which is run before and after every schema change.
#
# ⚠️ IDEMPOTENT, on CertificateProvisioner's own rule: an existing, loadable, still-chaining set is REUSED.
# Re-minting on every `up -d` would hand postgres a new identity while the API still trusts the old root, so
# the whole stack would fail verification until every container had restarted in the right order.
#
# ⚠️ The postgres key is chowned to uid 999 and 0600'd. PostgreSQL REFUSES TO START on a key it does not own
# or that is group/world readable, and the message names permissions rather than TLS — so getting this wrong
# looks like a broken image rather than a certificate problem.
set -eu

CERTS=/certs
CA_CRT="${CERTS}/ca.crt"
CA_KEY="${CERTS}/ca.key"
DAYS=3650

# The uid the postgres:16 image runs the server as. Not a setting: if a future base image changes it, the server
# refuses to start on a key it does not own and says so, which is a better failure than a knob nobody set.
POSTGRES_UID=999

log() { echo "[certs] $*"; }

# True when the CA and both leaves are present, parseable AND still chain to that CA. Anything less is
# re-minted whole: a half-set is the state where one hop verifies and another silently cannot.
set_is_usable() {
	[ -s "${CA_CRT}" ] && [ -s "${CA_KEY}" ] || return 1
	openssl x509 -in "${CA_CRT}" -noout >/dev/null 2>&1 || return 1

	for leaf in "${CERTS}/postgres/server.crt" "${CERTS}/minio/public.crt"; do
		[ -s "${leaf}" ] || return 1
		openssl verify -CAfile "${CA_CRT}" "${leaf}" >/dev/null 2>&1 || return 1
	done

	[ -s "${CERTS}/postgres/server.key" ] && [ -s "${CERTS}/minio/private.key" ] || return 1
	return 0
}

mint_leaf() {
	common_name="$1"
	san="$2"
	key_path="$3"
	crt_path="$4"

	csr="$(mktemp)"
	ext="$(mktemp)"

	cat >"${ext}" <<EOF
basicConstraints = critical, CA:FALSE
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName = ${san}
EOF

	openssl req -new -newkey rsa:2048 -nodes \
		-keyout "${key_path}" -out "${csr}" \
		-subj "/CN=${common_name}" >/dev/null 2>&1

	openssl x509 -req -in "${csr}" \
		-CA "${CA_CRT}" -CAkey "${CA_KEY}" -CAcreateserial \
		-days "${DAYS}" -sha256 -extfile "${ext}" \
		-out "${crt_path}" >/dev/null 2>&1

	rm -f "${csr}" "${ext}"
	log "issued ${common_name} (${san})"
}

if set_is_usable; then
	expires="$(openssl x509 -in "${CA_CRT}" -noout -enddate | cut -d= -f2)"
	log "existing internal CA reused — expires ${expires}"
	exit 0
fi

log "no usable internal certificate set found — minting one"
mkdir -p "${CERTS}/postgres" "${CERTS}/minio/CAs"

openssl req -x509 -newkey rsa:4096 -nodes -sha256 \
	-days "${DAYS}" \
	-keyout "${CA_KEY}" -out "${CA_CRT}" \
	-subj "/CN=Clinic Management internal CA" \
	-addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
	-addext "keyUsage=critical,keyCertSign,cRLSign" >/dev/null 2>&1
log "issued the internal CA (${DAYS} days)"

# localhost/127.0.0.1 are in both SANs so each container's own healthcheck can VERIFY the chain rather than
# skip verification — a probe that passes `--no-check-certificate` proves the port answers and nothing else.
mint_leaf postgres "DNS:postgres, DNS:localhost, IP:127.0.0.1" \
	"${CERTS}/postgres/server.key" "${CERTS}/postgres/server.crt"
mint_leaf minio "DNS:minio, DNS:localhost, IP:127.0.0.1" \
	"${CERTS}/minio/private.key" "${CERTS}/minio/public.crt"

cp "${CA_CRT}" "${CERTS}/minio/CAs/internal-ca.crt"

chmod 644 "${CA_CRT}" "${CERTS}/postgres/server.crt" "${CERTS}/minio/public.crt" \
	"${CERTS}/minio/CAs/internal-ca.crt"
chmod 600 "${CA_KEY}" "${CERTS}/postgres/server.key" "${CERTS}/minio/private.key"
chown "${POSTGRES_UID}:${POSTGRES_UID}" "${CERTS}/postgres/server.key" "${CERTS}/postgres/server.crt"

log "internal certificate set ready in ${CERTS}"
