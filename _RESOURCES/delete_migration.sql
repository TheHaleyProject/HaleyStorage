-- ============================================================
-- Delete / archive / restore migration
-- Moves lifecycle columns to:
--   delete_state tinyint
--   deleted datetime null
-- Run against every per-module (dss_client_*) database.
-- ============================================================

SET @OLD_FOREIGN_KEY_CHECKS = @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;

-- ── directory ────────────────────────────────────────────────
SET @has_directory_deleted_bit = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'directory'
      AND COLUMN_NAME = 'deleted'
      AND DATA_TYPE = 'bit'
);
SET @sql_directory_rename_deleted = IF(
    @has_directory_deleted_bit > 0,
    'ALTER TABLE `directory` CHANGE COLUMN `deleted` `deleted_old` bit(1) NOT NULL DEFAULT b''0'';',
    'SELECT 1;'
);
PREPARE stmt_directory_rename_deleted FROM @sql_directory_rename_deleted;
EXECUTE stmt_directory_rename_deleted;
DEALLOCATE PREPARE stmt_directory_rename_deleted;

ALTER TABLE `directory`
    ADD COLUMN IF NOT EXISTS `delete_state` tinyint(4) NOT NULL DEFAULT 0
    COMMENT 'Lifecycle state. 0=active, 1=soft deleted, 2=archived, 3=purged.' AFTER `cuid`,
    ADD COLUMN IF NOT EXISTS `deleted` datetime DEFAULT NULL
    COMMENT 'UTC timestamp when this row entered a non-active delete_state. NULL while active.' AFTER `delete_state`;

SET @has_directory_deleted_old = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'directory'
      AND COLUMN_NAME = 'deleted_old'
);
SET @sql_directory_migrate = IF(
    @has_directory_deleted_old > 0,
    'UPDATE `directory` SET `delete_state` = CASE WHEN `deleted_old` = b''1'' THEN 1 ELSE 0 END, `deleted` = COALESCE(`deleted_at`, `deleted`);',
    'SELECT 1;'
);
PREPARE stmt_directory_migrate FROM @sql_directory_migrate;
EXECUTE stmt_directory_migrate;
DEALLOCATE PREPARE stmt_directory_migrate;

SET @has_directory_deleted_at = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'directory'
      AND COLUMN_NAME = 'deleted_at'
);
SET @sql_directory_drop_deleted_at = IF(
    @has_directory_deleted_at > 0,
    'ALTER TABLE `directory` DROP COLUMN `deleted_at`;',
    'SELECT 1;'
);
PREPARE stmt_directory_drop_deleted_at FROM @sql_directory_drop_deleted_at;
EXECUTE stmt_directory_drop_deleted_at;
DEALLOCATE PREPARE stmt_directory_drop_deleted_at;

SET @sql_directory_drop_deleted_old = IF(
    @has_directory_deleted_old > 0,
    'ALTER TABLE `directory` DROP COLUMN `deleted_old`;',
    'SELECT 1;'
);
PREPARE stmt_directory_drop_deleted_old FROM @sql_directory_drop_deleted_old;
EXECUTE stmt_directory_drop_deleted_old;
DEALLOCATE PREPARE stmt_directory_drop_deleted_old;

-- ── document ─────────────────────────────────────────────────
SET @has_document_deleted_bit = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'document'
      AND COLUMN_NAME = 'deleted'
      AND DATA_TYPE = 'bit'
);
SET @sql_document_rename_deleted = IF(
    @has_document_deleted_bit > 0,
    'ALTER TABLE `document` CHANGE COLUMN `deleted` `deleted_old` bit(1) NOT NULL DEFAULT b''0'';',
    'SELECT 1;'
);
PREPARE stmt_document_rename_deleted FROM @sql_document_rename_deleted;
EXECUTE stmt_document_rename_deleted;
DEALLOCATE PREPARE stmt_document_rename_deleted;

ALTER TABLE `document`
    ADD COLUMN IF NOT EXISTS `original_name` bigint(20) DEFAULT NULL
    COMMENT 'FK -> name_store.id. Preserves the original logical filename when a deleted document is tombstoned to free the active (parent,name) slot for a re-upload. NULL while the document still owns its active name.' AFTER `name`,
    ADD COLUMN IF NOT EXISTS `delete_state` tinyint(4) NOT NULL DEFAULT 0
    COMMENT 'Lifecycle state. 0=active, 1=soft deleted, 2=archived, 3=purged.' AFTER `original_name`,
    ADD COLUMN IF NOT EXISTS `deleted` datetime DEFAULT NULL
    COMMENT 'UTC timestamp when this row entered a non-active delete_state. NULL while active.' AFTER `delete_state`;

SET @has_document_deleted_old = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'document'
      AND COLUMN_NAME = 'deleted_old'
);
SET @sql_document_migrate = IF(
    @has_document_deleted_old > 0,
    'UPDATE `document` SET `delete_state` = CASE WHEN `deleted_old` = b''1'' THEN 1 ELSE 0 END, `deleted` = COALESCE(`deleted_at`, `deleted`);',
    'SELECT 1;'
);
PREPARE stmt_document_migrate FROM @sql_document_migrate;
EXECUTE stmt_document_migrate;
DEALLOCATE PREPARE stmt_document_migrate;

