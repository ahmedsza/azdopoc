CREATE PROCEDURE [dbo].[usp_RecordRentPayment]
    @LeaseId INT,
    @RentalMonth DATE,
    @PaidOn DATE,
    @Amount DECIMAL(18,2),
    @Reference NVARCHAR(100),
    @SubmissionKey UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Amount IS NULL OR @Amount <= 0.00 OR @RentalMonth IS NULL OR DAY(@RentalMonth) <> 1
        THROW 50001, 'Enter a positive amount and a valid rental month.', 1;
    IF @PaidOn IS NULL OR @PaidOn > CONVERT(DATE, GETDATE()) OR @Reference IS NULL
        THROW 50002, 'Enter a valid payment date and reference.', 1;
    IF @SubmissionKey IS NULL
        THROW 50003, 'The payment submission identifier is missing.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (
            SELECT 1 FROM [dbo].[Leases] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Id] = @LeaseId AND [StartDate] <= EOMONTH(@RentalMonth) AND [EndDate] >= @RentalMonth
        )
            THROW 50004, 'The lease does not cover this rental month.', 1;

        DECLARE @ExistingId INT;
        SELECT @ExistingId = [Id]
        FROM [dbo].[RentPayments] WITH (UPDLOCK, HOLDLOCK)
        WHERE [SubmissionKey] = @SubmissionKey;

        IF @ExistingId IS NOT NULL
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM [dbo].[RentPayments]
                WHERE [Id] = @ExistingId AND [LeaseId] = @LeaseId AND [RentalMonth] = @RentalMonth
                  AND [PaidOn] = @PaidOn AND [Amount] = @Amount AND [Reference] = @Reference
            )
                THROW 50005, 'This submission identifier was already used for a different payment.', 1;
            COMMIT TRANSACTION;
            SELECT @ExistingId AS [Id];
            RETURN;
        END;

        INSERT INTO [dbo].[RentPayments] ([LeaseId], [RentalMonth], [PaidOn], [Amount], [Reference], [SubmissionKey])
        VALUES (@LeaseId, @RentalMonth, @PaidOn, @Amount, @Reference, @SubmissionKey);

        DECLARE @PaymentId INT = CONVERT(INT, SCOPE_IDENTITY());
        COMMIT TRANSACTION;
        SELECT @PaymentId AS [Id];
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;