# Off-server backup sidecar. postgres:16-alpine ships pg_dump matching the server major
# version (required — pg_dump must be >= the server); add rclone for the off-site upload.
FROM postgres:16-alpine

RUN apk add --no-cache rclone tar gzip

COPY backup.sh /usr/local/bin/backup.sh
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/backup.sh /usr/local/bin/entrypoint.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
