-- AttendanceBridge schema (Phase 2)
-- Apply to the MySQL database the PHP school system uses (or a dedicated
-- bridge database it can read across). MySQL 5.7+ / 8.x, InnoDB, utf8mb4.

-- Raw attendance punches. The bridge writes; PHP reads.
CREATE TABLE IF NOT EXISTS bio_punch (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  device_id       INT NOT NULL,
  enroll_number   INT NOT NULL,                 -- device-side user id (NOT the student id)
  punch_time      DATETIME NOT NULL,
  verify_mode     INT NOT NULL,                 -- raw FK verify code (bit-packed on newer firmware)
  verify_label    VARCHAR(64) NULL,             -- decoded, e.g. 'FP', 'FACE', 'Card+FP'
  in_out_mode     INT NOT NULL,                 -- raw FK in/out code (low byte = io, high bytes = door)
  io_mode         INT NOT NULL DEFAULT 0,       -- decoded in/out (low byte of in_out_mode)
  door_mode       INT NOT NULL DEFAULT 0,       -- decoded door mode (high bytes of in_out_mode)
  dedup_key       CHAR(40) NOT NULL,            -- SHA1(device_id|enroll|punch_time|in_out_mode)
  raw_temperature SMALLINT NULL,                -- tenths of a degree, or NULL if unsupported
  processed       TINYINT NOT NULL DEFAULT 0,   -- set by the PHP attendance engine
  imported_at     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE KEY uq_dedup (dedup_key),
  KEY idx_unprocessed (processed, punch_time),
  KEY idx_enroll_time (enroll_number, punch_time)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Maps a device enroll_number to a person in the school system.
CREATE TABLE IF NOT EXISTS bio_enroll_map (
  device_id     INT NOT NULL,
  enroll_number INT NOT NULL,
  person_type   ENUM('student','staff') NOT NULL,
  person_id     BIGINT UNSIGNED NOT NULL,       -- FK into the existing students/staff table
  active        TINYINT NOT NULL DEFAULT 1,
  PRIMARY KEY (device_id, enroll_number)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- One row per physical device: connection details + last-pull health.
CREATE TABLE IF NOT EXISTS bio_device (
  device_id     INT NOT NULL,
  name          VARCHAR(64) NOT NULL,
  ip_address    VARCHAR(45) NOT NULL,
  net_port      INT NOT NULL DEFAULT 5005,
  net_password  INT NOT NULL DEFAULT 0,
  license       INT NOT NULL DEFAULT 1261,
  last_pull_at  DATETIME NULL,
  last_punch_at DATETIME NULL,
  last_status   VARCHAR(255) NULL,
  PRIMARY KEY (device_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Operational log of the bridge itself (errors, reconnects, pull counts).
CREATE TABLE IF NOT EXISTS bio_bridge_log (
  id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  level     ENUM('INFO','WARN','ERROR') NOT NULL,
  event     VARCHAR(64) NOT NULL,
  message   TEXT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
