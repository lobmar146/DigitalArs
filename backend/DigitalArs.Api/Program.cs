/* =============================================================================
Índice de secciones:
   1. Arranque y cadena de conexión          7. Autenticación con JWT
   2. Acceso a datos                          8. Autorización
   3. Identity                                9. Límite de solicitudes
   4. Servicios propios                      10. Controllers, CORS y Swagger
   5. Manejo global de errores               11. Verificaciones de arranque
   6. Opciones del JWT                       12. El pipeline
=============================================================================*/

using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using DigitalArs.Api.Data.Context;
using DigitalArs.Api.Errors;
using DigitalArs.Api.Helpers.Configuration;
using DigitalArs.Api.Hubs;
using DigitalArs.Api.Interfaces;
using DigitalArs.Api.OpenApi;
using DigitalArs.Api.Repositories;
using DigitalArs.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// -----------------------------------------------------------------------------
// 1. Arranque y cadena de conexión
// -----------------------------------------------------------------------------
// CreateBuilder prepara el host y carga la configuración de varias fuentes a la
// vez: appsettings.json, los user-secrets y las variables de entorno, en ese
// orden de prioridad creciente. La conexión no está en ningún appsettings: llega
// como variable de entorno ConnectionStrings__DefaultConnection, que en local
// define Properties/launchSettings.json y en un servidor define el entorno.

var builder = WebApplication.CreateBuilder(args);
// Render termina TLS en su proxy. Solo confiamos en la red privada del proxy,
// y procesamos un salto; nunca aceptamos encabezados de cualquier IP pública.
if (builder.Configuration.GetValue<bool>("Hosting:BehindRenderProxy"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
    });
}
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Cortar acá y no en la primera consulta: un error de arranque con instrucciones
// es mucho más fácil de resolver que un 500 a mitad de camino.
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Falta la cadena de conexión. Copiá Properties/launchSettings.Example.json a " +
        "Properties/launchSettings.json y completá ConnectionStrings__DefaultConnection " +
        "con tu instancia de SQL Server. Ver README.md del backend.");
}

// -----------------------------------------------------------------------------
// 2. Acceso a datos: conexión, contextos y repositorios
// -----------------------------------------------------------------------------
// "Scoped" quiere decir una instancia por petición HTTP, compartida por todo lo
// que se construya durante esa petición.
//
// Los dos DbContext reciben deliberadamente la MISMA SqlConnection scoped. Eso es
// lo que le permite a AccountService abrir una transacción que abarque a la vez
// las tablas de Identity y las de negocio: si el alta del perfil falla, tampoco
// queda creada la cuenta de acceso.

builder.Services.AddScoped(_ => new SqlConnection(connectionString));
builder.Services.AddDbContext<DigitalArsDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<SqlConnection>()));
builder.Services.AddDbContext<AuthDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<SqlConnection>()));

// Un repositorio por recurso. Los controllers y servicios dependen de la interfaz.
builder.Services.AddScoped<ITipoMovimientoRepository, TipoMovimientoRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<ICuentaRepository, CuentaRepository>();
builder.Services.AddScoped<IMovimientoRepository, MovimientoRepository>();
builder.Services.AddScoped<INotificacionRepository, NotificacionRepository>();

// -----------------------------------------------------------------------------
// 3. Identity: cuentas, contraseñas, roles e invitaciones
// -----------------------------------------------------------------------------
// Identity administra las siete tablas AspNet*: guarda el hash de la contraseña
// (nunca el texto), los roles y el security stamp. AddIdentityCore es la variante
// sin cookies ni pantallas propias, que es la que corresponde en una API que se
// autentica con JWT.

builder.Services.AddIdentityCore<IdentityUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    // A los 5 intentos fallidos la cuenta queda bloqueada 15 minutos.
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<AuthDbContext>().AddDefaultTokenProviders();

// AddDefaultTokenProviders habilita los tokens con los que se arma la invitación
// de primera contraseña; esta línea les fija cuánto duran. El plazo tiene que
// coincidir con Invitacion.ExpiraEnSegundos, que es el que se le informa al
// frontend: un día.
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromDays(1));

if (builder.Configuration.GetValue<bool>("DataProtection:PersistToDatabase"))
{
    builder.Services.AddDbContext<DataProtectionDbContext>(options => options.UseSqlServer(connectionString));
    builder.Services.AddDataProtection().SetApplicationName("DigitalArs")
        .PersistKeysToDbContext<DataProtectionDbContext>();
}

