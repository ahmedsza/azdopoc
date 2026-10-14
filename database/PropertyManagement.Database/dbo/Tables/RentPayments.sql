CREATE TABLE [dbo].[RentPayments]
(
    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_RentPayments] PRIMARY KEY,
    [LeaseId] INT NOT NULL,
    [RentalMonth] DATE NOT NULL,
    [PaidOn] DATE NOT NULL,
    [Amount] DECIMAL(18,2) NOT NULL,
    [Reference] NVARCHAR(100) NOT NULL,
    [SubmissionKey] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [FK_RentPayments_Leases] FOREIGN KEY ([LeaseId]) REFERENCES [dbo].[Leases] ([Id]),
    CONSTRAINT [UQ_RentPayments_SubmissionKey] UNIQUE ([SubmissionKey]),
    CONSTRAINT [CK_RentPayments_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_RentPayments_RentalMonth] CHECK (DATEPART(DAY, [RentalMonth]) = 1)
);
GO
CREATE INDEX [IX_RentPayments_LeaseId_RentalMonth] ON [dbo].[RentPayments] ([LeaseId], [RentalMonth]) INCLUDE ([Amount]);