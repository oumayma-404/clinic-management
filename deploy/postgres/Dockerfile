# Custom Postgres image for Point-in-Time Recovery (PITR).
#
# Base: postgres:16 (Debian bookworm, glibc) rather than the stack's postgres:16-alpine —
# WAL-G ships only glibc prebuilt binaries (no musl/alpine build). Same PG16 on-disk format,
# so the postgres_data volume stays compatible with the rest of the stack.
#
# Ships two extra binaries used by the PITR flow:
#   * wal-g       — Postgres's archive_command (`wal-g wal-push`) streams every completed WAL
#                   segment off-site; the pitr sidecar (same image) takes physical base backups
#                   (`wal-g backup-push`) and prunes retention (`wal-g delete retain`).
#   * supercronic — container-friendly cron for the sidecar's scheduled base backups: preserves
#                   the container environment (unlike Debian's vixie cron, which scrubs it) and
#                   logs each run to stdout so `docker logs` shows the schedule firing.
FROM postgres:16

ARG WALG_VERSION=v3.0.5
ARG SUPERCRONIC_VERSION=v0.2.33
# Docker sets TARGETARCH automatically on `buildx`; defaults to amd64 for the common single-VPS case.
ARG TARGETARCH=amd64

RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends curl ca-certificates; \
    case "${TARGETARCH}" in \
      amd64) WALG_ARCH=amd64;   SC_ARCH=amd64 ;; \
      arm64) WALG_ARCH=aarch64; SC_ARCH=arm64 ;; \
      *) echo "unsupported TARGETARCH=${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    curl -fsSL -o /tmp/wal-g.tar.gz \
      "https://github.com/wal-g/wal-g/releases/download/${WALG_VERSION}/wal-g-pg-ubuntu-20.04-${WALG_ARCH}.tar.gz"; \
    tar -xzf /tmp/wal-g.tar.gz -C /usr/local/bin; \
    mv "/usr/local/bin/wal-g-pg-ubuntu-20.04-${WALG_ARCH}" /usr/local/bin/wal-g; \
    rm /tmp/wal-g.tar.gz; \
    curl -fsSL -o /usr/local/bin/supercronic \
      "https://github.com/aptible/supercronic/releases/download/${SUPERCRONIC_VERSION}/supercronic-linux-${SC_ARCH}"; \
    chmod +x /usr/local/bin/wal-g /usr/local/bin/supercronic; \
    apt-get purge -y --auto-remove curl; \
    rm -rf /var/lib/apt/lists/*; \
    wal-g --version; \
    supercronic -version

# Used only by the pitr sidecar (the postgres service keeps the stock entrypoint).
COPY pitr-entrypoint.sh pitr-backup.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/pitr-entrypoint.sh /usr/local/bin/pitr-backup.sh