// -----------------------------------------------------------------------------
// 4. Servicios propios
// -----------------------------------------------------------------------------
// Se registran por su interfaz, así quien los recibe depende del contrato y no de
// la clase concreta. Eso es lo que permite probar las reglas de negocio sin tener
// SQL Server levantado, como hace Tests/AuthenticationChecks.

builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<ICuentaService, CuentaService>();
builder.Services.AddScoped<IDepositoService, DepositoService>();
builder.Services.AddScoped<ITransferenciaService, TransferenciaService>();
builder.Services.AddScoped<IHistorialService, HistorialService>();
builder.Services.AddScoped<ITarjetaService, TarjetaService>();
builder.Services.AddScoped<INotificacionService, NotificacionService>();

// TarjetaService lleva en memoria los intentos fallidos de contraseña sobre el revelado del
// código de seguridad. Es estado efímero y por eso no va a la base: se reinicia solo con el
// tiempo. Con varias instancias de la API cada una llevaría su propia cuenta, lo cual es
// aceptable acá porque corre una sola.
builder.Services.AddMemoryCache();

// SignalR mantiene abierta una conexión con cada usuario para avisarle de sus movimientos en el
// momento. Viene incluido en ASP.NET Core: no hace falta instalar ningún paquete.
// Los servicios de negocio no lo usan directamente, sino a través de INotificadorEnTiempoReal.
builder.Services.AddSignalR();
builder.Services.AddScoped<INotificadorEnTiempoReal, NotificadorSignalR>();

// La invitación de primera contraseña llega por correo: el token nunca pasa por el
// administrador. La sección "Email" dice a qué SMTP conectarse; sin ella se usan los
// valores de smtp4dev (localhost:2525), el SMTP de desarrollo del README.
builder.Services.AddOptions<EmailOptions>().BindConfiguration("Email").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddScoped<IEnviadorDeInvitaciones, SmtpEnviadorDeInvitaciones>();

// -----------------------------------------------------------------------------
// 5. Manejo global de errores
// -----------------------------------------------------------------------------
// Cualquier excepción que no atrape un controller termina acá: se registra en el
// log y sale como un 500 con la misma forma de ErrorResponse que el resto, sin
// filtrarle al cliente el detalle de la excepción.

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
// UseExceptionHandler() sin argumentos no arranca si no hay un servicio de respaldo registrado.
// GlobalExceptionHandler escribe la respuesta y devuelve true, así que este respaldo no llega a
// usarse nunca: está para cumplir el requisito del middleware.
builder.Services.AddProblemDetails();

// -----------------------------------------------------------------------------
// 6. Opciones del JWT, validadas al arrancar
// -----------------------------------------------------------------------------
// BindConfiguration lee la sección "Jwt" y la vuelca en JwtOptions. ValidateOnStart
// hace que una clave ausente o de menos de 32 bytes corte el arranque, en vez de
// fallar recién cuando alguien intenta iniciar sesión.

builder.Services.AddOptions<JwtOptions>().BindConfiguration("Jwt").ValidateDataAnnotations()
    .Validate(o => Encoding.UTF8.GetByteCount(o.Key) >= 32, "Jwt:Key debe tener al menos 32 bytes.")
    .ValidateOnStart();

// Copia suelta de las mismas opciones, porque la configuración de abajo se arma
// ahora y no puede esperar a que el contenedor construya IOptions<JwtOptions>.
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// -----------------------------------------------------------------------------
// 7. Autenticación con JWT
// -----------------------------------------------------------------------------

// El token se firma una sola vez, pero el usuario puede desactivarse o perder la sesión
// después. Por eso cada request vuelve a contrastar el token contra el estado real en la base.
async Task<bool> ElTokenSigueSiendoValido(TokenValidatedContext context)
{
    var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<IdentityUser>>();
    var db = context.HttpContext.RequestServices.GetRequiredService<DigitalArsDbContext>();

    var identityUserId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    if (identityUserId is null) return false;

    var user = await users.FindByIdAsync(identityUserId);
    if (user is null) return false;

    // Sin contraseña definida, el usuario todavía no consumió su invitación.
    if (!await users.HasPasswordAsync(user)) return false;

    // El stamp se renueva cuando se revoca la sesión: ahí un token viejo deja de coincidir.
    var stampDelToken = context.Principal?.FindFirstValue("security_stamp");
    if (stampDelToken != await users.GetSecurityStampAsync(user)) return false;

    // Desactivar a alguien rota su stamp, pero si la baja se hace directo en la base eso no
    // pasa: por eso el estado se vuelve a mirar acá, en cada petición.
    return await db.Usuarios.AnyAsync(u => u.identity_user_id == identityUserId && u.is_active);
}

