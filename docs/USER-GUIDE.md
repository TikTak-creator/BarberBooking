# Setup and operation / Установка и работа

## Windows: first start

1. Download the official `Bladez-Setup-1.0.0.exe` or Windows portable ZIP from this repository's Releases. Compare the download SHA-256 with the release checksum file. The application itself is currently unsigned; a Windows reputation warning is possible. The included Microsoft prerequisite has Microsoft's signature.
2. Run Setup and open **Bladez Host**. The app is installed for the current Windows user. Keep its server files together; do not move only the EXE.
3. Enter the shop name, owner name, a Latin login and a unique password of 12–128 characters, with uppercase/lowercase Latin letters, digits and at least five different characters. There is no default owner account.
4. Leave the website/MySQL ports as shown unless another service uses them. The wizard creates an independent MySQL, never reconfigures a system database. The first start downloads approximately 281 MB and checks a pinned SHA-256. If a Microsoft VC++ Runtime is missing, the host offers its included signed installer; Windows asks for elevation only for that component.
5. Choose **Настроить и запустить**, wait for readiness, then **Открыть сайт**. Sign in with the owner credentials you just chose.
6. Open **Настройки**, set the real shop contact details, time zone, booking horizon and cancellation policy. Edit **Мастера** and **Услуги**; the optional initial catalog is illustrative. Create staff accounts in **Команда**, choosing Reception or a specific Barber. Enable MFA for the owner under the account's **Безопасность** page and store recovery codes separately.

The default URL is `http://127.0.0.1:5088`. It is deliberately local to the computer. The Host window owns both child processes and shuts them down on exit. Minimizing keeps it in the tray. Windows sleep, logout or shutdown interrupts availability; it is not a Windows service. Do not publish MySQL or plain HTTP to the Internet. Use the VPS package for public booking, or have an administrator configure a trusted HTTPS reverse proxy according to `HostSettings` and the security guide.

Data defaults to `%LOCALAPPDATA%\Bladez\Host`, protected for the current account and SYSTEM. `server.json` contains non-secret ports/URLs. `secrets.vault` and `master.key` belong together; never delete/recreate the key to fix a connection error. Moving this folder to another Windows account without an encrypted backup will not bypass DPAPI. Program updates/uninstall must keep the data folder.

### Windows backup and recovery

In Host choose **Создать резервную копию**, select a new `.bladez` filename and a strong separate passphrase. The server stops for a consistent copy, then restarts if it was running. The archive contains the physical MySQL data and the field-encryption key under AES-GCM; the MySQL binaries/cache and authentication-cookie keyring are excluded. The included tool supports up to 512 MiB of source data.

Keep copies on a separate protected device. Test restoration: in a fresh Host setup choose **Восстановить копию**, select the file, an empty destination folder and its passphrase. Existing profiles cannot be overwritten. The key is rewrapped for the new Windows account, runtime paths are updated and old browser sessions expire. Ports from the old profile are retained; stop the source instance before testing the restored one on the same machine. The selected restored profile is remembered locally for subsequent launches. This is a Windows-to-Windows physical restore, not a Linux transfer/import tool.

If setup fails, preserve the data folder. Check free disk space, port conflicts, the VC++ prerequisite and `mysql-data/mysql.err`. Do not paste these files or secrets into a public issue. A failed download is never executed unless its checksum matches; remove only the failed download ZIP before retrying.

## Linux / VPS: first start

Supported installer target: Linux x86_64, Ubuntu/Debian with systemd for automatic Docker installation. Other Docker hosts can use the Compose files after an administrator provisions the required settings. Plan at least 2 GB RAM plus disk capacity for images, MySQL and backups.

1. Obtain a VPS and a domain. Point its DNS A record to the server; set an AAAA record only if IPv6 actually reaches it. Allow inbound TCP 80/443 and optionally UDP 443 in the provider firewall. Do not open MySQL port 3306.
2. Download and unpack the source/host ZIP on the VPS. Open `deploy/linux`.
3. Run `sudo bash install.sh`. It can install Docker Engine and Compose using Docker's official apt signing key and repository. It refuses conflicting existing Docker packages instead of deleting them. Existing databases are never overwritten.
4. Enter the domain without `https://`, certificate contact email, shop/owner names, login and password. Password input is hidden and never passed as a command-line argument. Wait for schema/owner initialization.
5. Open `https://your-domain`. Caddy requests and renews its public certificate. A certificate cannot be issued until DNS and firewall/routing are correct. Log in and customize catalog, policies and staff as described above.

