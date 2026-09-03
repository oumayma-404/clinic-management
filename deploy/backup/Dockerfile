# Off-server backup sidecar. postgres:16-alpine ships pg_dump matching the server major
# version (required — pg_dump must be >= the server); add rclone for the off-site upload.
FROM postgres:16-alpine

# `age` encrypts the dump and the object-store archive BEFORE rclone touches them
# (hosted-security-hardening FR-3.6), and decrypts them again in the same run so FR-3.7's
# "verify what you just uploaded" is a real step rather than a round trip to the remote.
RUN apk add --no-cache rclone tar gzip age

COPY backup.sh /usr/local/bin/backup.sh
COPY mirror-objects.sh /usr/local/bin/mirror-objects.sh
COPY check-keyring.sh /usr/local/bin/check-keyring.sh
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/backup.sh /usr/local/bin/mirror-objects.sh \
    /usr/local/bin/check-keyring.sh /usr/local/bin/entrypoint.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
