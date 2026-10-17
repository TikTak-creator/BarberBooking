# Security model / Модель безопасности

Bladez trusts the operating-system account running its host, its OS administrators, and the business Owner. It does not trust browser input, booking locators, client-supplied prices, role names, appointment versions or forwarded headers from arbitrary proxies.

## Data boundary

- Identity hashes passwords with PBKDF2 (600,000 iterations). Passwords are not decryptable and are not stored in configuration.
- AES-256-GCM encrypts names, phone numbers, emails, notes, cancellation/time-off reasons and stored Identity token material. A random 96-bit nonce and a purpose-specific associated-data value accompany every field.
- Username, role membership, booking date/status, catalog information, prices and audit actor IDs remain queryable metadata. This is field encryption, not whole-database encryption. Email/phone lookup indexes use keyed HMAC fingerprints.
- The master key is separate from MySQL. Windows uses current-user DPAPI and private ACLs. Linux files are private to the application user; the OS/disk operator must protect the volume. A missing key cannot be regenerated to decrypt existing data. The database's key ID is checked before normal requests; mismatch fails closed.
- Host connection secrets and optional SMTP password are sealed in `secrets.vault`. Linux initially supplies a read-only runtime connection file. Do not expose either profile directory through a web server.
- TLS connects the app to MySQL. Windows pins its instance's CA; Linux additionally checks the `db` certificate name. MySQL is bound to loopback on Windows and has no published container port on Linux.

## Browser boundary

Server-side role/ownership checks protect every management endpoint and booking ticket. Cookie authentication is HttpOnly, SameSite=Lax, Secure on HTTPS; public plain HTTP is rejected. Only explicitly configured proxy IPs may supply forwarding headers. Loopback-only HTTP is a deliberate local Windows exception.

All unsafe MVC/JSON operations require CSRF tokens, including login. Auth and booking endpoints have request limits. Failed password attempts lock an account after five failures for fifteen minutes. Optional TOTP and one-use recovery codes support MFA. A temporary staff password must be replaced. Session security stamps are validated on every request. Account disable/password change invalidates other sessions.

Responses have no-store caching, a script nonce CSP, frame denial and restrictive browser permissions. User values enter the schedule through encoded JSON and `textContent`. No browser storage contains database credentials or a decryption key. A browser user can still inspect their own authorized responses and modify local UI: security relies on what the server authorizes, not on hiding DevTools.

## Business consistency

Creation, rescheduling and time-off changes lock the corresponding barber row before conflict checks. This also serializes competing requests when a schedule is empty. Appointment versions prevent stale status/reschedule/cancel writes. Past slots, off-grid times, shift boundaries, inactive catalog items and time-off conflicts are checked on the server. Price/name/duration snapshots preserve the agreed service when the catalog changes.

## Deployment and backups

No factory owner password exists. Initialization creates a fresh database; it never resets an existing owner. Normal app credentials have SELECT/INSERT/UPDATE/DELETE only. A separate temporary user performs EF migrations and is revoked afterward.

Offline backups include both MySQL data and the field-encryption key inside a passphrase-encrypted AES-GCM archive (PBKDF2-SHA512, 600,000 iterations). The included small-host tools cap source data at 512 MiB. Copying requires stopping the private server. Test restoration into an empty separate profile. Windows and Linux archive formats are platform-specific physical MySQL backups, not a cross-platform migration mechanism. Never delete the working profile until a restored copy has been tested. Backups contain contacts and credential hashes even though the archive is encrypted: restrict access and store the passphrase separately.

The original demo embedded a database credential. This release removes it from current configuration, but Git history still contains the demo. Rotate that credential anywhere it was reused. No existing system MySQL instance or legacy database is modified by the Windows Host.

## Operational limits

This release does not claim regulatory certification, protection from a compromised host account, end-to-end encryption against the server owner, SMS identity verification, or live payment processing. SMTP account email requires trusted operator configuration and TLS-capable mail infrastructure. Keep dependencies, OS and MySQL patched; plan certificate rotation before the private Linux database certificate expires (825 days), and monitor disk space, database readiness and backups.

Перед реальной эксплуатацией: настройте HTTPS и домен, замените примеры каталога, включите MFA владельцу, проверьте SMTP и восстановление копии, ограничьте доступ к VPS/учётной записи Windows. Не отправляйте ключи, файлы копий и пароли в GitHub issues.
