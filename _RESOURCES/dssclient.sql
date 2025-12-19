-- --------------------------------------------------------
-- Host:                         127.0.0.1
-- Server version:               11.8.2-MariaDB - mariadb.org binary distribution
-- Server OS:                    Win64
-- HeidiSQL Version:             12.10.0.7000
-- --------------------------------------------------------

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET NAMES utf8 */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;


-- Dumping database structure for dss_client
CREATE DATABASE IF NOT EXISTS `dss_client` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci */;
USE `dss_client`;

-- Dumping structure for table dss_client.chunked_files
CREATE TABLE IF NOT EXISTS `chunked_files` (
  `id` bigint(20) NOT NULL,
  `part` bigint(20) NOT NULL,
  `size` int(11) NOT NULL DEFAULT 0 COMMENT 'in MB',
  `uplodaed` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`,`part`),
  CONSTRAINT `fk_chunked_files_chunk_info` FOREIGN KEY (`id`) REFERENCES `chunk_info` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_client.chunk_info
CREATE TABLE IF NOT EXISTS `chunk_info` (
  `id` bigint(20) NOT NULL,
  `size` bigint(20) NOT NULL DEFAULT 1 COMMENT 'in MB',
  `parts` int(11) NOT NULL DEFAULT 2,
  `name` varchar(64) NOT NULL COMMENT 'directory name for the for the chunked_files. Inside the directory, we create files with just numbers. file.mp4.001, file.mp4.002 etc.',
  `path` varchar(400) NOT NULL COMMENT 'chunked directory full path (including folder name)',
  `is_completed` bit(1) NOT NULL DEFAULT b'0',
  PRIMARY KEY (`id`),
  CONSTRAINT `fk_chunk_info_doc_version` FOREIGN KEY (`id`) REFERENCES `doc_version` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `cns_chunk_info` CHECK (`parts` > 1),
  CONSTRAINT `cns_chunk_info_0` CHECK (`size` > 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_client.directory
CREATE TABLE IF NOT EXISTS `directory` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `display_name` varchar(120) NOT NULL,
  `created` timestamp NOT NULL DEFAULT current_timestamp(),
  `modified` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `cuid` varchar(48) NOT NULL DEFAULT 'uuid()',
  `deleted` bit(1) NOT NULL DEFAULT b'0' COMMENT 'soft delete',
  `name` varchar(120) NOT NULL,
  `parent` bigint(20) NOT NULL DEFAULT 0 COMMENT 'Can be null for root folders. We mark it as 0 for root folders',
  `workspace` bigint(20) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_directory` (`workspace`,`parent`,`name`),
  UNIQUE KEY `unq_directory_0` (`cuid`),
  CONSTRAINT `fk_directory_workspace` FOREIGN KEY (`workspace`) REFERENCES `workspace` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB AUTO_INCREMENT=1000 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_client.document
CREATE TABLE IF NOT EXISTS `document` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `cuid` varchar(48) NOT NULL DEFAULT 'uuid()' COMMENT 'Collision Resistant Global unique identifier',
  `created` timestamp NOT NULL DEFAULT current_timestamp(),
  `parent` bigint(20) NOT NULL,
  `name` bigint(20) NOT NULL,
  `modified` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `deleted` bit(1) NOT NULL DEFAULT b'0' COMMENT 'Soft delete',
  `workspace` bigint(20) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_file_index` (`cuid`),
  UNIQUE KEY `unq_document` (`parent`,`name`),
  KEY `fk_file_index_parent` (`workspace`),
  KEY `fk_document_directory` (`parent`),
  KEY `fk_document_name_store` (`name`),
  CONSTRAINT `fk_document_directory` FOREIGN KEY (`parent`) REFERENCES `directory` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_document_name_store` FOREIGN KEY (`name`) REFERENCES `name_store` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_document_workspace` FOREIGN KEY (`workspace`) REFERENCES `workspace` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB AUTO_INCREMENT=1988 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_client.doc_info
CREATE TABLE IF NOT EXISTS `doc_info` (
  `file` bigint(20) NOT NULL,
  `display_name` varchar(200) NOT NULL,
  PRIMARY KEY (`file`),
  KEY `idx_doc_info` (`display_name`),
  CONSTRAINT `fk_file_info_file_index_0` FOREIGN KEY (`file`) REFERENCES `document` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_client.doc_version
CREATE TABLE IF NOT EXISTS `doc_version` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `cuid` varchar(48) NOT NULL DEFAULT 'uuid()',
  `created` timestamp NOT NULL DEFAULT current_timestamp(),
  `ver` int(11) NOT NULL DEFAULT 1,
  `parent` bigint(20) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_file_version` (`parent`,`ver`),
  UNIQUE KEY `unq_doc_version` (`cuid`),
  KEY `idx_file_version_0` (`created`),
  CONSTRAINT `fk_doc_version_document` FOREIGN KEY (`parent`) REFERENCES `document` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB AUTO_INCREMENT=1996 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_client.extension
CREATE TABLE IF NOT EXISTS `extension` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(100) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_extension` (`name`)
) ENGINE=InnoDB AUTO_INCREMENT=1000 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_client.name_store
CREATE TABLE IF NOT EXISTS `name_store` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `extension` int(11) NOT NULL,
  `name` bigint(20) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_name_store` (`name`,`extension`),
  KEY `idx_name_store_0` (`extension`,`name`),
  CONSTRAINT `fk_name_store_extension` FOREIGN KEY (`extension`) REFERENCES `extension` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_name_store_name_vault` FOREIGN KEY (`name`) REFERENCES `vault` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB AUTO_INCREMENT=900 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_client.vault
CREATE TABLE IF NOT EXISTS `vault` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `name` varchar(200) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_name_store` (`name`)
) ENGINE=InnoDB AUTO_INCREMENT=500 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_client.version_info
CREATE TABLE IF NOT EXISTS `version_info` (
  `id` bigint(20) NOT NULL,
  `storage_name` varchar(200) NOT NULL COMMENT 'name with which the storage provider identifies it.. For FS, provider, it might be a  number or a GUID. For B2 , S3 , Azure provider or something else, it might be the refernce id provided by that service',
  `storage_path` text NOT NULL,
  `staging_path` varchar(300) DEFAULT NULL COMMENT 'Optional Staging path, if present.',
  `size` bigint(20) NOT NULL DEFAULT 0 COMMENT 'SIZE IN BYTES',
  `metadata` text DEFAULT NULL,
  `flags` int(11) NOT NULL DEFAULT 0 COMMENT 'Flags:\n0 - None\n1 - Chunked-Upload Mode (Marked)\n2 - Uploaded to Chunking Area (Chunks are always deleted upon moving out)\n4 - Uploaded to Staging Area (Optional)\n8 - Uploaded to Storage Area (Final)\n16 - Deleted Chunked Files (Mandatory)\n32 - Deleted Staging Copy (Optional)\n64 - Upload Process Completed',
  PRIMARY KEY (`id`),
  KEY `idx_version_info` (`storage_name`),
  CONSTRAINT `fk_version_info_doc_version` FOREIGN KEY (`id`) REFERENCES `doc_version` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_client.workspace
CREATE TABLE IF NOT EXISTS `workspace` (
  `id` bigint(20) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

/*!40103 SET TIME_ZONE=IFNULL(@OLD_TIME_ZONE, 'system') */;
/*!40101 SET SQL_MODE=IFNULL(@OLD_SQL_MODE, '') */;
/*!40014 SET FOREIGN_KEY_CHECKS=IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1) */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40111 SET SQL_NOTES=IFNULL(@OLD_SQL_NOTES, 1) */;
