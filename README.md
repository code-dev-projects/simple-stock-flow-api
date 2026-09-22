# simple-stock-flow-api

Backend del sistema de **Productos y Ventas**: .NET 8, arquitectura hexagonal. Expone la interfaz
de programación, guarda las reglas de negocio y **es dueño del esquema de la base**, que aplica con
sus propias migraciones al arrancar.

De lo que no se ocupa: de pintar pantallas —eso es de `simple-stock-flow-portal`— ni de orquestar
el despliegue, que vive en `simple-stock-flow-infra`.


## 1. Qué es esto

El backend REST del sistema de Productos y Ventas: catálogo, registro de ventas y reporte por
rango de fechas, sobre .NET 8 + PostgreSQL con arquitectura hexagonal. **Es dueño del esquema
de la base**: las migraciones viven aquí y se aplican al arrancar.

De lo que **no** se ocupa: no sirve la interfaz de usuario (eso es `simple-stock-flow-portal`), no
define el `docker compose` ni el `.env` del sistema (eso es `simple-stock-flow-infra`) y no crea la
base de datos —eso lo hace el contenedor de Postgres; este servicio solo crea el esquema
dentro de ella—.

> **Antes de seguir, lo que un evaluador necesita saber:** el sistema **se usa de punta a punta**
> —entrar, ver el catálogo con sus imágenes, crear un producto, venderlo, ver bajar el stock y
> sacar el reporte por rango—. Comprobado contra el sistema levantado el **2026-09-21**. Esta
> misma advertencia declaró lo contrario durante días: el registro de qué decía y qué se midió
> está en la §5.

---

## Cómo se clona

El sistema vive en repositorios separados y **el compose construye desde las carpetas hermanas**,
así que la disposición no es cosmética: hay que clonarlos en el mismo directorio y con su nombre.

```bash
mkdir simple-stock-flow && cd simple-stock-flow
git clone https://github.com/code-dev-projects/simple-stock-flow-infra.git
git clone https://github.com/code-dev-projects/simple-stock-flow-api.git
git clone https://github.com/code-dev-projects/simple-stock-flow-portal.git
```

```
simple-stock-flow/
├── simple-stock-flow-infra/     compose, .env.example y verify.sh
├── simple-stock-flow-api/       backend .NET 8
└── simple-stock-flow-portal/    front Angular 20
```

`simple-stock-flow-docs` no hace falta para levantar el sistema: guarda el material de trabajo.

## 2. Cómo se levanta

Hay dos caminos. El primero es el del sistema completo; el segundo es para trabajar solo sobre
la API.

### A) El sistema completo (API + Postgres + portal)

Necesita **Docker**. El `docker compose` no está en este repositorio, está en
`simple-stock-flow-infra`, que es un repo hermano dentro del mismo workspace:

```bash
cd ../simple-stock-flow-infra
cp .env.example .env          # rellena POSTGRES_PASSWORD, JWT_SIGNING_KEY y ADMIN_PASSWORD
docker compose up -d
docker compose ps             # los tres contenedores deben quedar (healthy)
```

Queda publicado un único puerto al host: **`http://localhost:8080`** (valor de `PORTAL_PORT`).
Ahí responde el portal, y su nginx hace de proxy hacia la API bajo `/api/` y `/media/`. El
contenedor de la API **no publica puerto propio**, y `/health` no está proxiado: para verlo hay
que entrar por dentro de la red de Docker.

```bash
docker compose exec db wget -q -O - http://service:8080/health
# {"status":"ok"}
```

### B) Solo este servicio, en local

Necesita el **SDK de .NET** (los proyectos apuntan a `net8.0`; se verificó compilando y
ejecutando con el SDK 10.0.401) y un Postgres accesible. Las dos variables de abajo son
**obligatorias** y no tienen valor por defecto útil:

```bash
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=simple_stock_flow;Username=simple_stock_flow;Password=LA_DEL_ENV"
export Jwt__SigningKey="una-clave-de-al-menos-32-caracteres-aqui"
export ASPNETCORE_ENVIRONMENT=Development

dotnet run --project src/bootstrap --urls http://localhost:5080
```

