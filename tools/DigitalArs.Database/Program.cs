using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Uso: init | promote email. La conexión se recibe exclusivamente del entorno.
if (args.Length == 0 || (args[0] != "init" && args[0] != "promote") ||
    (args[0] == "promote" && args.Length != 2))
{
    Console.Error.WriteLine("Uso: init | promote email");
    return 1;
}
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Falta ConnectionStrings__DefaultConnection.");
    return 1;
}
await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
try
{
    // Una sola ejecución de aprovisionamiento por base a la vez.
    await Execute("DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource='DigitalArs-bootstrap', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; IF @r < 0 THROW 50000, 'Otro aprovisionamiento está activo.', 1;");
    if (args[0] == "init")
    {
        await Execute("IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped=0) THROW 50000, 'La base debe estar vacía. No se modificó ningún dato.', 1;");
        string[] scripts = ["Create(v.002).sql", "Identity(v.001).sql", "Create(v.003).sql",
            "Create(v.004).sql", "Notificaciones(v.001).sql", "Notificaciones(v.002).sql", "Cloud.sql"];
        await Execute("SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET XACT_ABORT ON;");
        foreach (var script in scripts)
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", script));
            // Estos scripts locales usan USE y el primero reinicia tablas. En nube
            // la base ya existe y SOLO se admite inicializar una base vacía.
            sql = Regex.Replace(sql, @"^\s*USE\s+DigitalArs\s*;[^\r\n]*", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            sql = Regex.Replace(sql, @"^\s*DROP TABLE IF EXISTS dbo\.\w+;[^\r\n]*", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) await Execute(batch);
            Console.WriteLine($"Preparado: {script}");
        }
    }
    else
    {
        // Promoción local y explícita: nunca hay una ruta HTTP de bootstrap pública.
        await using var command = new SqlCommand("""
            DECLARE @user nvarchar(450) = (SELECT Id FROM dbo.AspNetUsers
                WHERE NormalizedEmail=UPPER(@email) AND PasswordHash IS NOT NULL);
            IF @user IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.Usuarios WHERE identity_user_id=@user AND is_active=1)
                THROW 50000, 'Registrá primero la cuenta desde el frontend.', 1;
            DECLARE @role nvarchar(450) = (SELECT Id FROM dbo.AspNetRoles WHERE NormalizedName=N'ADMINISTRADOR');
            IF @role IS NULL THROW 50000, 'Falta el rol Administrador.', 1;
            IF EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE RoleId=@role AND UserId<>@user)
                THROW 50000, 'Ya existe un administrador. Usá el panel de administración.', 1;
            DELETE ur FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId
                WHERE ur.UserId=@user AND r.NormalizedName=N'USUARIO';
            IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId=@user AND RoleId=@role)
                INSERT dbo.AspNetUserRoles(UserId,RoleId) VALUES(@user,@role);
            UPDATE dbo.AspNetUsers SET SecurityStamp=CONVERT(nvarchar(36),NEWID()) WHERE Id=@user;
            """, connection, transaction);
        command.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = args[1];
        await command.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync();
    Console.WriteLine("Completado. Si promoviste una cuenta, volvé a iniciar sesión.");
    return 0;
}
catch (Exception)
{
    try { await transaction.RollbackAsync(); } catch (InvalidOperationException) { }
    // No imprimir conexión ni comandos que puedan contener información privada.
    Console.Error.WriteLine("Falló el aprovisionamiento y se revirtió la transacción. Revisá conectividad, permisos y que la base esté vacía (init) o la cuenta exista (promote).");
    return 1;
}

async Task Execute(string sql)
{
    await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
    await command.ExecuteNonQueryAsync();
}
