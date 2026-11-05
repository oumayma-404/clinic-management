# A refused sign-in is written down, and the vendor hears of the suspicious ones

Asked 2026-11-04, after six refused sign-ins on Sat 31/10 left no trace of who had tried: the server had refused
them before checking a password (the address had no account), and nothing anywhere recorded the address.

## What shipped

- **Every refusal at the six credential doors is logged**: the app's sign-in, second-factor setup and recovery
  code, and the console's three. Example: `Sign-in refused at ClinicSignIn: UnknownEmail for karim.benali@gmail.com
  from 41.226.10.20`. The address is written in full when it reads as one (`SignInRefusalRules.ShownEmail`), and as
  « (pas une adresse e-mail) » otherwise; the password and the code never appear. The alert's « E-mail saisi » line
  is the same string.
- **The vendor is e-mailed in five cases**, through `VendorAlertRecipients` (`Backup:AlertEmail`, else every
  active console account — on prod today, the one console account):

  | Case | When | Quiet period |
  |---|---|---|
  | Console refused | every refusal on the console | 1 h per e-mail + address |
  | Account under attack | 5 refusals on one existing account in 15 min | 1 h per account |
  | Address guessing | 10 refusals from one address in 15 min | 1 h per address |
  | E-mail with no account | 3 refusals for one address nobody owns in 15 min (the 31/10 case) | 24 h per address |
  | Deactivated account | a switched-off account presented its right password | 24 h per account |

  Never more than 10 e-mails an hour in all.

## Why it is built this way

- **One pipeline behavior, not a call in each handler** (`SignInRefusalBehavior`). A door is covered by carrying
  `ICredentialAttempt`; `SignInRefusalCoverageTests` derives the doors from the sources (every request whose file
  calls `VerifyPassword(`) and fails on one without the marker. The three re-checks made inside a session
  (`ChangePasswordCommand`, `StepUpCommand`, `ChangePlatformPasswordCommand`) are a reviewed list with reasons.
- **It reads the refusal off the `Result.Code` the handler already returns** and changes nothing about the
  answer. The account is looked up only when the code is a refusal, so the « code requis » step of every ordinary
  sign-in costs nothing and records nothing.
- **Counts live in memory, like `LoginAttemptTracker`**: a table would add a write to an anonymous endpoint on every
  refusal. A restart forgets the counts, which costs at most one repeated e-mail. The log line is the record.
- **The e-mail leaves from its own scope, with the request's execution context suppressed**, so the refusal is
  answered before any mail server is reached.
- **The address is written in full, at the owner's request (2026-11-05).** The first version masked it
  (« ka***@gmail.com »), which could not answer « who tried? » — the question this feature was asked for.
- ⚠️ **Text that does not read as an address is not written at all**: it is most often a password typed into the
  wrong box. So the test is stricter than `EmailAddressInput`, which would store « Karim@Dentiste2024 »: the provider
  needs a dot and a letters-only ending, and only letters, digits and `. _ % + -` pass, so what is shown cannot forge
  a log line or carry markup. `EmailKey`, the raw form, is only ever a key in memory.
- ⚠️ **On the hosted deployment the log is also a file kept 30 days** (`api_logs` volume, `Program.cs`), so these
  addresses are too. `LogTemplateCoverageTests` forbids `{Email}` in a log template and carries its one reviewed
  exemption for this line: a sign-in address is a staff, console or prospect account, never a patient.
- ⚠️ The subject is fixed per case: the address and the IP are a stranger's input and stay out of mail headers.

## Not in scope

The robots that probe the site (the web container's « Server Reference ID » lines) are internet background noise
and are not alerted on. A successful console sign-in is not alerted on either.
