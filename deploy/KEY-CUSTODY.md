# Key custody — where the keys are, who holds them, and what to do when one is lost

**Applies to:** the `HostedMultiTenant` deployment (`docker-compose.hosted.yml`). The `CloudBrowser` deployment
(`docker-compose.prod.yml`) shares the backup and PITR keys; a clinic's own Windows PC (`SelfHostedLan`) uses
machine-scoped DPAPI instead of a certificate and none of this applies to it.

> This is a **deliverable**, not a note (FR-3.8). If it is not filled in with real names and real locations
> before the deployment carries a real practice's records, the deployment is not ready.

---

## The five keys

| # | Key | Encrypts | Where it lives | Losing it costs |
|---|---|---|---|---|
| 1 | **Key-ring certificate** (PKCS#12 + password) | the Data Protection key ring | `secrets/keyring-certificate.pfx` on the host, mounted read-only at `/run/secrets/keyring_certificate` | every second factor, every cabinet's SMS/WhatsApp/SMTP credentials, every Google Agenda token — **all at once** |
| 2 | **Backup encryption key** (`age` key pair) | the nightly dump + the object-store archive | public half in `.env` (`BACKUP_AGE_RECIPIENT`); **private half off this host** | **every off-site backup, permanently** |
| 3 | **PITR stream key** (`WALG_LIBSODIUM_KEY`) | every WAL segment + every base backup | `.env` on the host | **every archived base backup and WAL segment, permanently** |
| 4 | **Volume keyfile** (LUKS) | the disk holding `postgres_data` + `minio_data` | the host's own boot volume, `/etc/clinic/luks.key`, mode `0400 root:root` | the data volume cannot be unlocked at boot |
| 5 | **Audit chain key** (`Audit:ChainKey`) | *nothing* — it **signs** the audit ledger (FR-4.1) | `secrets/audit-chain-key` on the host, mounted read-only at `/run/secrets/audit_chain_key` | no data at all — but every entry written under it becomes **unverifiable**, which reads exactly like tampering |

**Fill this in before go-live.** Each row needs a real answer, not a placeholder:

| # | Primary holder | Second copy held by | Where the second copy is kept |
|---|---|---|---|
| 1 | _(name, role)_ | _(name, role)_ | _(sealed envelope in the practice safe / password manager vault / …)_ |
| 2 | _(name, role)_ | _(name, role)_ | _(must NOT be the same place as the backups themselves)_ |
| 3 | _(name, role)_ | _(name, role)_ | _(must NOT be the same place as the WAL archive)_ |
| 4 | _(name, role)_ | _(name, role)_ | _(must NOT be on the encrypted volume it unlocks)_ |
| 5 | _(name, role)_ | _(name, role)_ | _(with key 1's certificate — but NOT inside the database it signs)_ |

⚠️ **Key 5 is the odd one out, and it is worth understanding why it is here at all.** It encrypts nothing, so
losing it loses no records — what it costs is the *evidence*. Each audit entry carries a value derived from itself
and its predecessor, keyed by this secret, which is what makes an entry impossible to alter or remove without
`verify-schema` naming the first broken one. Replace the key and every entry written under the old one fails that
walk — indistinguishable, from the outside, from somebody having edited the ledger. So: generate it once
(`openssl rand -base64 48`), keep it, and treat a request to "just regenerate it" as the destructive act it is.

⚠️ **It is deliberately NOT the Data Protection ring (key 1).** Part 3 re-protects that ring, and FR-3.9 makes it
the thing a restore may fail to read — so if the chain were keyed on it, the ledger would become unverifiable at
exactly the moment somebody wanted to check it. Two keys, kept in the same place, doing different jobs.

⚠️ **Keys 2 and 3 must not be stored with what they encrypt.** An archive holding both the ciphertext and the key
that opens it is not encrypted in any way that matters — the likeliest exposure is a leaked or stolen backup, and
that is precisely the case where both would leak together.

---

## 1 — The key-ring certificate

### What it protects, and what it does not

The Data Protection key ring holds the master keys that decrypt **every** secret in the database. Because this
deployment persists the ring to a named volume, the framework's own automatic key encryption is disabled — so
before FR-3.1 the ring sat in **cleartext** on that volume and a copied disk yielded every one of those secrets
with no key at all.

It protects a **stolen, snapshotted or decommissioned disk**. It does **not** protect against someone who already
has root on the running host: that process can read the certificate it was given, by design.

### Generating one

```bash
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
  -keyout keyring.key -out keyring.crt -subj "/CN=clinic-keyring"
openssl pkcs12 -export -inkey keyring.key -in keyring.crt \
  -out deploy/secrets/keyring-certificate.pfx
printf '%s' '<the export password>' > deploy/secrets/keyring-certificate-password
shred -u keyring.key keyring.crt
chmod 0400 deploy/secrets/keyring-certificate*
```

Both files are named by `deploy/.env`'s `KEYRING_CERTIFICATE_FILE` / `KEYRING_CERTIFICATE_PASSWORD_FILE`.

### ⚠️ Deploying it — the order is the whole safety argument

Configuring the certificate encrypts keys the ring writes **from then on**. It re-wraps **nothing** already on the
volume, so the existing key stays in cleartext for the rest of its life *and* remains a valid decryptor. Doing
this in the wrong order destroys every second factor on the deployment at once (risk R-2). The order is:

```bash
# 1. Deploy with the certificate configured. Nothing is re-encrypted yet; the old keys still decrypt.
docker compose -f docker-compose.hosted.yml up -d

# 2. Mint a new active key and move every stored secret onto it.
docker exec clinic-api-prod dotnet ClinicManagement.API.dll reprotect-secrets --rotate

# 3. Confirm it finished. This must read ZERO for every family.
docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema \
  | grep -E 'key-ring-protection|secrets-protected-under-current-ring'

# 4. ONLY THEN remove the superseded plaintext key files, and re-verify afterwards.
docker run --rm -v clinic-management_dataprotection_keys:/keys alpine grep -rl '<key ' /keys
#    → delete only the files listed, then repeat step 3.
```

`reprotect-secrets` **names** any row it could not decrypt and exits `2`. **Do not delete any key file while a row
is listed** — its key is what is still needed to read it.

### Rotating it (FR-3.2)

Keep the **previous two** generations as decryptors. More than two produces a warning on startup; fewer risks
ciphertext nobody has re-protected yet.

```yaml
DataProtection__CertificatePath: /run/secrets/keyring_certificate            # the new one
DataProtection__PreviousCertificates__0__Path: /run/secrets/keyring_certificate_previous
DataProtection__PreviousCertificates__0__Password_FILE: /run/secrets/keyring_certificate_previous_password
```

Then run steps 2–4 above. Once `secrets-protected-under-current-ring` reads zero, the retired generation may be
dropped from the list — and only then.

### If it is lost

There is no recovery of the ciphertext. What is recoverable, per family:

| Family | Recovery |
|---|---|
| Clinic administrators' second factor | `reset-user-totp --email <address>`, then the user enrols afresh |
| Console accounts' second factor | `platform-account --reset-totp --email <address>` |
| A cabinet's SMS / WhatsApp / SMTP credentials | the cabinet re-enters them in « Paramètres → Rappels » |
| A cabinet's Google Agenda token | the cabinet reconnects in « Paramètres → Google Agenda » |

Every one of these needs somebody at the practice. Plan for a support day, not an afternoon.

---

## 2 — The backup encryption key

```bash
age-keygen -o backup-identity.txt     # keep this file OFF the server
grep 'public key' backup-identity.txt # → BACKUP_AGE_RECIPIENT in deploy/.env
```

The sidecar is given the **public** half only. It can encrypt and **cannot decrypt what it wrote** — deliberately:
a container reachable from the network holding the key that opens every archive is exactly the exposure the
encryption exists to prevent.

The sidecar **refuses to run** without `BACKUP_AGE_RECIPIENT`. That is not a nuisance: "encrypt if a key happens
to be set" is the version that ships a complete copy of every practice's medical records to somebody else's
storage in the clear, while reporting success.

### ⚠️ If this key is lost, every backup taken with it is unrecoverable.

Not "difficult" — unrecoverable. There is no escrow, no vendor copy and no reset. This is the single most
important line in this document.

### Restoring from one

```bash
age --decrypt --identity backup-identity.txt --output db.dump db-<timestamp>.dump.age
pg_restore --list db.dump | head        # must be non-empty before going further
```

---

## 3 — The PITR stream key

`WALG_LIBSODIUM_KEY` is read by **both** the `postgres` service (whose `archive_command` pushes each WAL segment
itself) and the `pitr` sidecar (which pushes base backups). Both read the same value from `.env`. **Set it on only
one and the base backups are encrypted while the WAL ships in the clear** — the half that never stops flowing.

```bash
wal-g libsodium-key-gen        # or: openssl rand -hex 32
```

The sidecar **refuses to start** without it. ⚠️ **Lose it and every archived base backup and WAL segment is
unrecoverable**, the same statement as key 2.

---

## 4 — The volume keyfile (LUKS, FR-3.5)

### What this protects, in these words

Encrypting the data volume protects a **stolen, snapshotted or decommissioned disk**. It does **not** protect
against someone who already has root on the running host — while the machine is up the volume is mounted and
readable, and no disk encryption changes that. Anyone who tells you otherwise is describing a different control.

### Setting it up (a scheduled window — this moves data)

```bash
# 0. Take a full backup and verify it decrypts and parses BEFORE touching anything. See RESTORE-DRILL.md.
docker compose -f docker-compose.hosted.yml down

# 1. Create the keyfile on the host's own BOOT volume — never on the volume it unlocks.
dd if=/dev/urandom of=/etc/clinic/luks.key bs=512 count=8
chmod 0400 /etc/clinic/luks.key && chown root:root /etc/clinic/luks.key

# 2. Format the new device and open it.
cryptsetup luksFormat --type luks2 /dev/<device> /etc/clinic/luks.key
cryptsetup luksOpen /dev/<device> clinicdata --key-file /etc/clinic/luks.key
mkfs.ext4 /dev/mapper/clinicdata && mount /dev/mapper/clinicdata /srv/clinic

# 3. Move the Docker volumes' contents onto it, then point Docker's data root or the volumes at /srv/clinic.
# 4. Unattended reboot (the server must come back with no human present):
echo 'clinicdata /dev/<device> /etc/clinic/luks.key luks' >> /etc/crypttab
echo '/dev/mapper/clinicdata /srv/clinic ext4 defaults,nofail 0 2' >> /etc/fstab

# 5. Reboot cold and confirm the stack returns with NO interaction and no passphrase prompt.
reboot
```

⚠️ **The keyfile on the boot volume is what makes an unattended reboot possible, and it is also the limit of this
control**: anyone who can read that boot volume can unlock the data volume. That is the accepted trade — a server
that will not come back without somebody typing a passphrase at 03:00 is a worse outcome for a practice than the
threat it defends against.

### Rolling back

Reverting the LUKS change means moving the data back onto an unencrypted device, and it **requires the keyfile**.
Without it the volume cannot be opened and there is nothing to move. Keep the second copy (table above) somewhere
that survives losing this host.

---

## ⚠️ What travels together, and what travels apart (FR-3.11)

**One rule, and it changed with FR-3.1.** Say it this way and no other:

> The key ring (`dataprotection_keys`) is now **encrypted**, so it may be backed up **alongside** `postgres_data`.
> What must travel **separately, never in the same archive**, is the **certificate** that decrypts it — together
> with the backup key and the PITR key.

Before FR-3.1 the ring was cleartext and *it* was the thing that had to travel apart; two operator documents said
opposite things about it. The ring is no longer the secret. **The certificate is.**

Also kept apart, for a reason of its own: `internal_certs` holds the internal CA's **private key**, so an archive
with both lets whoever holds it impersonate the database and the object store to any container that trusts that
root. (Losing that volume costs nothing — the `certs` one-shot mints a fresh set on the next `up -d`.)

---

## Reverting Part C — what breaks, and how to get back

| Change | Reverting it |
|---|---|
| Key-ring certificate | Safe **only while the old plaintext key files still exist**. Once they are deleted, removing the certificate leaves nothing able to read the ring — restore the certificate, do not remove it |
| **File-based secrets** | ⚠️ **A hard startup failure** once the environment values are deleted: the app refuses a `*_FILE` naming a missing file rather than starting with an empty secret. Recovery is to restore the files, or to put the literals back in `environment:` and remove the `*_FILE` variables — both, or the file still wins |
| Backup / PITR encryption | Safe going forward; archives **already written stay encrypted** and still need their key |
| LUKS | Requires the keyfile. See above |

---

## Before go-live — the checklist

- [ ] Every row of the holder table above filled in with a real name and a real location
- [ ] Keys 2, 3 and 4 each stored somewhere that is **not** where their ciphertext is
- [ ] `verify-schema` reports `key-ring-protection: the key ring is encrypted…`
- [ ] `verify-schema` reports `secrets-protected-under-current-ring` at zero
- [ ] `verify-schema` reports `google-token-protected: 0 cabinet(s) still hold…`
- [ ] One restore drill completed end to end and recorded in [RESTORE-DRILL.md](./RESTORE-DRILL.md)
- [ ] The host has been rebooted cold and returned **unattended**
