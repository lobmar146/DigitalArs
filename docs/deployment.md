# DigitalArs: Vercel + Render + Azure SQL

Este despliegue usa el fork `lobmar146/DigitalArs`. Los cambios están en `deploy`.
No ejecutar los Seed locales en la nube: incluyen una contraseña de administrador pública.

## Azure SQL Database

1. Activar una suscripción Azure y crear una base mediante la **oferta gratuita** de Azure SQL.
2. Verificar **Free offer applied**, costo estimado mensual **0** y seleccionar **pausar al alcanzar el límite gratuito**, sin continuidad de pago.
3. Usar General Purpose serverless, sin redundancia geográfica de backups ni recursos adicionales de pago. Preferir una región cercana al backend.
4. Permitir en el firewall únicamente los rangos de salida que Render muestra en **Connect → Outbound** y temporalmente la IP de la máquina que inicializa la base. No abrir `0.0.0.0–255.255.255.255`.
5. Conectar con TLS: `Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;`.

Inicializar **una base vacía** desde la raíz del repositorio (.NET 10):

```powershell
# Cargar el secreto en la variable sin versionarlo ni pegarlo en el chat.
$secureConnection = Read-Host 'Cadena de conexión Azure SQL' -AsSecureString
$env:ConnectionStrings__DefaultConnection = [System.Net.NetworkCredential]::new('', $secureConnection).Password
dotnet run --project tools/DigitalArs.Database -- init
Remove-Item Env:ConnectionStrings__DefaultConnection
```

La herramienta ejecuta el esquema dentro de una transacción, rechaza bases con tablas existentes,
omite los DROP y USE de los scripts locales y crea únicamente roles y catálogos; no copia usuarios ni saldos.
`database/Cloud.sql` agrega las claves de Data Protection que deben sobrevivir a los reinicios.
Azure SQL cifra el almacenamiento; limitar el acceso a esa tabla a la identidad de la API y al administrador.
Para la API usar un usuario SQL con permisos de lectura/escritura de datos; el usuario de aprovisionamiento necesita DDL.

## Render

Importar `render.yaml` como Blueprint o crear manualmente un Web Service Docker:

| Campo | Valor |
|---|---|
| Rama | `deploy` |
| Root Directory | vacío |
| Dockerfile | `backend/DigitalArs.Api/Dockerfile` |
| Docker context | `.` |
| Compute | **Free ($0)** |
| Health check | `/health` |
| Puerto | `10000` |

Configurar estas variables en Render (las marcadas como secreto no van al repositorio):

| Variable | Valor |
|---|---|
| `ConnectionStrings__DefaultConnection` | Conexión Azure SQL (secreto) |
| `Jwt__Key` | Clave aleatoria de al menos 32 bytes (secreto; conservar entre despliegues) |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_HTTP_PORTS` | `10000` |
| `Hosting__BehindRenderProxy` | `true` |
| `DataProtection__PersistToDatabase` | `true` |
| `Cors__AllowedOrigins__0` | URL HTTPS estable de Vercel, sin barra final |
| `Email__FrontendBaseUrl` | Misma URL estable de Vercel |
| `Email__Host` | Host de un proveedor SMTP compatible con puerto 2525 |
| `Email__Port` | `2525` |
| `Email__EnableSsl` | `true` |
| `Email__User` | Usuario SMTP |
| `Email__Password` | Clave SMTP (secreto) |
| `Email__From` | Remitente verificado en el proveedor |

Render Free bloquea SMTP 25, 465 y 587. Brevo soporta 2525; configurar un remitente verificado antes
de probar invitaciones. El registro directo y login no requieren correo.
El middleware procesa un salto de proxy privado; comprobar HTTPS y rate limiting en la instancia desplegada.
El endpoint de salud es liveness: no consulta SQL, para evitar consumir la cuota de la base con cada sondeo.
El arranque prueba la conexión y permite varios intentos para despertar SQL serverless.
Si se importa el repositorio como URL pública sin integración Git, el despliegue manual se inicia desde Render.

## Vercel

| Campo | Valor |
|---|---|
| Repositorio | `lobmar146/DigitalArs` |
| Rama de producción | `deploy` |
| Root Directory | `frontend/DigitalArs` |
| Framework | Vite |
| Node | 24.x |
| Build | `npm run build` |
| Output | `dist` |
| `VITE_API_URL` | URL HTTPS real de Render, sin `/api` ni barra final |

La variable se incorpora al compilar: si cambia, volver a desplegar el frontend.
`vercel.json` permite recargar rutas como `/login` y `/movimientos`.
Usar una URL estable para invitaciones y CORS; las URLs aleatorias de previews no están permitidas automáticamente.

## Primer administrador

Registrar una cuenta propia desde la web con una contraseña nueva. Luego, desde la máquina de administración,
con `ConnectionStrings__DefaultConnection` cargada como arriba:

```powershell
dotnet run --project tools/DigitalArs.Database -- promote TU_EMAIL
```

La herramienta exige una cuenta activa con contraseña y se niega a crear un segundo administrador inicial.
Rota su security stamp; volver a iniciar sesión después. No hay endpoint de bootstrap público.

## Verificación

- `/health` responde 200 y una ruta privada sin JWT responde 401.
- Registro y login funcionan desde Vercel; solo el origen configurado recibe CORS.
- Un registro tiene perfil, rol y cuenta en cero en Azure SQL.
- Promover el primer administrador y comprobar su panel.
- Crear un usuario por invitación, recibir el correo y definir contraseña; repetir luego de reiniciar la API.
- Comprobar notificaciones SignalR y una operación entre cuentas de demo.
- Reabrir el sitio después de la suspensión del backend y SQL.

## Límites verificados el 29/09/2026

- [Vercel Hobby](https://vercel.com/docs/plans/hobby): uso personal y no comercial; adecuado para esta demo educativa.
- [Render Free](https://render.com/docs/free): suspensión tras 15 minutos inactivo, arranque cercano a un minuto,
  750 horas mensuales **compartidas por workspace**, disco efímero y límites de tráfico/builds.
  Revisar los otros servicios del workspace antes de asumir que todas las horas quedan para DigitalArs.
- [Azure SQL gratuito](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql):
  100.000 vCore-segundos, 32 GB de datos y 32 GB de backup al mes por base elegible.
- [Puertos Brevo](https://help.brevo.com/hc/en-us/articles/10905415650322-Which-SMTP-port-should-I-use-Port-587-465-or-2525).

Estos límites requieren las configuraciones indicadas; no equivalen a un SLA de producción ni a uso ilimitado.
