-- Shikzya server-side schema for the Attendance Bridge (fleet model).
-- These tables live in the Shikzya database. The bridge agents NEVER touch MySQL
-- directly - they call the HTTPS API, and the API reads/writes these tables.
-- MySQL 5.7+ / 8.x, InnoDB, utf8mb4. Multi-tenant via tenant_id.

-- One row per agent / school site. The agent authenticates with site_token.
-- In production store a HASH of the token, not the plaintext.
CREATE TABLE IF NOT EXISTS bio_site (
  site_id       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id     VARCHAR(64) NOT NULL,
  name          VARCHAR(128) NOT NULL,
  site_token    CHAR(64) NOT NULL,
  active        TINYINT NOT NULL DEFAULT 1,
  agent_version VARCHAR(32) NULL,
  last_seen_at  DATETIME NULL,
  PRIMARY KEY (site_id),
  UNIQUE KEY uq_token (site_token),
  KEY idx_tenant (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Devices assigned to a site. Configured centrally in Shikzya admin; the agent
-- reads this list and services every active device. Adding a row here is all it
-- takes to onboard a new device - no school-side work.
CREATE TABLE IF NOT EXISTS bio_device (
  device_id       INT NOT NULL,
  site_id         BIGINT UNSIGNED NOT NULL,
  tenant_id       VARCHAR(64) NOT NULL,
  name            VARCHAR(64) NOT NULL,
  ip              VARCHAR(45) NOT NULL,
  port            INT NOT NULL DEFAULT 5005,
  machine_no      INT NOT NULL DEFAULT 1,
  net_password    INT NOT NULL DEFAULT 0,
  license         INT NOT NULL DEFAULT 1261,
  timeout_ms      INT NOT NULL DEFAULT 5000,
  protocol        INT NOT NULL DEFAULT 0,            -- 0 TCP/IP, 1 UDP
  pull_times      VARCHAR(255) NOT NULL DEFAULT '',  -- e.g. "12:00,17:00"
  time_sync_drift INT NOT NULL DEFAULT 30,
  active          TINYINT NOT NULL DEFAULT 1,
  last_pull_at    DATETIME NULL,
  last_status     VARCHAR(255) NULL,
  PRIMARY KEY (site_id, device_id),
  KEY idx_tenant (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Attendance punches uploaded by the agent. Server enforces de-duplication via
-- the unique key, so re-uploads (including offline spool retries) never duplicate.
CREATE TABLE IF NOT EXISTS bio_punch (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id       VARCHAR(64) NOT NULL,
  site_id         BIGINT UNSIGNED NOT NULL,
  device_id       INT NOT NULL,
  enroll_number   INT NOT NULL,                 -- device-side user id (NOT the student id)
  punch_time      DATETIME NOT NULL,
  verify_mode     INT NOT NULL,
  verify_label    VARCHAR(64) NULL,
  in_out_mode     INT NOT NULL,
  io_mode         INT NOT NULL DEFAULT 0,
  door_mode       INT NOT NULL DEFAULT 0,
  raw_temperature SMALLINT NULL,
  processed       TINYINT NOT NULL DEFAULT 0,
  imported_at     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE KEY uq_punch (tenant_id, device_id, enroll_number, punch_time, in_out_mode),
  KEY idx_tenant_time (tenant_id, punch_time),
  KEY idx_unprocessed (tenant_id, processed, punch_time)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Maps a device enroll_number to a person in Shikzya.
CREATE TABLE IF NOT EXISTS bio_enroll_map (
  tenant_id     VARCHAR(64) NOT NULL,
  device_id     INT NOT NULL,
  enroll_number INT NOT NULL,
  person_type   ENUM('student','staff') NOT NULL,
  person_id     BIGINT UNSIGNED NOT NULL,
  active        TINYINT NOT NULL DEFAULT 1,
  PRIMARY KEY (tenant_id, device_id, enroll_number)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- On-demand fetch queue. Shikzya inserts 'pending' when a school clicks "Fetch
-- attendance" the API hands it to the site's agent, which pulls and reports back.
CREATE TABLE IF NOT EXISTS bio_fetch_command (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id        VARCHAR(64) NOT NULL,
  site_id          BIGINT UNSIGNED NOT NULL,
  device_id        INT NOT NULL,
  status           ENUM('pending','running','done','error') NOT NULL DEFAULT 'pending',
  requested_by     VARCHAR(64) NULL,
  requested_at     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  finished_at      DATETIME NULL,
  records_read     INT NULL,
  records_inserted INT NULL,
  result_message   VARCHAR(255) NULL,
  PRIMARY KEY (id),
  KEY idx_pending (site_id, status, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
