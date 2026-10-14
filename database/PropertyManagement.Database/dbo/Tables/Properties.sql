CREATE TABLE [dbo].[Properties]
(
    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Properties] PRIMARY KEY,
    [Name] NVARCHAR(120) NOT NULL,
    [Address] NVARCHAR(240) NOT NULL,
    [City] NVARCHAR(100) NOT NULL,
    [Bedrooms] INT NOT NULL,
    [MonthlyRent] DECIMAL(18,2) NOT NULL,
    [Version] ROWVERSION NOT NULL,
    CONSTRAINT [CK_Properties_Bedrooms] CHECK ([Bedrooms] >= 0 AND [Bedrooms] <= 50),
    CONSTRAINT [CK_Properties_MonthlyRent] CHECK ([MonthlyRent] > 0),
    CONSTRAINT [CK_Properties_Name] CHECK (LEN(LTRIM(RTRIM([Name]))) > 0)
);