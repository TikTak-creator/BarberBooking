# Product screenshots / Экраны приложения

Screenshots are captured from a running local release build with an isolated QA database. Names, contacts and visits shown are test fixtures. The installer optionally seeds only an editable example catalog, never these customers or appointments.

The gallery contains 28 browser captures and 2 native Windows Host captures. The browser run completed booking, account and owner flows at desktop and 390px mobile widths without JavaScript errors or external asset requests. The artwork in `assets/bladez-banner.png` is documentation decoration, not an application screenshot.

## Customer / Клиент

| Home / Главная | Sign in / Вход |
|---|---|
| ![Home](screenshots/01-home.png) | ![Login](screenshots/03-login.png) |
| **Registration / Регистрация** | **Choose service / Услуга** |
| ![Registration](screenshots/04-register.png) | ![Service](screenshots/05-booking-service.png) |
| **Choose barber / Мастер** | **Choose time / Время** |
| ![Barber](screenshots/06-booking-barber.png) | ![Time](screenshots/07-booking-time.png) |
| **Contact details / Контакты** | **Private ticket / Личный талон** |
| ![Contacts](screenshots/08-booking-contacts.png) | ![Ticket](screenshots/09-ticket.png) |
| **My bookings / Мои записи** | **Profile / Профиль** |
| ![Bookings](screenshots/10-my-bookings.png) | ![Profile](screenshots/11-profile.png) |

<details><summary>Full home page / Главная целиком</summary>

![Full home](screenshots/02-home-full.png)

</details>

## Business / Команда и владелец

![Schedule / Расписание](screenshots/16-timeline.png)

| Visit details / Карточка визита | Staff booking / Быстрая запись |
|---|---|
| ![Visit](screenshots/17-appointment-details.png) | ![Quick booking](screenshots/18-quick-booking.png) |
| **Daily overview / Обзор дня** | **Visit register / Журнал визитов** |
| ![Overview](screenshots/19-dashboard.png) | ![Register](screenshots/20-appointments.png) |
| **Services / Прайс** | **Barbers / Мастера** |
| ![Services](screenshots/21-services.png) | ![Barbers](screenshots/22-barbers.png) |
| **Accounts / Аккаунты** | **Time off / Перерывы** |
| ![Accounts](screenshots/23-team.png) | ![Time off](screenshots/24-time-off.png) |
| **Audit / Журнал действий** | **System status / Состояние сервера** |
| ![Audit](screenshots/26-audit.png) | ![System](screenshots/27-system.png) |
| **Account MFA / Второй фактор** | **Shop and SMTP settings / Настройки салона и почты** |
| ![Security](screenshots/12-security.png) | ![Settings](screenshots/25-settings.png) |

## Mobile / Телефон

<p><img src="screenshots/13-mobile-home.png" width="23%" alt="Mobile home"> <img src="screenshots/14-mobile-bookings.png" width="23%" alt="Mobile bookings"> <img src="screenshots/15-mobile-security.png" width="23%" alt="Mobile security"> <img src="screenshots/28-mobile-timeline.png" width="23%" alt="Mobile schedule"></p>

The schedule deliberately scrolls inside its grid on small screens. The page itself has no horizontal overflow. Short appointments show time and name at a stable line height; the modal exposes the full details.

## Windows Host

| First setup / Первая настройка | Live server / Работающий сервер |
|---|---|
| ![Setup](screenshots/29-windows-setup.png) | ![Host](screenshots/30-windows-host.png) |

The running-host capture starts the actual private QA MySQL and self-contained web executable, waits for readiness, renders the real window and then shuts its processes down. These are not mockups.
