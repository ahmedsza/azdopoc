CREATE PROCEDURE [dbo].[usp_GetRentLedger]
    @RentalMonth DATE
AS
BEGIN
    SET NOCOUNT ON;

    IF @RentalMonth IS NULL OR DAY(@RentalMonth) <> 1
        THROW 50001, 'Choose a valid rental month.', 1;

    SELECT lease.[Id] AS [LeaseId], property.[Name] AS [PropertyName], tenant.[FullName] AS [TenantName],
        lease.[MonthlyRent], COALESCE(payment.[Paid], 0) AS [Paid],
        CASE WHEN lease.[MonthlyRent] > COALESCE(payment.[Paid], 0)
             THEN lease.[MonthlyRent] - COALESCE(payment.[Paid], 0) ELSE 0 END AS [Outstanding]
    FROM [dbo].[Leases] AS lease
    INNER JOIN [dbo].[Properties] AS property ON property.[Id] = lease.[PropertyId]
    INNER JOIN [dbo].[Tenants] AS tenant ON tenant.[Id] = lease.[TenantId]
    OUTER APPLY (
        SELECT SUM(rent.[Amount]) AS [Paid]
        FROM [dbo].[RentPayments] AS rent
        WHERE rent.[LeaseId] = lease.[Id] AND rent.[RentalMonth] = @RentalMonth
    ) AS payment
    WHERE lease.[StartDate] <= EOMONTH(@RentalMonth) AND lease.[EndDate] >= @RentalMonth
    ORDER BY property.[Name], tenant.[FullName], lease.[Id];
END;