Compose runs MySQL and the app on an isolated database network. Only Caddy exposes ports; the app runs as a non-root user. Database TLS has its own private CA and a certificate for `db`, separate from the public HTTPS certificate. The app trusts forwarding headers only from the fixed Caddy container IP. The subnet `172.30.89.0/24` must not overlap existing host networks; administrators changing it must also change the trusted proxy IP.

`private/` contains secrets and data. Never serve or commit it. `private/ca-key.pem` belongs only to the host operator; the application receives only the CA certificate. The private MySQL server certificate expires after 825 days: replace it with a new certificate for `db` signed by the stored CA and restart MySQL/app before expiration. Caddy manages public certificate renewal itself.

```bash
sudo bash manage.sh status     # containers + authenticated database check
sudo bash manage.sh logs       # recent app/proxy logs; inspect locally
sudo bash manage.sh stop       # graceful stop, keep data
sudo bash manage.sh start      # start with existing data
sudo bash manage.sh backup     # asks for a separate archive passphrase
```

Docker's `unless-stopped` restart policy starts services after a host reboot when Docker is enabled. This does not replace monitoring or backups. For mail configure SMTP STARTTLS and a trusted provider in the owner Settings page. Initial certificate validation may take a few minutes; inspect Caddy logs when it fails.

### Linux backups, restore and updates

Install `python3-cryptography` if absent (`sudo apt-get install python3-cryptography`). `manage.sh backup` stops the stack, creates an authenticated encrypted archive under `backups/`, and restarts it. The included tool caps source files at 512 MiB. For larger installations use an operator-reviewed MySQL backup plan that also preserves the application key. Test copies regularly.

Restore into a **fresh unpacked Linux package** with no `private/mysql/mysql` and no application key:

```bash
cd deploy/linux
sudo bash manage.sh restore /safe/path/copy.bladez-linux
# Review restored .env domain and DNS if this is a replacement server.
sudo bash manage.sh start
```

Do not simultaneously run two public hosts against the same domain. Windows `.bladez` and Linux `.bladez-linux` backups are distinct platform-specific formats. Old cookie sessions are not restored. Keep the original host until you verify login, contacts, catalog and bookings on the restored host.

Before an update, create and test an encrypted backup. Replace application source/deployment files with the matching new release while preserving `.env`, `private/` and `backups/`, then run `sudo bash manage.sh update`. It builds the app, stops it, creates a temporary schema account, applies migrations, revokes that account and starts the app. Failed migrations require investigation and, if needed, restoration from the pre-update backup; do not assume an automatic downgrade is safe.

## Daily operation / Ежедневная работа

- **Client / клиент:** create an account, optionally set MFA, choose service/barber/day/slot, enter contacts and review the ticket. Your account lists your own visits. Cancellation is available only before the configured cutoff and while pending/confirmed. Account ownership authorizes access; the ticket code alone does not.
- **Reception / ресепшен:** view schedule, create phone/walk-in bookings, confirm/cancel, reschedule, add breaks without conflicting with active visits. Check the service snapshot, price and time before confirming.
- **Barber / барбер:** view only the assigned schedule with masked customer phone. Record completion or no-show only after the relevant visit time; cannot change the catalog or staff accounts.
- **Owner / владелец:** configure catalog/hours/time zone, manage staff access and temporary passwords, review audit/system status, SMTP and policy. Completed visits determine revenue; pending/confirmed future bookings are not earned revenue.

SMTP is optional account-mail infrastructure, not a bundled mailbox. Email ownership requires the confirmation flow; phone verification/SMS and payment are not implemented. If SMTP is unavailable, do not promise password-reset delivery to customers. MFA recovery codes must be saved while first shown. Business privacy/retention rules need review for the jurisdiction where you operate.

Старый демо-проект и его база не обновляются автоматически: названия таблиц и модель доступа отличаются. Новая версия создаёт отдельную базу; перенос реальных старых данных требует явного проверенного преобразования, а не подключения нового сервера к старой схеме.
