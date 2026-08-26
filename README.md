# Room Booking Chatbot

Conversational assistant that manages meeting room bookings for the Cubo Itaú office,
built for the Promtior technical challenge.

**Gonzalo Gularte** — [ggularteuy@gmail.com](mailto:ggularteuy@gmail.com)

**Running at <https://room-booking-chatbot-production.up.railway.app>** — sign in as `User1` or
`User2` with the password from the challenge document.

<details>
<summary><b>🇪🇸 Leer en español</b></summary>

<br>

## El problema

Un asistente conversacional con llamada a herramientas que permite a un usuario autenticado
reservar, consultar y cancelar salas de reunión hablando en lenguaje natural.

### Reglas del dominio

- Cinco salas: **A, B, C, D y E**, cada una con su capacidad máxima.
- Las reservas van en **bloques de 30 minutos**.
- Los bloques contiguos se pueden combinar en una sola reserva, hasta **3 horas**.
- Un bloque lo ocupa una sola reserva — sin dobles reservas ni solapamientos. Una reserva de
  10:00 a 11:30 bloquea cualquier inicio anterior a las 11:30.
- Toda reserva necesita **título** y **cantidad de asistentes**, que no puede exceder la
  capacidad de la sala.
- Dos usuarios, `User1` y `User2`, con una contraseña compartida.
- Una reserva que ya terminó se rechaza. Esta última no está en el challenge; ver *Supuestos*.

### Las herramientas del asistente

| Herramienta | Para qué |
|---|---|
| `create_booking` | Reservar una sala en un rango, con título y asistentes, a nombre del usuario logueado |
| `list_available_rooms` | Qué salas quedan libres en un rango, y por qué las otras no |
| `get_room_schedule` | Agenda de una sala bloque por bloque |
| `cancel_booking` | Cancelar una reserva propia |

La validación —contigüidad, tope de 3 horas, capacidad, solapamientos— la hace la capa de
reservas, **no el prompt**.

## Cómo correrlo

Necesitás el SDK de .NET 10 y una API key de Groq, que es gratis y no pide tarjeta:
<https://console.groq.com>.

```bash
cd src/RoomBooking.Web
dotnet user-secrets set "Groq:ApiKey" "<tu key>"
cd ../..
dotnet run --project src/RoomBooking.Web --launch-profile https
```

Abrí <https://localhost:7264> y entrá como `User1` o `User2` con la contraseña del documento del
challenge. La base se crea y se siembra sola en el primer arranque, así que no hay paso de
migración.

### El notebook

`notebook/technologies.ipynb` explica de qué está hecha la solución y muestra cada pieza
funcionando, leyendo el código de este repositorio en vez de repetirlo. Va commiteado con sus
salidas, así que se puede leer sin ejecutarlo.

### Tests

```bash
dotnet test
```

Dos tests manejan el modelo real de punta a punta y se saltean si falta `GROQ_API_KEY`, para que
la suite corra sin conexión. Para que su ausencia sea un error en vez de un salteo, poné
`REQUIRE_LIVE_TESTS=1`.

## Por qué el notebook es Python y la solución no

El challenge pide un notebook Jupyter. El kernel de C# —.NET Interactive y la extensión Polyglot
Notebooks— **se deprecó en 2026**: la extensión el 27 de marzo, el runtime el 24 de abril, y el
repositorio quedó archivado. Un notebook en C# se apoyaría en herramientas sin mantenimiento que
el evaluador tendría que instalar para poder correrlo.

Por eso la solución es C# y el notebook es Python: documenta la arquitectura, muestra las
definiciones de herramientas en C# y ejercita el mismo modelo con las mismas definiciones.

## Dónde vive la validación

Las restricciones —contigüidad de bloques, tope de 3 horas, capacidad de sala, rechazo de
solapamientos— se hacen cumplir en `RoomBooking.Core`, no en el prompt del sistema. Las
herramientas devuelven errores estructurados y el asistente los comunica. **Un modelo no es una
capa de validación.**

## Supuestos

**Capacidades de las salas.** El challenge exige que sean específicas por sala pero nunca dice
los números. Estos son un supuesto, elegidos para que algunas salas queden chicas para una
reunión normal y otras no — que es lo que hace la regla observable:

| Sala | A | B | C | D | E |
|---|---|---|---|---|---|
| Capacidad | 4 | 6 | 8 | 12 | 20 |

Viven en `SeedData.Rooms` y cambiarlas no requiere ninguna otra edición.

**Hasta cuándo se puede reservar.** El challenge no fija horizonte, lo que dejaba el año 9999
reservable. Se puede reservar hasta un año hacia adelante.