SET @has_document_deleted_at = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'document'
      AND COLUMN_NAME = 'deleted_at'
);
SET @sql_document_drop_deleted_at = IF(
    @has_document_deleted_at > 0,
    'ALTER TABLE `document` DROP COLUMN `deleted_at`;',
    'SELECT 1;'
);
PREPARE stmt_document_drop_deleted_at FROM @sql_document_drop_deleted_at;
EXECUTE stmt_document_drop_deleted_at;
DEALLOCATE PREPARE stmt_document_drop_deleted_at;

SET @sql_document_drop_deleted_old = IF(
    @has_document_deleted_old > 0,
    'ALTER TABLE `document` DROP COLUMN `deleted_old`;',
    'SELECT 1;'
);
PREPARE stmt_document_drop_deleted_old FROM @sql_document_drop_deleted_old;
EXECUTE stmt_document_drop_deleted_old;
DEALLOCATE PREPARE stmt_document_drop_deleted_old;

SET @has_document_original_name_idx = (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'document'
      AND INDEX_NAME = 'fk_document_original_name_store'
);
SET @sql_document_original_name_idx = IF(
    @has_document_original_name_idx = 0,
    'CREATE INDEX `fk_document_original_name_store` ON `document` (`original_name`);',
    'SELECT 1;'
);
PREPARE stmt_document_original_name_idx FROM @sql_document_original_name_idx;
EXECUTE stmt_document_original_name_idx;
DEALLOCATE PREPARE stmt_document_original_name_idx;

SET @has_document_original_name_fk = (
    SELECT COUNT(*)
    FROM information_schema.TABLE_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE()
      AND TABLE_NAME = 'document'
      AND CONSTRAINT_NAME = 'fk_document_original_name_store'
);
SET @sql_document_original_name_fk = IF(
    @has_document_original_name_fk = 0,
    'ALTER TABLE `document` ADD CONSTRAINT `fk_document_original_name_store` FOREIGN KEY (`original_name`) REFERENCES `name_store` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION;',
    'SELECT 1;'
);
PREPARE stmt_document_original_name_fk FROM @sql_document_original_name_fk;
EXECUTE stmt_document_original_name_fk;
DEALLOCATE PREPARE stmt_document_original_name_fk;

-- ── doc_version ──────────────────────────────────────────────
SET @has_doc_version_deleted_bit = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'doc_version'
      AND COLUMN_NAME = 'deleted'
      AND DATA_TYPE = 'bit'
);
SET @sql_doc_version_rename_deleted = IF(
    @has_doc_version_deleted_bit > 0,
    'ALTER TABLE `doc_version` CHANGE COLUMN `deleted` `deleted_old` bit(1) NOT NULL DEFAULT b''0'';',
    'SELECT 1;'
);
PREPARE stmt_doc_version_rename_deleted FROM @sql_doc_version_rename_deleted;
EXECUTE stmt_doc_version_rename_deleted;
DEALLOCATE PREPARE stmt_doc_version_rename_deleted;

ALTER TABLE `doc_version`
    ADD COLUMN IF NOT EXISTS `delete_state` tinyint(4) NOT NULL DEFAULT 0
    COMMENT 'Lifecycle state for this version row. 0=active, 1=soft deleted, 2=archived, 3=purged.' AFTER `sub_ver`,
    ADD COLUMN IF NOT EXISTS `deleted` datetime DEFAULT NULL
    COMMENT 'UTC timestamp when this version row entered a non-active delete_state. NULL while active.' AFTER `delete_state`;

SET @has_doc_version_deleted_old = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'doc_version'
      AND COLUMN_NAME = 'deleted_old'
);
SET @sql_doc_version_migrate = IF(
    @has_doc_version_deleted_old > 0,
    'UPDATE `doc_version` SET `delete_state` = CASE WHEN `deleted_old` = b''1'' THEN 1 ELSE 0 END, `deleted` = COALESCE(`deleted_at`, `deleted`);',
    'SELECT 1;'
);
PREPARE stmt_doc_version_migrate FROM @sql_doc_version_migrate;
EXECUTE stmt_doc_version_migrate;
DEALLOCATE PREPARE stmt_doc_version_migrate;

SET @has_doc_version_deleted_at = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'doc_version'
      AND COLUMN_NAME = 'deleted_at'
);
SET @sql_doc_version_drop_deleted_at = IF(
    @has_doc_version_deleted_at > 0,
    'ALTER TABLE `doc_version` DROP COLUMN `deleted_at`;',
    'SELECT 1;'
);
PREPARE stmt_doc_version_drop_deleted_at FROM @sql_doc_version_drop_deleted_at;
EXECUTE stmt_doc_version_drop_deleted_at;
DEALLOCATE PREPARE stmt_doc_version_drop_deleted_at;

