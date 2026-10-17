<p align="center"><img src="docs/assets/bladez-banner.png" alt="Bladez — Perfect style. Your look." width="100%"></p>
<p align="center"><b>Booking that feels simple. A business system behind it.</b></p>
<p align="center"><a href="readme_ru.md">Русский</a> · <a href="https://github.com/TikTak-creator/BarberBooking/releases">Download</a> · <a href="docs/USER-GUIDE.md">User guide</a> · <a href="docs/DEVELOPMENT.md">Developer guide</a></p>
<p align="center"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white"> <img alt="MySQL 8.4" src="https://img.shields.io/badge/MySQL-8.4_LTS-00758F?logo=mysql&logoColor=white"> <img alt="Windows + Linux" src="https://img.shields.io/badge/Host-Windows_%2B_Linux-1874FF"> <img alt="CI" src="https://github.com/TikTak-creator/BarberBooking/actions/workflows/verify.yml/badge.svg"></p>

Bladez is a self-hosted barbershop booking system with customer accounts, staff roles, a visual schedule and a separate MySQL database. The original portfolio demo has been rebuilt around server-side authorization, concurrent booking checks and private data storage.

The supplied artwork above is a **documentation banner**. Product screenshots below show the actual application. The interface is currently in Russian; both installation and development are documented in English and Russian.

## For your barbershop

| Customer experience | Business workspace | Your own host |
|---|---|---|
| Four-step booking: service → barber → time → contacts | Daily schedule, visit details and status changes | Windows setup wizard with its own MySQL |
| Personal booking history and permitted cancellation | Owner, reception and scoped barber accounts | Linux/VPS installer with Caddy HTTPS |
| Password login and optional authenticator MFA | Services, working hours, time off and audit trail | Encrypted offline backups and restore into a fresh profile |

**No default owner password. No shared demo customer database.** Setup asks you to create the owner. Optional example barbers/services are clearly marked and can be edited; no customer bookings are seeded.

## Install without writing code