// TokenValidationParameters es lo que se comprueba del token en sí: que lo haya
// emitido esta API, que sea para este frontend, que la firma cierre con la clave
// y que no esté vencido. ClockSkew en cero saca la tolerancia de 5 minutos que
// .NET aplica por defecto al vencimiento.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateLifetime = true, RequireExpirationTime = true,
        ValidateIssuerSigningKey = true,
        // El texto de relleno nunca se usa: si la clave falta, el arranque ya cortó
        // en la sección 6. Está solo para que esta línea compile y no explote antes.
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            string.IsNullOrEmpty(jwt.Key) ? "missing-key-validation-will-fail-on-start" : jwt.Key)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.Zero
    };
    // Con el token ya validado criptográficamente, recién acá se lo contrasta
    // contra la base. context.Fail() convierte la petición en un 401.
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!await ElTokenSigueSiendoValido(context))
                context.Fail("Token inválido o usuario desactivado.");
        },

        // El navegador no deja mandar headers propios al abrir un WebSocket, así que el cliente
        // de SignalR manda el JWT en la query string (?access_token=...). Acá se lo levanta de
        // ahí y se lo entrega al resto de la validación, que sigue siendo exactamente la misma:
        // este evento no valida nada, solo dice DÓNDE está el token.
        //
        // Se acepta únicamente en la ruta del hub. Si se aceptara en cualquier ruta, cualquier
        // endpoint podría autenticarse con un token pegado en la URL, y las URLs quedan
        // guardadas en el historial del navegador y en los logs de acceso del servidor.
        //
        // OJO al editar: este evento va DENTRO de este mismo objeto, al lado de
        // OnTokenValidated. Escribir un segundo "options.Events = new JwtBearerEvents {...}"
        // reemplaza a este y se pierde la comprobación de usuario desactivado en TODA la API.
        OnMessageReceived = context =>
        {
            var esLaRutaDelHub = context.HttpContext.Request.Path
                .StartsWithSegments("/hubs/notificaciones");

            if (!esLaRutaDelHub)
                return Task.CompletedTask;

            var tokenDeLaQuery = context.Request.Query["access_token"].ToString();

            if (!string.IsNullOrEmpty(tokenDeLaQuery))
                context.Token = tokenDeLaQuery;

            return Task.CompletedTask;
        }
    };
});

// -----------------------------------------------------------------------------
// 8. Autorización: todo protegido salvo que se diga lo contrario
// -----------------------------------------------------------------------------
// La FallbackPolicy se aplica a cualquier endpoint que no declare su propia regla.
// Es una decisión de seguridad: olvidarse un [Authorize] deja el endpoint
// protegido, no abierto. Lo que va a ser público tiene que pedirlo explícitamente
// con [AllowAnonymous].

builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// -----------------------------------------------------------------------------
// 9. Límite de solicitudes
// -----------------------------------------------------------------------------
// La política "auth" NO es global: solo corre donde un controller la pide con
// [EnableRateLimiting("auth")], hoy AuthController. Cuenta por dirección IP en
// ventanas de un minuto, y al excederlas responde 429 sin encolar nada.

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// -----------------------------------------------------------------------------
// 10. Controllers, CORS y Swagger
// -----------------------------------------------------------------------------

const string MensajeDeDatosInvalidos = "Revisá los datos ingresados.";

// Sin esta fábrica, un DTO inválido devuelve el ValidationProblemDetails que arma
// [ApiController] por su cuenta, con otra forma que el ErrorResponse que usan el resto de
// los endpoints y el GlobalExceptionHandler. Acá se unifica: un solo contrato de error.
BadRequestObjectResult ErrorDeValidacion(ActionContext context)
{
    // Las claves con $ son rutas del JSON y solo aparecen cuando el cuerpo no se pudo
    // deserializar. Su texto nombra los tipos internos del DTO, así que no se publica:
    // si el cuerpo no se entiende, no hay nada puntual que contarle a quien llama.
    var cuerpoIlegible = context.ModelState.Keys.Any(clave => clave.StartsWith('$'));

    if (cuerpoIlegible)
    {
        return new BadRequestObjectResult(new ErrorResponse
        {
            Code = "VALIDATION_ERROR",
            Message = MensajeDeDatosInvalidos
        });
    }

    var errores = context.ModelState
        .SelectMany(entrada => entrada.Value?.Errors ?? [])
        .Select(error => error.ErrorMessage)
        .Where(mensaje => !string.IsNullOrWhiteSpace(mensaje))
        .ToArray();

    return new BadRequestObjectResult(new ErrorResponse
    {
        Code = "VALIDATION_ERROR",
        Message = errores.FirstOrDefault() ?? MensajeDeDatosInvalidos,
        Errors = errores
    });
}

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = ErrorDeValidacion);

