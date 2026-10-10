-- =====================================================================
--  BarberFlow / Bladez — Онлайн-запись клиентов для барбершопа
--  Схема базы данных MySQL 8.x
--  (EF Core создаёт её автоматически при первом старте приложения;
--   данный скрипт предоставлен для ручного развёртывания и аудита)
-- =====================================================================

CREATE DATABASE IF NOT EXISTS barber_booking_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE barber_booking_db;

-- 1. Мастера барбершопа (Barbers)
CREATE TABLE IF NOT EXISTS barbers (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    full_name        VARCHAR(100) NOT NULL,
    title            VARCHAR(100) NOT NULL DEFAULT 'Senior Barber',
    bio              VARCHAR(500) NULL,
    avatar_url       VARCHAR(255) NOT NULL DEFAULT '/images/barbers/default.png',
    badge_color      VARCHAR(20)  NOT NULL DEFAULT '#F59E0B',
    rating           DOUBLE       NOT NULL DEFAULT 5.0,
    experience_years INT          NOT NULL DEFAULT 5,
    is_active        TINYINT(1)   NOT NULL DEFAULT 1,
    working_days     VARCHAR(50)  NOT NULL DEFAULT '1,2,3,4,5,6,0',
    work_start_time  TIME         NOT NULL DEFAULT '10:00:00',
    work_end_time    TIME         NOT NULL DEFAULT '21:00:00',
    INDEX ix_barbers_active (is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. Услуги и прайс-лист (Services)
CREATE TABLE IF NOT EXISTS services (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    name             VARCHAR(150)  NOT NULL,
    description      VARCHAR(500)  NULL,
    price            DECIMAL(12,2) NOT NULL DEFAULT 0,
    duration_minutes INT           NOT NULL DEFAULT 45,
    category         VARCHAR(100)  NOT NULL DEFAULT 'Стрижки',
    icon             VARCHAR(50)   NOT NULL DEFAULT 'bi-scissors',
    is_active        TINYINT(1)    NOT NULL DEFAULT 1,
    INDEX ix_services_active (is_active),
    INDEX ix_services_category (category)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. Записи клиентов (Appointments)
CREATE TABLE IF NOT EXISTS appointments (
    id                  INT AUTO_INCREMENT PRIMARY KEY,
    booking_code        VARCHAR(20)   NOT NULL UNIQUE,
    barber_id           INT           NOT NULL,
    service_id          INT           NOT NULL,
    client_name         VARCHAR(100)  NOT NULL,
    client_phone        VARCHAR(30)   NOT NULL,
    client_email        VARCHAR(100)  NULL,
    notes               VARCHAR(500)  NULL,
    appointment_date    DATE          NOT NULL,
    start_time          TIME          NOT NULL,
    end_time            TIME          NOT NULL,
    price               DECIMAL(12,2) NOT NULL DEFAULT 0,
    status              INT           NOT NULL DEFAULT 0, -- 0=Pending, 1=Confirmed, 2=Completed, 3=Cancelled
    created_at          DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    cancellation_reason VARCHAR(255)  NULL,

    -- Внешние ключи
    CONSTRAINT fk_app_barber  FOREIGN KEY (barber_id)  REFERENCES barbers(id)  ON DELETE RESTRICT,
    CONSTRAINT fk_app_service FOREIGN KEY (service_id) REFERENCES services(id) ON DELETE RESTRICT,

    -- Композитный индекс для быстрого поиска свободных слотов и предотвращения двойного бронирования
    INDEX ix_appointment_barber_date (barber_id, appointment_date, status),
    INDEX ix_appointment_booking_code (booking_code),
    INDEX ix_appointment_phone (client_phone)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