Open [Releases](https://github.com/TikTak-creator/BarberBooking/releases) and choose the package for your computer/server.

- **Windows 10/11 x64:** install `Bladez-Setup-1.0.0.exe`, open **Bladez Host**, enter the shop name and owner credentials, then choose **Set up and start**. MySQL is downloaded from Oracle and checked against a pinned SHA-256. .NET is included. If required, the included signed Microsoft VC++ Runtime asks for administrator permission.
- **Linux x64 VPS (Ubuntu/Debian):** unpack the source/host package, open `deploy/linux`, run `sudo bash install.sh`, and answer the prompts. The installer can install Docker from its official signed apt repository, provisions MySQL and requests a public HTTPS certificate. You must supply your own domain, DNS and reachable ports 80/443.
- **Source ZIP:** for developers who want to inspect, change or build the code.

Windows starts in **local office mode** at `127.0.0.1`: it runs while the Host window/tray is open and Windows is signed in. Public, always-on hosting is covered by the Linux package. See the [full setup and recovery guide](docs/USER-GUIDE.md).

## Product tour

**30 actual screens**, captured from a running release build and its Windows host. Customer names and visits are isolated QA fixtures. [Open the full gallery →](docs/SCREENSHOTS.md)

<p align="center"><img src="docs/screenshots/01-home.png" alt="Public home and service catalog" width="100%"></p>

| Choose a real free slot | Your private ticket |
|---|---|
| ![Booking time](docs/screenshots/07-booking-time.png) | ![Booking ticket](docs/screenshots/09-ticket.png) |

<p align="center"><b>The working day, in one place.</b></p>
<p align="center"><img src="docs/screenshots/16-timeline.png" alt="Staff schedule with readable short visits" width="100%"></p>

| Business overview | Staff accounts and access |
|---|---|
| ![Dashboard](docs/screenshots/19-dashboard.png) | ![Team](docs/screenshots/23-team.png) |
| **Account security** | **Service management** |
| ![Security](docs/screenshots/12-security.png) | ![Services](docs/screenshots/21-services.png) |

<details><summary><b>Windows setup, host control and mobile views</b></summary>

| First-run setup | Running host |
|---|---|
| ![Windows setup](docs/screenshots/29-windows-setup.png) | ![Windows host](docs/screenshots/30-windows-host.png) |

<p align="center"><img src="docs/screenshots/13-mobile-home.png" width="28%" alt="Mobile home"> <img src="docs/screenshots/14-mobile-bookings.png" width="28%" alt="Mobile bookings"> <img src="docs/screenshots/28-mobile-timeline.png" width="28%" alt="Mobile staff schedule"></p>

</details>

## Accounts and access

| Action | Owner | Reception | Barber | Customer |
|---|:---:|:---:|:---:|:---:|
| View/manage the shared schedule | ✓ | ✓ | Own column | — |
| Create visits and reschedule | ✓ | ✓ | — | Own booking |
| Complete visits / record no-show | ✓ | ✓ | Own past visits | — |
| Edit catalog, team, business settings | ✓ | — | — | — |
| View customer contacts | ✓ | ✓ | Masked phone | Own |
| View/cancel own permitted bookings | ✓ | ✓ | ✓ | ✓ |
| Configure account MFA | ✓ | ✓ | ✓ | ✓ |

Staff accounts start with a temporary password and must replace it. Disabling an account invalidates existing authenticated sessions. A booking locator alone never grants access; optional guest bookings use a separate 256-bit secret.

## Security, explained honestly

Passwords use ASP.NET Core Identity PBKDF2 hashing. Customer contact fields and authenticator token material use AES-256-GCM with distinct purposes and random nonces. The data key stays outside MySQL: Windows protects it with the current user's DPAPI; Linux uses private file permissions. Runtime database credentials permit CRUD, while schema upgrades use a temporary migration account. MySQL connections verify a private TLS CA; public Linux hosting uses HTTPS.

Authorization is enforced on the server. CSRF protection, request limits, optimistic versions, per-barber database locks and a script nonce CSP protect important flows. Secrets are never embedded in JavaScript. **DevTools can inspect data that the signed-in user is allowed to receive.** Encryption cannot protect a server from its own OS administrator or malware running as its host account. Backups, updates and host access remain essential. Read the [security model](docs/SECURITY.md).

## For developers

**C# / .NET 10 · ASP.NET Core MVC + JSON endpoints · Identity · EF Core 10 · Oracle MySQL provider · MySQL 8.4 LTS · Bootstrap 5.3 · vanilla JavaScript · Windows Forms · Docker Compose · Caddy.**

```text
BarberBooking.Web/  Controllers, booking rules, Identity, EF migrations, Razor views
Bladez.Core/       Private files, field encryption, MySQL provisioning, host lifecycle, backups
Bladez.Host/       Windows setup and server control
Bladez.Tests/      Integration/security checks against an isolated real MySQL
 deploy/linux/     VPS installer, Compose, HTTPS proxy, maintenance and encrypted backups
 deploy/windows/   Windows installer definition and release packaging
```

Install .NET SDK 10.0.401, then run `dotnet build BarberBooking.slnx`. Use a new host profile/database, not the old demo database. See [development setup, architecture and API contracts](docs/DEVELOPMENT.md). `dotnet run --project Bladez.Tests` provisions a private test MySQL on Windows; Linux CI supplies an isolated TLS MySQL. Tests exercise concurrent booking, role access, IDOR, encrypted storage, CSRF and real TOTP.

The default `main` history and the development branch are preserved. New work is on `redbox/business-release`.

## Scope and next steps

Included: accounts, roles, MFA, catalog management, booking lifecycle, schedule/time off, audit, local Windows hosting and Linux deployment. SMTP account email is optional and requires your own server credentials.

Not included: payment processing, SMS verification, a tax/POS system, multi-location tenancy or guaranteed regulatory certification. Phone numbers are not marked verified. Business policy, retention periods, real staff information and the privacy notice must be configured by the operator. [Third-party notices](docs/THIRD-PARTY.md) describe bundled dependencies and artwork.