**Largo del título.** La columna declara 200 caracteres y SQLite no hace cumplir los largos
declarados, así que los títulos eran ilimitados — y vuelven al contexto del asistente en cada
consulta de agenda. Se valida como regla.

**Reservas en el pasado.** El challenge no las prohíbe, así que una reserva para el martes pasado
habría sido tan válida como una para mañana. Eso se lee como algo a medio terminar, así que se
rechazan las que ya **terminaron**, y sólo ésas. Una reunión que empezó hace diez minutos todavía
vale la pena registrarla.

**Zona horaria.** Una oficina, un reloj de pared. Los horarios se guardan y comparan tal como se
escriben, sin conversión.

## Desplegar en Railway

Cuatro configuraciones. Ninguna es adivinable a partir del error que te da sin ella, así que van
con el motivo:

| | |
|---|---|
| `Groq__ApiKey` | El doble guión bajo es cómo .NET lee configuración anidada. Sin esto el contenedor aborta al arrancar y lo dice |
| Volumen en `/data` | Donde vive la base. Sin esto, cada redeploy vacía la oficina |
| `RAILWAY_RUN_UID=0` | Railway entrega el volumen al contenedor propiedad de `root` mientras esta imagen corre como usuario sin privilegios. Sin esto no puede crear la base |
| Puerto destino `8080` | Railway inyecta `PORT` y la app escucha en el que le den; 8080 es el que la imagen usa por defecto, así que coinciden |

El autodeploy necesita que la GitHub App de Railway **tenga acceso al repositorio** — autorizarla
al iniciar sesión no es lo mismo, y sin la instalación Railway reporta *"GitHub Repo not found"* y
nunca se entera de un push.

</details>

## The problem

A chatbot with tool-calling capabilities that lets an authenticated user book, inspect
and cancel meeting rooms through natural conversation.

### Domain rules

- Five rooms: **A, B, C, D, E**. Each has its own maximum capacity.
- Bookings are made in **30-minute slots**.
- Contiguous slots may be combined into a single appointment, up to **3 hours**.
- A slot is held by at most one booking — no double bookings, no overlaps.
  A booking running 10:00–11:30 blocks any appointment starting before 11:30.
- Every appointment requires a **title** and a **number of attendees**, which may not
  exceed the room's capacity.
- Two users, `User1` and `User2`, authenticate with a shared password.
- A booking that has already ended is refused. This one is not in the challenge; see
  Assumptions.

### Assistant tools

| Tool | Purpose |
|---|---|
| `create_booking` | Book a room for a date/time range, with title and attendee count, for the logged-in user |
| `list_available_rooms` | List rooms free over a requested time range |
| `get_room_schedule` | Return available vs. occupied slots for one room over a range |
| `cancel_booking` | Cancel a booking owned by the logged-in user |

Constraint validation (contiguity, 3-hour cap, capacity, overlaps) is enforced by the
booking layer, not left to the model.

## Layout

```
.
├── src/
│   ├── RoomBooking.Core/    # domain entities, booking rules, EF Core + SQLite
│   ├── RoomBooking.Agent/   # Microsoft.Extensions.AI chat client and tools
│   └── RoomBooking.Web/     # ASP.NET Core host, cookie auth, Blazor chat UI
├── tests/
│   └── RoomBooking.Tests/   # xUnit coverage of the booking rules
├── notebook/                # required deliverable: technologies notebook (Python)
└── doc/                     # required deliverable: overview + component diagram
```

## Deliverables

- [ ] Booking system enforcing the domain rules above
- [ ] Authentication for `User1` / `User2`
- [ ] Chatbot wired to the booking system via tool calling
- [ ] Jupyter notebook explaining the technologies used, with code examples
- [ ] `doc/` — project overview and component diagram
- [ ] Cloud deployment

## Stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10 |
| Domain and persistence | EF Core 10 + SQLite |
| AI layer | `Microsoft.Extensions.AI` 10.9 — `IChatClient`, `AIFunctionFactory`, `UseFunctionInvocation()` |
| Model provider | Groq, through its OpenAI-compatible endpoint |
| Models | `openai/gpt-oss-120b`, falling back to `openai/gpt-oss-20b` |
| Web and chat UI | ASP.NET Core 10 + Blazor Server |
| Tests | xUnit |
| Notebook | Python |
| Deployment | Railway, via Dockerfile |

### Why the notebook is Python and the solution is not

The challenge requires a Jupyter notebook. The C# Jupyter kernel — .NET Interactive and
the Polyglot Notebooks extension — was deprecated in 2026 (extension on March 27, runtime
on April 24, repository archived), so a C# notebook would rest on unmaintained tooling that
the reviewer has to install and run. The solution is therefore C#, and the notebook is
Python: it documents the architecture, shows the C# tool definitions, and exercises the
deployed API to display real tool-calling traces.

