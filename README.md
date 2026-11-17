# Tezgâh

[![CI](https://github.com/emrecanAy/tezgah/actions/workflows/ci.yml/badge.svg)](https://github.com/emrecanAy/tezgah/actions/workflows/ci.yml)

One platform for small businesses, sold module by module. A hairdresser gets customers, a stamp card and appointments; a café gets the stamp card and stock; a logistics firm gets only fleet tracking. Same code base, same database, and each company sees only what it was given.

.NET 10 modular monolith, React 19, PostgreSQL. The UI is in Turkish.

![Loyalty screen of a café: stamp cards filling up towards a free coffee](docs/screenshots/loyalty.png)

| Hairdresser: appointments | Logistics: fleet | Platform admin: who gets what |
|---|---|---|
| ![Day agenda per staff member](docs/screenshots/agenda.png) | ![Vehicles with inspection and insurance alerts](docs/screenshots/fleet.png) | ![Companies and their module checkboxes](docs/screenshots/admin.png) |

## Modules

| Module | What it does | Needs |
|---|---|---|
| **Müşteriler** (customers) | Customer records, one per phone number per company | |
| **Sadakat** (loyalty) | Stamp cards with a rule the company defines: "every 4 haircuts, one free", "every 4 coffees, one on us". Optional automatic stamp when an appointment is completed | Customers |
| **Randevu** (appointments) | Services, staff, a day agenda. A staff member cannot be double-booked | Customers |
| **Filo** (fleet) | Vehicles, fuel and maintenance logs, consumption, alerts for inspection, insurance and service dates | |
| **Stok** (inventory) | Products, stock in/out movements, low-stock warnings | |

## Two ways to choose modules

**Per company, at runtime.** The platform administrator ticks the modules each company has. Turning one off closes it immediately (`403 module_not_enabled`) but keeps its data, so turning it back on loses nothing. Dependencies are enforced: loyalty cannot be given without customers.

**Per installation, at start-up.** `Platform:Modules` lists the modules a deployment ships. A customer who bought only fleet tracking gets an installation where the other modules are never registered: no endpoints, no tables.

```bash
docker compose -f compose.yaml -f compose.fleet.yaml up --build
```

## Run it

Requirements: Docker.

```bash
docker compose up --build
```

Open http://localhost:8080. The first start creates three demo companies with data. The login page lists their accounts:

| Account | Password | Modules |
|---|---|---|
| `makas@tezgah.local` (hairdresser) | `demo1234` | Customers, Loyalty, Appointments |
| `kahve@tezgah.local` (café) | `demo1234` | Customers, Loyalty, Inventory |
| `lojistik@tezgah.local` (logistics) | `demo1234` | Fleet |
| `admin@tezgah.local` (platform admin) | `admin1234` | Opens companies, assigns modules |

API reference: http://localhost:5080/docs

### Things to try

- Sign in as the hairdresser, open **Randevu**, click an appointment and mark it **Tamamlandı**. Then open **Sadakat**: the customer's card has a new stamp. The two modules do not know each other.
- Try to book the same staff member for an overlapping time. The second booking is refused.
- Sign in as the admin, give Yıldız Lojistik the **Stok** module, then sign in as Yıldız Lojistik: it appears in the menu.

### Working on the code

```bash
docker compose up -d postgres

dotnet run --project src/Tezgah.Api     # API on http://localhost:5080  (.NET 10 SDK)
cd web && npm install && npm run dev    # web on http://localhost:5173  (Node 22)
```

```bash
dotnet test                             # needs Docker: Testcontainers starts PostgreSQL
cd web && npm run lint && npm test
```

## Architecture

```mermaid
flowchart TB
    web[React client<br/>menu built from GET /api/me] --> api

    subgraph api[Tezgah.Api - one deployable]
        platform[Platform<br/>companies, users, module switchboard]
        subgraph modules[Modules - each its own project and schema]
            customers[Customers]
            loyalty[Loyalty]
            appointments[Appointments]
            fleet[Fleet]
            inventory[Inventory]
        end
    end

    loyalty -. ICustomerDirectory .-> customers
    appointments -. ICustomerDirectory .-> customers
    appointments -- AppointmentCompleted<br/>via outbox --> loyalty
    api --> pg[(PostgreSQL<br/>one schema per module)]
```

**A module is a project that implements `IModule`.** It registers its services, maps its endpoints, owns its migrations and can seed demo data. The host lists the modules once; everything else (routing, the per-company gate, migrations, seeding) loops over that list. Adding a sixth module means one project and one line in `Program.cs`.

**Modules never reference each other.** They compile against `Tezgah.Core` only and talk through it: `ICustomerDirectory` for looking up customers, integration events for "something happened". `ArchitectureTests` fails the build if a module references another module or exposes anything but its `IModule` class.

**Multi-tenancy in one place.** Module data implements `ITenantOwned`. The shared `TenantDbContext` stamps the company on insert and adds a query filter on every query, so module code never writes `Where(x => x.TenantId == ...)` and cannot forget it. Reading tenant data without a tenant throws instead of returning everything.

**The module gate in one place.** Every module's endpoints are mounted under `/api/{module}` behind one filter that checks the company's selection. A module cannot leave an endpoint open by mistake.

**Events respect the selection too.** When an appointment is completed, Appointments publishes `AppointmentCompleted` through its outbox. The dispatcher runs Loyalty's handler only if that company has Loyalty, inside that company's tenant scope. A hairdresser without a stamp card gets no loyalty data.

## Correctness under concurrency

Small businesses have two tills and a phone, and the same thing does happen twice at once.

| Situation | Guard | Test |
|---|---|---|
| Ten receptionists book the same 10:00 slot for the same stylist | PostgreSQL `EXCLUDE USING gist (staff_id WITH =, tstzrange(starts_at, ends_at) WITH &&)`. Cancelled appointments do not block | `Ten_people_booking_the_same_slot_at_once_produce_exactly_one_appointment` |
| Two tills stamp the same customer's card at the same moment | The card row is locked (`FOR UPDATE`); the very first stamps race to create the card and are settled by a unique index | `Stamps_given_at_the_same_moment_are_all_counted` |
| One reward redeemed from two tills | Same row lock | `One_reward_redeemed_from_several_tills_at_once_is_handed_out_once` |
| Thirty sales of the last ten items | One conditional `UPDATE ... SET stock = stock - n WHERE stock - n >= 0`, plus a `CHECK (stock >= 0)` | `Thirty_people_taking_one_item_each_from_a_stock_of_ten_empty_it_exactly` |
| An appointment's "completed" event delivered twice | The stamp records its source; a second delivery finds it and does nothing | `Completing_an_appointment_stamps_the_customers_loyalty_card_exactly_once` |
| One company reading another's data | Tenant query filter; foreign ids answer 404 | `TenantIsolationTests` |

## Repository layout

```
src/
  Tezgah.Api/              composition root: the module list, auth, migrations at start-up
  Tezgah.Core/             IModule, ModuleCatalog, tenancy, outbox, Result, cross-module contracts
  Tezgah.Platform/         companies, users, sign-in (JWT), admin endpoints, demo companies
  Modules/
    Tezgah.Customers/
    Tezgah.Loyalty/
    Tezgah.Appointments/
    Tezgah.Fleet/
    Tezgah.Inventory/
tests/Tezgah.Tests/        integration tests on real PostgreSQL, module catalog, architecture rules
web/                       React + TypeScript + Vite + TanStack Query, one folder per module
```

## Deliberately left out

- **Staff accounts and roles inside a company.** Each company has its owner account. Roles exist in the token but every company user can do everything in the modules they have.
- **Billing.** Which modules a company pays for is the admin's checkbox, not a subscription system.
- **Working hours and online self-booking.** The agenda shows 09:00 to 20:00 and bookings are made by staff.
- **SMS or e-mail reminders** for appointments and fleet due dates.

## License

MIT
