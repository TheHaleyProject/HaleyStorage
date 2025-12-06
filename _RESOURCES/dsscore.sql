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


-- Dumping database structure for dss_core
CREATE DATABASE IF NOT EXISTS `dss_core` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci */;
USE `dss_core`;

-- Dumping structure for table dss_core.client
CREATE TABLE IF NOT EXISTS `client` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(100) NOT NULL,
  `display_name` varchar(100) NOT NULL,
  `guid` varchar(48) NOT NULL DEFAULT 'uuid()',
  `path` varchar(140) NOT NULL COMMENT 'Created only at register time.\nWe would have anyhow created the guid based on the provided name. If the client is created as managed, then the path should be based on the guid. or else it should be based on the name itself.',
  `created` timestamp NOT NULL DEFAULT current_timestamp(),
  `modified` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_client` (`name`),
  UNIQUE KEY `unq_client_1` (`guid`)
) ENGINE=InnoDB AUTO_INCREMENT=1975 DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_core.client_keys
CREATE TABLE IF NOT EXISTS `client_keys` (
  `client` int(11) NOT NULL,
  `signing` varchar(300) NOT NULL,
  `encrypt` varchar(300) NOT NULL,
  `password` varchar(120) NOT NULL,
  PRIMARY KEY (`client`),
  CONSTRAINT `fk_client_keys_client` FOREIGN KEY (`client`) REFERENCES `client` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for procedure dss_core.DropDatabasesWithPrefix
DELIMITER //
CREATE PROCEDURE `DropDatabasesWithPrefix`(IN prefix VARCHAR(100))
BEGIN
  DECLARE done INT DEFAULT FALSE;
  DECLARE db_name VARCHAR(255);
  DECLARE cur CURSOR FOR
    SELECT SCHEMA_NAME
    FROM INFORMATION_SCHEMA.SCHEMATA
    WHERE SCHEMA_NAME LIKE CONCAT(prefix, '%')
      AND SCHEMA_NAME NOT IN ('mysql', 'information_schema', 'performance_schema', 'sys');
  DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = TRUE;

  OPEN cur;

  read_loop: LOOP
    FETCH cur INTO db_name;
    IF done THEN
      LEAVE read_loop;
    END IF;
    SET @drop_stmt = CONCAT('DROP DATABASE `', db_name, '`');
    PREPARE stmt FROM @drop_stmt;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END LOOP;

  CLOSE cur;
END//
DELIMITER ;

-- Dumping structure for table dss_core.module
CREATE TABLE IF NOT EXISTS `module` (
  `parent` int(11) NOT NULL,
  `guid` varchar(48) NOT NULL,
  `cuid` varchar(48) NOT NULL COMMENT 'collision resistant unique identifier',
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(120) NOT NULL,
  `display_name` varchar(120) NOT NULL,
  `path` varchar(200) NOT NULL,
  `active` bit(1) NOT NULL DEFAULT b'1',
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `modified` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `storage_profile` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_directory_01` (`parent`,`name`),
  UNIQUE KEY `unq_module` (`parent`,`guid`),
  UNIQUE KEY `unq_module_0` (`cuid`),
  KEY `fk_module_profile` (`storage_profile`),
  CONSTRAINT `fk_direcory_client` FOREIGN KEY (`parent`) REFERENCES `client` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_module_profile` FOREIGN KEY (`storage_profile`) REFERENCES `profile` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB AUTO_INCREMENT=1966 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_core.profile
CREATE TABLE IF NOT EXISTS `profile` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(120) NOT NULL,
  `profile_mode` int(11) NOT NULL DEFAULT 0 COMMENT '0 - Direct Save\n1 - Stage and Move\n2 - Stage and Retain a copy',
  `storage_provider` int(10) unsigned DEFAULT NULL,
  `staging_provider` int(10) unsigned DEFAULT NULL,
  `config` text DEFAULT NULL COMMENT 'Storage & staging config.. Like region name, bucket name (if applicable) etc.',
  PRIMARY KEY (`id`),
  KEY `fk_profile_provider` (`storage_provider`),
  KEY `fk_profile_provider_0` (`staging_provider`),
  CONSTRAINT `fk_profile_provider` FOREIGN KEY (`storage_provider`) REFERENCES `provider` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_profile_provider_0` FOREIGN KEY (`staging_provider`) REFERENCES `provider` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_core.provider
CREATE TABLE IF NOT EXISTS `provider` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(120) DEFAULT NULL,
  `created` timestamp NOT NULL DEFAULT current_timestamp(),
  `description` text DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table dss_core.workspace
CREATE TABLE IF NOT EXISTS `workspace` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `cuid` varchar(48) NOT NULL COMMENT 'collision resistant unique identifier',
  `guid` varchar(48) NOT NULL,
  `parent` int(11) NOT NULL,
  `name` varchar(120) NOT NULL,
  `display_name` varchar(120) NOT NULL,
  `path` varchar(200) NOT NULL,
  `active` bit(1) NOT NULL DEFAULT b'1',
  `control_mode` int(11) NOT NULL DEFAULT 1 COMMENT '1 - numbers - Semi or Fully managed\n2 - hash - Semi or fully managed.',
  `parse_mode` int(11) NOT NULL DEFAULT 0 COMMENT '0- Parse //Semi Managed. You prepare the ID From outside.. Application will only prepare folders.\n1- Generate //Fully Managed, Application will generate the id as required.',
  `created` timestamp NOT NULL DEFAULT current_timestamp(),
  `modified` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_workspace` (`parent`,`name`),
  UNIQUE KEY `unq_workspace_0` (`parent`,`guid`),
  UNIQUE KEY `unq_workspace_1` (`cuid`),
  CONSTRAINT `fk_workspace_module` FOREIGN KEY (`parent`) REFERENCES `module` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `cns_workspace_0` CHECK (`parse_mode` >= 0 and `parse_mode` <= 1),
  CONSTRAINT `cns_workspace` CHECK (`control_mode` >= 1 and `control_mode` <= 2)
) ENGINE=InnoDB AUTO_INCREMENT=1923 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

/*!40103 SET TIME_ZONE=IFNULL(@OLD_TIME_ZONE, 'system') */;
/*!40101 SET SQL_MODE=IFNULL(@OLD_SQL_MODE, '') */;
/*!40014 SET FOREIGN_KEY_CHECKS=IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1) */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40111 SET SQL_NOTES=IFNULL(@OLD_SQL_NOTES, 1) */;
