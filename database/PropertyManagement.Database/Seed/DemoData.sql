SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'PropertyManagementDemo'
    THROW 50010, 'Sample data may only be inserted into PropertyManagementDemo.', 1;

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM dbo.Properties) OR EXISTS (SELECT 1 FROM dbo.Tenants)
    BEGIN
        COMMIT TRANSACTION;
        PRINT 'Database already contains data. Sample seed skipped.';
        RETURN;
    END;

    DECLARE @Month DATE = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
    DECLARE @Today DATE = CONVERT(DATE, GETDATE());
    DECLARE @Properties TABLE ([Id] INT, [Name] NVARCHAR(120));
    DECLARE @Tenants TABLE ([Id] INT, [FullName] NVARCHAR(120));
    DECLARE @Leases TABLE ([Id] INT, [PropertyId] INT);

    INSERT INTO dbo.Properties ([Name], [Address], [City], [Bedrooms], [MonthlyRent])
    OUTPUT inserted.Id, inserted.Name INTO @Properties
    VALUES
        (N'Willow House', N'12 Example Avenue', N'Johannesburg', 3, 12500.00),
        (N'Parkside 08', N'8 Sample Lane', N'Johannesburg', 2, 9800.00),
        (N'The Courtyard', N'24 Demo Street', N'Pretoria', 3, 16000.00),
        (N'Oak Court 04', N'4 Fictional Road', N'Pretoria', 1, 8500.00),
        (N'Westwood Place', N'19 Example Drive', N'Johannesburg', 3, 14200.00),
        (N'Cedar Studio', N'3 Sample Crescent', N'Johannesburg', 1, 6500.00);

    INSERT INTO dbo.Tenants ([FullName], [Email], [Phone])
    OUTPUT inserted.Id, inserted.FullName INTO @Tenants
    VALUES
        (N'Amara Dlamini', N'amara@example.com', N'010 000 0001'),
        (N'Leo Jacobs', N'leo@example.com', N'010 000 0002'),
        (N'Priya Naidoo', N'priya@example.com', N'010 000 0003'),
        (N'Daniel Mokoena', N'daniel@example.com', N'010 000 0004'),
        (N'Sofia Adams', N'sofia@example.com', N'010 000 0005');

    INSERT INTO dbo.Leases ([PropertyId], [TenantId], [StartDate], [EndDate], [MonthlyRent])
    OUTPUT inserted.Id, inserted.PropertyId INTO @Leases
    SELECT property.Id, tenant.Id, DATEADD(MONTH, mapping.OffsetMonths, @Month),
        DATEADD(DAY, -1, DATEADD(MONTH, mapping.OffsetMonths + 12, @Month)), mapping.Rent
    FROM (VALUES
        (N'Willow House', N'Amara Dlamini', -3, 12500.00),
        (N'Parkside 08', N'Leo Jacobs', -2, 9800.00),
        (N'The Courtyard', N'Priya Naidoo', -1, 16000.00),
        (N'Westwood Place', N'Daniel Mokoena', -2, 14200.00),
        (N'Oak Court 04', N'Sofia Adams', 1, 8500.00)
    ) AS mapping(PropertyName, TenantName, OffsetMonths, Rent)
    INNER JOIN @Properties AS property ON property.Name = mapping.PropertyName
    INNER JOIN @Tenants AS tenant ON tenant.FullName = mapping.TenantName;

    DECLARE @LeaseId INT;
    SELECT @LeaseId = lease.Id FROM @Leases AS lease INNER JOIN @Properties AS property ON property.Id = lease.PropertyId WHERE property.Name = N'Willow House';
    EXEC dbo.usp_RecordRentPayment @LeaseId, @Month, @Month, 12500.00, N'DEMO-WILLOW', 'b9467301-8700-4a47-9d8c-88344ea695e1';
    SELECT @LeaseId = lease.Id FROM @Leases AS lease INNER JOIN @Properties AS property ON property.Id = lease.PropertyId WHERE property.Name = N'Parkside 08';
    EXEC dbo.usp_RecordRentPayment @LeaseId, @Month, @Today, 6500.00, N'DEMO-PARKSIDE', 'b9467301-8700-4a47-9d8c-88344ea695e2';
    SELECT @LeaseId = lease.Id FROM @Leases AS lease INNER JOIN @Properties AS property ON property.Id = lease.PropertyId WHERE property.Name = N'The Courtyard';
    EXEC dbo.usp_RecordRentPayment @LeaseId, @Month, @Month, 16000.00, N'DEMO-COURTYARD', 'b9467301-8700-4a47-9d8c-88344ea695e3';

    COMMIT TRANSACTION;
    PRINT 'Fictional sample portfolio created.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;