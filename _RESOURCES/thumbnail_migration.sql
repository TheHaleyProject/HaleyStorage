-- ============================================================
-- Thumbnail sub_ver migration
-- Removes: kind column (doc_version), thumb column (version_info)
-- Adds:    sub_ver column (doc_version), new unique key
-- Run against every per-module (dss_client_*) database.
-- ============================================================

SET FOREIGN_KEY_CHECKS = 0;

-- 1. Remove the kind column (no longer needed — sub_ver carries the discriminator).
ALTER TABLE doc_version
    DROP COLUMN `kind`;

-- 2. Add sub_ver: 0 = content version (default), 1+ = thumbnail sub-versions.
ALTER TABLE doc_version
    ADD COLUMN `sub_ver` int(11) NOT NULL DEFAULT 0
    COMMENT '0 = content version (default). 1, 2, 3… = thumbnail sub-versions under the same content version. Combined with (parent, ver) to form the new unique key.';

-- 3. Drop the old (parent, ver) unique key and recreate as (parent, ver, sub_ver).
ALTER TABLE doc_version
    DROP INDEX `unq_file_version`,
    ADD UNIQUE KEY `unq_file_version` (`parent`, `ver`, `sub_ver`);

-- 4. Drop the thumb FK column from version_info (linkage is now structural via sub_ver).
ALTER TABLE version_info
    DROP COLUMN IF EXISTS `thumb`;

SET FOREIGN_KEY_CHECKS = 1;