- **`--urls` no es opcional si quieres un puerto predecible.** `dotnet run` lee
  `src/bootstrap/Properties/launchSettings.json`, que fija `https://localhost:62596` y
  `http://localhost:62597`. El argumento `--urls` gana; la variable `ASPNETCORE_URLS` **no**.
- Swagger queda en `http://localhost:5080/swagger`, y solo con `ASPNETCORE_ENVIRONMENT=Development`.
- **Sin `Jwt__SigningKey` de 32 caracteres o más, la API se niega a arrancar** y lanza un
  `InvalidOperationException` que dice exactamente qué falta. Es deliberado: firmar con una
  clave de relleno arranca bien y reparte tokens falsificables. Esto afecta también a
  `dotnet ef` (§3).
- Si apuntas al Postgres del compose desde el host, necesitas el perfil de desarrollo que
  publica el 5432 (§3).

### Variables de entorno que lee el servicio

Doble guion bajo separa niveles de configuración.

| Variable | Qué es | Por defecto |
|---|---|---|
| `ConnectionStrings__Postgres` | Cadena de conexión | `Host=localhost;...;Username=postgres;Password=postgres` (solo sirve en local) |
| `Jwt__SigningKey` | Clave de firma, mínimo 32 caracteres | **ninguno — sin ella no arranca** |
| `Jwt__Issuer` · `Jwt__Audience` · `Jwt__LifetimeMinutes` | Emisor, audiencia y vigencia del token | `simple-stock-flow` · `simple-stock-flow-app` · `60` |
| `Storage__RootPath` | Carpeta donde se escriben los binarios | `/var/lib/simple-stock-flow/media` (en `Development`, `./.media`) |
| `Storage__PublicBaseUrl` | Prefijo con el que se publican | `/media` |
| `Cors__Origins__0` | Origen permitido | `http://localhost:4200` |

`Bootstrap__AdminUsername` y `Bootstrap__AdminPassword` **las lee el servicio al arrancar**:
`AdministratorBootstrap` crea con ellas al primer administrador cuando la tabla de usuarios está
vacía. Sin la contraseña no lo crea, y entonces no hay forma de entrar al sistema.

---

## 3. Dónde están los datos

### La base

| | |
|---|---|
| Motor | PostgreSQL 16 (`postgres:16-alpine`) |
| Base | **`simple_stock_flow`** |
| Usuario | **`simple_stock_flow`** |
| Contraseña | **`POSTGRES_PASSWORD` del archivo `.env` de `simple-stock-flow-infra`.** No se versiona y no tiene valor por defecto: si no lo tienes, no hay dónde leerlo, hay que ponerlo |
| Esquema | **`sales`** — no `public`. `SalesDbContext` hace `HasDefaultSchema("sales")` |
| Puerto | 5432 **dentro** de la red de Docker. **Al host solo se publica con el perfil de desarrollo** (abajo) |
| Volumen | `simple-stock-flow_pgdata`. Los datos sobreviven a `docker compose down`; `down -v` los borra |

Las cinco tablas son `sales.category`, `sales.product`, `sales.sale`, `sales.sale_item` y
`sales.user`, **en singular** por convención del proyecto. `user` no necesita comillas mientras
vaya cualificada por el esquema, que es como se consulta siempre.

### Mirar las filas con tus propios ojos

Sin instalar ningún cliente, con el stack levantado:

```bash
cd ../simple-stock-flow-infra
docker compose exec db psql -U simple_stock_flow -d simple_stock_flow -c "select * from sales.category;"
```

Y para navegar a mano: `docker compose exec db psql -U simple_stock_flow -d simple_stock_flow`, luego
`\dn` (esquemas), `\dt sales.*` (tablas) y `\q`.

### Migraciones

El esquema es de este repositorio y se cambia **solo** con migraciones, nunca con SQL suelto. No
se listan aquí: una lista escrita a mano se queda corta —esta llegó a declarar cuatro cuando ya
había nueve—. Para ver las aplicadas, pregúntaselo a la base.

**El historial de migraciones no vive en `sales`**, sino en `public."__EFMigrationsHistory"`
—ahí es donde EF lo pone y donde hay que mirarlo—:

