-- AttendanceBridge schema
-- Apply to the MySQL database Shikzya uses. MySQL 5.7+ / 8.x, InnoDB, utf8mb4.
-- The bridge creates these automatically on first DB use (CREATE TABLE IF NOT EXISTS).
--
-- Multi-tenant: every row carries tenant_id (the school) so one shared database
-- can hold many schools. Each bridge deployment is configured with its tenant_id.

-- Raw attendance punches. The bridge writes; Shikzya reads (filtered by tenant_id).
CREATE TABLE IF NOT EXISTS bio_punch (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id       VARCHAR(64) NOT NULL,          -- school / Shikzya tenant
  device_id       INT NOT NULL,
  enroll_number   INT NOT NULL,                 -- device-side user id (NOT the student id)
  punch_time      DATETIME NOT NULL,
  verify_mode     INT NOT NULL,                 -- raw FK verify code (bit-packed on newer firmware)
  verify_label    VARCHAR(64) NULL,             -- decoded, e.g. 'FP', 'FACE', 'Card+FP'
  in_out_mode     INT NOT NULL,                 -- raw FK in/out code (low byte = io, high bytes = door)
  io_mode         INT NOT NULL DEFAULT 0,       -- decoded in/out (low byte of in_out_mode)
  door_mode       INT NOT NULL DEFAULT 0,       -- decoded door mode (high bytes of in_out_mode)
  dedup_key       CHAR(40) NOT NULL,            -- SHA1(tenant_id|device_id|enroll|punch_time|in_out_mode)
  raw_temperature SMALLINT NULL,                -- tenths of a degree, or NULL if unsupported
  processed       TINYINT NOT NULL DEFAULT 0,   -- set by the Shikzya attendance engine
  imported_at     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE KEY uq_dedup (dedup_key),
  KEY idx_tenant_time (tenant_id, punch_time),
  KEY idx_unprocessed (tenant_id, processed, punch_time),
  KEY idx_enroll_time (tenant_id, enroll_number, punch_time)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Maps a device enroll_number to a person in the school system.
CREATE TABLE IF NOT EXISTS bio_enroll_map (
  tenant_id     VARCHAR(64) NOT NULL,
  device_id     INT NOT NULL,
  enroll_number INT NOT NULL,
  person_type   ENUM('student','staff') NOT NULL,
  person_id     BIGINT UNSIGNED NOT NULL,       -- FK into the existing students/staff table
  active        TINYINT NOT NULL DEFAULT 1,
  PRIMARY KEY (tenant_id, device_id, enroll_number)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- One row per physical device: connection details + last-pull health.
CREATE TABLE IF NOT EXISTS bio_device (
  tenant_id     VARCHAR(64) NOT NULL,
  device_id     INT NOT NULL,
  name          VARCHAR(64) NOT NULL,
  ip_address    VARCHAR(45) NOT NULL,
  net_port      INT NOT NULL DEFAULT 5005,
  net_password  INT NOT NULL DEFAULT 0,
  license       INT NOT NULL DEFAULT 1261,
  last_pull_at  DATETIME NULL,
  last_punch_at DATETIME NULL,
  last_status   VARCHAR(255) NULL,
  PRIMARY KEY (tenant_id, device_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- On-demand fetch queue. Shikzya inserts a 'pending' row when a school clicks
-- "Fetch attendance"; the school's bridge claims it, pulls, and marks it done.
CREATE TABLE IF NOT EXISTS bio_fetch_command (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id        VARCHAR(64) NOT NULL,
  device_id        INT NOT NULL,
  status           ENUM('pending','running','done','error') NOT NULL DEFAULT 'pending',
  requested_by     VARCHAR(64) NULL,            -- who clicked (Shikzya user), optional
  requested_at     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  started_at       DATETIME NULL,
  finished_at      DATETIME NULL,
  records_read     INT NULL,
  records_inserted INT NULL,
  result_message   VARCHAR(255) NULL,
  PRIMARY KEY (id),
  KEY idx_pending (tenant_id, device_id, status, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Operational log of the bridge itself (errors, reconnects, pull counts).
CREATE TABLE IF NOT EXISTS bio_bridge_log (
  id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id VARCHAR(64) NULL,
  level     ENUM('INFO','WARN','ERROR') NOT NULL,
  event     VARCHAR(64) NOT NULL,
  message   TEXT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
