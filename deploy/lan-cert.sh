#!/bin/sh
# Mint the CA + server certificate for a CONTAINERISED `SelfHostedLan` deployment.
#
# ⚠️ **Why this exists, and why it is not a replacement for `CertificateProvisioner`.** On a clinic PC the API
# self-generates its certificate on first boot and gets the SANs right, because `LanAddresses.IPv4()` enumerates
# *that machine's* interfaces. In a container it enumerates the **container's** — so the certificate names
# `172.20.0.3` and `localhost`, and a phone connecting to the host's `192.168.1.x` fails the TLS handshake on a
# hostname mismatch **after** the user has already installed and trusted the CA. That is the worst shape of
# failure: every visible step succeeded.
#
# The container cannot know the host's LAN address, so it is supplied here and the result is handed to the API
# through `Https:CertPath` — the documented operator-supplied path, not a new mechanism.
#
# Usage (from the repository root):
#   docker run --rm -e LAN_IP=192.168.1.18 \
#     -v clinic-selfhosted-lan_lan-certs:/out -v "$PWD/deploy/lan-cert.sh:/lan-cert.sh:ro" \
#     alpine:3 sh /lan-cert.sh
#
# It writes `server.pfx`, `ca.crt` and `server-cert-password` into the volume the API mounts at `/app/.local`,
# which is the same layout the provisioner produces — so the trust page keeps serving the right CA.

set -eu

: "${LAN_IP:?LAN_IP must be set to the address a phone will type, e.g. 192.168.1.18}"
OUT=/out
DAYS_CA=1825
DAYS_LEAF=825

apk add --no-cache openssl >/dev/null

PASSWORD=$(openssl rand -base64 32 | tr -d '\n')
WORK=$(mktemp -d)
cd "$WORK"

openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days "$DAYS_CA" \
    -keyout ca.key -out ca.crt \
    -subj "/CN=Clinic Management Local CA" \
    -addext "basicConstraints=critical,CA:TRUE" \
    -addext "keyUsage=critical,keyCertSign,cRLSign"

openssl req -newkey rsa:2048 -nodes -sha256 -keyout server.key -out server.csr -subj "/CN=clinic-server"

# ⚠️ `IP:` entries, not `DNS:`. A client connecting to an address literal validates it against **iPAddress**
# SANs; a DNS SAN spelled like an IP matches nothing, which is exactly the trap that makes this file necessary.
cat > leaf.ext <<EXT
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:localhost,DNS:clinic-server,IP:127.0.0.1,IP:${LAN_IP}
EXT

openssl x509 -req -in server.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
    -out server.crt -days "$DAYS_LEAF" -sha256 -extfile leaf.ext

# AES rather than OpenSSL 3's legacy RC2 default: .NET on Linux refuses an RC2-protected PKCS#12 outright.
openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt -certfile ca.crt \
    -keypbe AES-256-CBC -certpbe AES-256-CBC -macalg SHA256 \
    -passout "pass:${PASSWORD}"

install -m 0644 server.pfx "$OUT/server.pfx"
install -m 0644 ca.crt "$OUT/ca.crt"
printf '%s' "$PASSWORD" > "$OUT/server-cert-password"
chmod 0644 "$OUT/server-cert-password"

echo "SANs: DNS:localhost, DNS:clinic-server, IP:127.0.0.1, IP:${LAN_IP}"
echo "PASSWORD=${PASSWORD}"
