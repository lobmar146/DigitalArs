-- Se ejecuta conectado a la base destino. No contiene usuarios ni contraseñas.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.DataProtectionKeys', N'U') IS NULL
    CREATE TABLE dbo.DataProtectionKeys (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FriendlyName NVARCHAR(MAX) NULL,
        Xml NVARCHAR(MAX) NULL
    );
GO
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = N'ADMINISTRADOR')
    INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (CONVERT(NVARCHAR(450), NEWID()), N'Administrador', N'ADMINISTRADOR', CONVERT(NVARCHAR(MAX), NEWID()));
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = N'USUARIO')
    INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (CONVERT(NVARCHAR(450), NEWID()), N'Usuario', N'USUARIO', CONVERT(NVARCHAR(MAX), NEWID()));
GO
