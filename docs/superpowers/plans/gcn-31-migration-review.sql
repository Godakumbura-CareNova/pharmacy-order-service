START TRANSACTION;

DROP TEMPORARY TABLE IF EXISTS GCN31_existing_orders_need_owner_mapping;
CREATE TEMPORARY TABLE GCN31_existing_orders_need_owner_mapping (
    GuardId INT NOT NULL PRIMARY KEY
);
INSERT INTO GCN31_existing_orders_need_owner_mapping VALUES (1);
INSERT INTO GCN31_existing_orders_need_owner_mapping SELECT 1 FROM Orders LIMIT 1;
DROP TEMPORARY TABLE GCN31_existing_orders_need_owner_mapping;

ALTER TABLE `Orders` MODIFY COLUMN `CustomerId` char(36) COLLATE ascii_general_ci NOT NULL;

ALTER TABLE `Orders` ADD `RejectionReason` varchar(1000) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Orders` ADD `RequestFingerprint` varchar(64) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `Orders` ADD `RequestId` char(36) COLLATE ascii_general_ci NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

ALTER TABLE `OrderItems` ADD `MedicineName` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `OrderItems` ADD `PrescriptionId` int NULL;

CREATE TABLE `OrderStockAllocations` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OrderItemId` int NOT NULL,
    `StockId` int NOT NULL,
    `Quantity` int NOT NULL,
    CONSTRAINT `PK_OrderStockAllocations` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_OrderStockAllocations_OrderItems_OrderItemId` FOREIGN KEY (`OrderItemId`) REFERENCES `OrderItems` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_OrderStockAllocations_Stocks_StockId` FOREIGN KEY (`StockId`) REFERENCES `Stocks` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE UNIQUE INDEX `IX_Orders_CustomerId_RequestId` ON `Orders` (`CustomerId`, `RequestId`);

CREATE INDEX `IX_OrderItems_PrescriptionId` ON `OrderItems` (`PrescriptionId`);

CREATE INDEX `IX_OrderStockAllocations_OrderItemId` ON `OrderStockAllocations` (`OrderItemId`);

CREATE INDEX `IX_OrderStockAllocations_StockId` ON `OrderStockAllocations` (`StockId`);

ALTER TABLE `OrderItems` ADD CONSTRAINT `FK_OrderItems_Prescriptions_PrescriptionId` FOREIGN KEY (`PrescriptionId`) REFERENCES `Prescriptions` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260913140700_AddPickupCheckout', '8.0.18');

COMMIT;