SET @sql_doc_version_drop_deleted_old = IF(
    @has_doc_version_deleted_old > 0,
    'ALTER TABLE `doc_version` DROP COLUMN `deleted_old`;',
    'SELECT 1;'
);
PREPARE stmt_doc_version_drop_deleted_old FROM @sql_doc_version_drop_deleted_old;
EXECUTE stmt_doc_version_drop_deleted_old;
DEALLOCATE PREPARE stmt_doc_version_drop_deleted_old;

SET @has_old_doc_version_idx = (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'doc_version'
      AND INDEX_NAME = 'idx_doc_version_parent_subver_ver'
);
SET @sql_drop_old_doc_version_idx = IF(
    @has_old_doc_version_idx > 0,
    'DROP INDEX `idx_doc_version_parent_subver_ver` ON `doc_version`;',
    'SELECT 1;'
);
PREPARE stmt_drop_old_doc_version_idx FROM @sql_drop_old_doc_version_idx;
EXECUTE stmt_drop_old_doc_version_idx;
DEALLOCATE PREPARE stmt_drop_old_doc_version_idx;

SET @has_doc_version_delete_state_idx = (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'doc_version'
      AND INDEX_NAME = 'idx_doc_version_parent_delete_state_subver_ver'
);
SET @sql_doc_version_delete_state_idx = IF(
    @has_doc_version_delete_state_idx = 0,
    'CREATE INDEX `idx_doc_version_parent_delete_state_subver_ver` ON `doc_version` (`parent`, `delete_state`, `sub_ver`, `ver`);',
    'SELECT 1;'
);
PREPARE stmt_doc_version_delete_state_idx FROM @sql_doc_version_delete_state_idx;
EXECUTE stmt_doc_version_delete_state_idx;
DEALLOCATE PREPARE stmt_doc_version_delete_state_idx;

SET @has_doc_version_ver_delete_state_idx = (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'doc_version'
      AND INDEX_NAME = 'idx_doc_version_parent_ver_delete_state_subver'
);
SET @sql_doc_version_ver_delete_state_idx = IF(
    @has_doc_version_ver_delete_state_idx = 0,
    'CREATE INDEX `idx_doc_version_parent_ver_delete_state_subver` ON `doc_version` (`parent`, `ver`, `delete_state`, `sub_ver`);',
    'SELECT 1;'
);
PREPARE stmt_doc_version_ver_delete_state_idx FROM @sql_doc_version_ver_delete_state_idx;
EXECUTE stmt_doc_version_ver_delete_state_idx;
DEALLOCATE PREPARE stmt_doc_version_ver_delete_state_idx;

-- ── supporting indexes ───────────────────────────────────────
SET @has_old_dir_idx = (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'directory'
      AND INDEX_NAME = 'idx_directory_workspace_parent_deleted'
);
SET @sql_drop_old_dir_idx = IF(
    @has_old_dir_idx > 0,
    'DROP INDEX `idx_directory_workspace_parent_deleted` ON `directory`;',
    'SELECT 1;'
);
PREPARE stmt_drop_old_dir_idx FROM @sql_drop_old_dir_idx;
EXECUTE stmt_drop_old_dir_idx;
DEALLOCATE PREPARE stmt_drop_old_dir_idx;

SET @has_new_dir_idx = (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'directory'
      AND INDEX_NAME = 'idx_directory_workspace_parent_delete_state'
);
SET @sql_new_dir_idx = IF(
    @has_new_dir_idx = 0,
    'CREATE INDEX `idx_directory_workspace_parent_delete_state` ON `directory` (`workspace`, `parent`, `delete_state`);',
    'SELECT 1;'
);
PREPARE stmt_new_dir_idx FROM @sql_new_dir_idx;
EXECUTE stmt_new_dir_idx;
DEALLOCATE PREPARE stmt_new_dir_idx;

SET @has_old_doc_idx = (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'document'
      AND INDEX_NAME = 'idx_document_workspace_parent_deleted'
);
SET @sql_drop_old_doc_idx = IF(
    @has_old_doc_idx > 0,
    'DROP INDEX `idx_document_workspace_parent_deleted` ON `document`;',
    'SELECT 1;'
);
PREPARE stmt_drop_old_doc_idx FROM @sql_drop_old_doc_idx;
EXECUTE stmt_drop_old_doc_idx;
DEALLOCATE PREPARE stmt_drop_old_doc_idx;

SET @has_new_doc_idx = (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'document'
      AND INDEX_NAME = 'idx_document_workspace_parent_delete_state'
);
SET @sql_new_doc_idx = IF(
    @has_new_doc_idx = 0,
    'CREATE INDEX `idx_document_workspace_parent_delete_state` ON `document` (`workspace`, `parent`, `delete_state`);',
    'SELECT 1;'
);
PREPARE stmt_new_doc_idx FROM @sql_new_doc_idx;
EXECUTE stmt_new_doc_idx;
DEALLOCATE PREPARE stmt_new_doc_idx;

SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