```bash
docker compose exec db psql -U simple_stock_flow -d simple_stock_flow \
  -c 'select "MigrationId" from public."__EFMigrationsHistory";'
```

Para crear una migración nueva hace falta `Jwt__SigningKey` en el entorno: `dotnet ef` construye
el host real de la aplicación, y ese host se niega a arrancar sin la clave.

```bash
Jwt__SigningKey="una-clave-de-al-menos-32-caracteres-aqui" \
dotnet ef migrations add <Nombre> \
  --project src/adapters/outbound/persistence \
  --startup-project src/bootstrap \
  --output-dir Migrations
```

### Las imágenes no están en la base

Los binarios que sube `POST /api/products/{id}/image` **no van a Postgres**. `LocalFileStorage`
los escribe en la carpeta `Storage__RootPath` = `/var/lib/simple-stock-flow/media`, que en el compose
es el volumen `simple-stock-flow_media` (y nunca dentro de la imagen del contenedor). La base solo
guarda la clave del archivo. Se sirven como estáticos bajo `/media/{key}`.

```bash
docker compose exec service ls -la /var/lib/simple-stock-flow/media
```

(En Git Bash sobre Windows, antepón `MSYS_NO_PATHCONV=1` o la ruta absoluta se traduce sola y
el comando falla.)

La carpeta está vacía hasta que alguien sube una imagen. Tras un arranque en frío y el sembrador
de `simple-stock-flow-tools` quedan ahí los 22 binarios del catálogo, servidos bajo `/media/{key}`.

---

## 4. Cómo se prueba

```bash
dotnet build SimpleStockFlow.sln     # 0 errores y 0 warnings, o no compila
dotnet test SimpleStockFlow.sln      # los tres proyectos de prueba
```

`TreatWarningsAsErrors` está activo: cualquier warning rompe la compilación.

**Aquí no se publica cuántas pruebas son.** La cifra la imprime `dotnet test` al terminar, y una
escrita en esta línea envejece con cada tarea. Lo que sí conviene saber es cuál necesita Docker:

| Proyecto | ¿Necesita Docker? |
|---|---|
| `tests/Domain.UnitTests` | **No** |
| `tests/Application.UnitTests` | **No** — los puertos outbound van en doble (NSubstitute) |
| `tests/Adapters.IntegrationTests` | **Sí** — levanta un Postgres real con Testcontainers |

```bash
dotnet test tests/Domain.UnitTests          # sin Docker
dotnet test tests/Application.UnitTests     # sin Docker
dotnet test tests/Adapters.IntegrationTests # con Docker corriendo
```

Testcontainers levanta su propio Postgres efímero: **no toca la base del compose** y no hace
falta que el stack esté arriba, solo el demonio de Docker.

---

## 5. Qué falta

**Nada.** No queda ningún método sin implementar ni ningún `TODO` en los servicios, y el sistema se
ejerce de punta a punta. Comprobado el 2026-09-21.

Este apartado llegó a declarar lo contrario durante días —once métodos sin implementar, el login
devolviendo 500— y por eso **ya no publica recuentos**: una cifra escrita en prosa no tiene quien la
vigile, y caduca más rápido cuanto mejor va el trabajo. Para saber el estado, ejecútelo:

```bash
dotnet test SimpleStockFlow.sln                # los tres proyectos; los de integración piden Docker
cd ../simple-stock-flow-infra && ./verify.sh   # la frontera entre el código y su empaquetado
```

## Las operaciones

**No se listan aquí, y es deliberado.** La lista que este apartado llegó a publicar tenía doce
filas y le faltaba `GET /api/categories`: una tabla escrita a mano se desincroniza del código sin
que nada se queje.

Con el sistema levantado están **documentadas y ejecutables** en `http://localhost:8080/swagger`,
generadas del propio código. Se abren también desde la barra del portal.

## Licencia

MIT. Copyright (c) 2026 Jesus Ariel Gonzalez Bonilla. El texto completo está en
[`LICENSE`](LICENSE): puede usarse, copiarse, modificarse y distribuirse libremente, con la única
condición de conservar el aviso de copyright.