// El frontend de Vite corre en otro puerto, así que el navegador exige CORS explícito.
// En producción los orígenes se declaran en Cors:AllowedOrigins; no hay valor por defecto.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length == 0 && builder.Environment.IsDevelopment())
{
    allowedOrigins = ["http://localhost:5173"];
}
// AllowCredentials es obligatorio para SignalR: el navegador lo exige para abrir el WebSocket.
// Se puede usar porque los orígenes están enumerados uno por uno con WithOrigins. Es
// incompatible con AllowAnyOrigin, así que si alguna vez se cambia a "cualquier origen", el
// hub deja de conectar.
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// CORS es una política del navegador: evita que otro sitio llame a esta API desde
// la pestaña de un usuario. No reemplaza a la autenticación ni frena a un cliente
// HTTP como curl o Postman.

// El transformer le agrega a Swagger el candado para pegar el Bearer. Solo documenta
// cómo mandar el token: no valida nada.
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

// =============================================================================
// A partir de acá la aplicación ya está construida: se termina de verificar el
// entorno y se arma el recorrido de las peticiones.
// =============================================================================

var app = builder.Build();

// -----------------------------------------------------------------------------
// 11. Verificaciones de arranque
// -----------------------------------------------------------------------------
// Las dos comprobaciones siguientes existen para fallar temprano y con un motivo
// claro, en vez de dejar la API en pie y que el primer usuario se coma el error.

// Pedir el valor fuerza la validación de JwtOptions declarada en la sección 6.
_ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtOptions>>().Value;

// Un DbContext es scoped y acá todavía no hay ninguna petición, así que hay que
// abrir un scope a mano para poder pedirlo.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DigitalArsDbContext>();
    // Una base serverless pausada puede rechazar el primer intento al despertar.
    var connected = false;
    for (var attempt = 0; attempt < 6; attempt++)
    {
        if (await db.Database.CanConnectAsync()) { connected = true; break; }
        if (attempt < 5) await Task.Delay(TimeSpan.FromSeconds(10));
    }
    if (!connected)
        throw new InvalidOperationException("No fue posible conectar con SQL Server. Revisá DefaultConnection y el servicio SQL Server.");
}

// Swagger solo en Development: en producción no se publica la documentación.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "DigitalArs.Api v1"));
}

// -----------------------------------------------------------------------------
// 12. El pipeline: por dónde pasa cada petición
// -----------------------------------------------------------------------------
// Acá el orden es lo único que importa. Cada middleware recibe la petición, puede
// cortarla o dejarla seguir hacia el siguiente, y después ve pasar la respuesta de
// vuelta. Se recorren de arriba hacia abajo, y la respuesta vuelve en sentido
// inverso. Mover una línea de lugar cambia el comportamiento.

app.UseExceptionHandler();   // primero de todo: así envuelve lo que falle más adentro
app.UseForwardedHeaders();  // restaura HTTPS y la IP antes de redirección y rate limiting
app.UseHttpsRedirection();   // manda a HTTPS antes de procesar nada
app.UseCors("Frontend");     // antes de autenticar: el preflight OPTIONS viaja sin token
app.UseAuthentication();     // ¿quién sos? lee el Bearer y arma la identidad
app.UseAuthorization();      // ¿podés? aplica [Authorize] y la FallbackPolicy
app.UseRateLimiter();        // aplica la política "auth" donde el controller la pide
app.MapControllers();        // los endpoints REST
// Liveness sin consultar SQL: los chequeos del hosting no mantienen la base despierta.
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapHub<NotificacionesHub>("/hubs/notificaciones"); // el WebSocket de las notificaciones

// Nota sobre el orden: el rate limiter quedó después de la autorización, así que un
// 401 o un 403 se responde sin consumir cuota. Para el login, que es [AllowAnonymous]
// y es lo que realmente interesa limitar, da igual.

app.Run();