### The daily allowance

Groq's free tier allows a fixed number of tokens per day **per model**, and every turn resends
the instructions and the five tool definitions, so a day of use reaches it. When the first model
refuses, the assistant repeats that one call against a second model, which has an allowance of
its own — the fallback sits beneath the tool-invocation loop, so a refusal arriving after a
booking has been created cannot cause it to be created twice.

If both are spent, the chat says so in a sentence and notes that the bookings themselves are
unaffected, rather than leaving a request pending while the client waits out a Retry-After
measured in minutes. Set `Groq:FallbackModel` to empty to disable the fallback.

### Where validation lives

Booking constraints — slot contiguity, the 3-hour cap, room capacity, overlap rejection —
are enforced in `RoomBooking.Core`, not in the system prompt. The tools return structured
errors and the assistant relays them. A model is not a validation layer.

## Running it

Requires the .NET 10 SDK and a Groq API key, which is free and needs no card:
<https://console.groq.com>.

```bash
cd src/RoomBooking.Web
dotnet user-secrets set "Groq:ApiKey" "<your key>"
cd ../..
dotnet run --project src/RoomBooking.Web --launch-profile https
```

Then open <https://localhost:7264> and sign in as `User1` or `User2` with the password
`TechnicalChallengePromtior`. The database is created and seeded on first run, so there
is no migration step.

### The notebook

`notebook/technologies.ipynb` explains what the solution is built from and shows each piece
working, reading the code out of this repository rather than restating it. It is committed with
its outputs, so it can be read without being run.

```bash
cd notebook
pip install -r requirements.txt
GROQ_API_KEY="<your key>" jupyter lab technologies.ipynb
```

### Tests

```bash
dotnet test
```

Two of the tests drive the live model end to end and are skipped when `GROQ_API_KEY` is
absent, so the suite runs offline. To make their absence a failure instead, set
`REQUIRE_LIVE_TESTS=1`.

```bash
GROQ_API_KEY="<your key>" REQUIRE_LIVE_TESTS=1 dotnet test
```

### Container

```bash
docker build -t room-booking .
docker run -p 8080:8080 -e Groq__ApiKey="<your key>" -v room-booking-data:/data room-booking
```

The volume matters: the database lives at `/data`, and a container filesystem does not survive
a redeploy.

### Deploying to Railway

Four settings. None of them is guessable from the error you get without it, so they are listed
with the reason.

| | |
|---|---|
| `Groq__ApiKey` | The double underscore is how .NET reads nested configuration. Without it the container aborts at startup and says so |
| Volume at `/data` | Where the database lives. Without it, every redeploy empties the office |
| `RAILWAY_RUN_UID=0` | Railway hands the volume to the container owned by `root` while this image runs as a non-root user. Without it the database cannot be created |
| Target port `8080` | Railway injects `PORT` and the application listens on whatever it is given; 8080 is the image's fallback, so the two agree |

The image keeps its non-root user for every other platform, most of which give the mount to the
container's own user.

Autodeploy needs Railway's GitHub App to have access to the repository — authorising it at sign-in
is not the same thing, and without the installation Railway reports "GitHub Repo not found" and
never sees a push.

## Assumptions

**Room capacities.** The challenge requires room-specific capacities but never states the
values — it says only that each room has a maximum and that attendee counts must not exceed
it. The following are assumed, chosen so that some rooms are small enough for a routine
meeting to exceed them and others are not, which is what makes the rule observable:

| Room | A | B | C | D | E |
|---|---|---|---|---|---|
| Capacity | 4 | 6 | 8 | 12 | 20 |

They live in `SeedData.Rooms` and changing them requires no other edit.

**Time zone.** Bookings are stored and compared as wall-clock times in a single office time
zone. The challenge describes one office, so no conversion is modelled.

**How far ahead.** Nothing in the challenge sets a horizon, which left the year 9999
bookable — junk that would sit in its owner's list forever. Rooms can be held up to a
year ahead.

**Title length.** The column declares 200 characters and SQLite does not enforce declared
lengths, so titles were unbounded and were read back into the assistant's context on every
schedule request. 200 is enforced as a rule.

**Bookings in the past.** The challenge lists its constraints explicitly and says nothing
about the past, so a reservation for last Tuesday would otherwise be as valid as one for
tomorrow. That reads as unfinished, so bookings that have already *ended* are refused —
and only those. A meeting that began ten minutes ago is still worth recording, and a
stricter rule risked rejecting a case the challenge intends to work.
