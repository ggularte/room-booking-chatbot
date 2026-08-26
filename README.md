# Room Booking Chatbot

Conversational assistant that manages meeting room bookings for the Cubo Itaú office,
built for the Promtior technical challenge.

**Gonzalo Gularte** — [ggularteuy@gmail.com](mailto:ggularteuy@gmail.com)

**Running at <https://room-booking-chatbot-production.up.railway.app>** — sign in as `User1` or
`User2` with the password from the challenge document.

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
