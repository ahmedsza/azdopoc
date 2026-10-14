CREATE TABLE [dbo].[Leases]
(
    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Leases] PRIMARY KEY,
    [PropertyId] INT NOT NULL,
    [TenantId] INT NOT NULL,
    [StartDate] DATE NOT NULL,
    [EndDate] DATE NOT NULL,
    [MonthlyRent] DECIMAL(18,2) NOT NULL,
    [Version] ROWVERSION NOT NULL,
    CONSTRAINT [FK_Leases_Properties] FOREIGN KEY ([PropertyId]) REFERENCES [dbo].[Properties] ([Id]),
    CONSTRAINT [FK_Leases_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]),
    CONSTRAINT [CK_Leases_Dates] CHECK ([EndDate] >= [StartDate]),
    CONSTRAINT [CK_Leases_MonthlyRent] CHECK ([MonthlyRent] > 0)
);
GO
CREATE INDEX [IX_Leases_PropertyId_StartDate] ON [dbo].[Leases] ([PropertyId], [StartDate]) INCLUDE ([EndDate]);
GO
CREATE INDEX [IX_Leases_TenantId] ON [dbo].[Leases] ([TenantId]);