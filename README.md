# Room Booking Chatbot

Conversational assistant that manages meeting room bookings for the Cubo Itaú office,
built for the Promtior technical challenge.

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

### Where validation lives

Booking constraints — slot contiguity, the 3-hour cap, room capacity, overlap rejection —
are enforced in `RoomBooking.Core`, not in the system prompt. The tools return structured
errors and the assistant relays them. A model is not a validation layer.

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
