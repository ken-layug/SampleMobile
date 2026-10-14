# SampleMobile — guidance for Claude Code

Provider-agnostic notification POC: ASP.NET Core 10, Blazor (per-page Interactive Server), PostgreSQL via EF Core/Npgsql, Firebase Admin SDK. Design: [docs/architecture.md](docs/architecture.md). Mobile contract: [docs/mobile-api-contract.md](docs/mobile-api-contract.md).

## Layer rule (enforced by project references and `tests/.../Architecture/DependencyRuleTests.cs`)

- `Domain`: no packages. Aggregates own their state transitions; domain events are past-tense records in `Domain/Events/{Aggregate}Events.cs`.
- `Application`: use cases in `Features/{Feature}/{UseCase}.cs` (request record and handler in one file, dispatched through `ISender`); ports in `Abstractions/`; it only references `Microsoft.Extensions.*.Abstractions`/`Options`.
- `Infrastructure`: the only place that names a vendor (EF Core, Npgsql, Identity, FirebaseAdmin). Provider ids such as `"fcm"`/`"simulated"` live in `Push/PushProviderIds.cs`, never in Application.
- `Web`: composition root, controllers, Blazor components. The mobile user id always comes from `User.GetUserId()`.

## Conventions that aren't obvious from one file

- Braces follow `.editorconfig` (K&R). Expected failures return `OperationResult`; HTTP mapping lives only in `Web/Common/OperationResultExtensions.cs`. Validation error codes are camelCase property names.
- Every domain event goes to the transactional outbox through `UnitOfWork`, never dispatched in-process. `IDomainEventHandler<T>` implementations run **at least once**, so they must be idempotent (deduplication key plus unique index). Register each one explicitly in `Application/DependencyInjection.cs`; a guard test enforces this.
- New push providers: implement `IPushNotificationProvider` in `Infrastructure/Push/<Name>/` and add one line in `AddPushProviders`. Use cases don't change. Only an explicit "address is invalid" result may disable a device.
- Never log or return provider addresses unmasked (`TokenMasker`). Never commit credentials.
- Blazor: every component with code uses a `.razor.cs` code-behind. Interactive pages send requests through `IDashboardSender` (one DI scope per call), never by injecting `ISender` or a `DbContext` into the circuit.
- Migrations are generated with `dotnet ef`, never hand-written. Timestamps come from the injected `TimeProvider`, which rounds to PostgreSQL's microsecond precision.

## Commands

```bash
docker compose up -d                                   # PostgreSQL on localhost:5433
dotnet run --project src/SampleMobile.Web --launch-profile http
dotnet build SampleMobile.slnx
dotnet test                                            # needs Docker (Testcontainers)
dotnet test -- --filter-not-trait "Category=Integration"
dotnet ef migrations add <Name> --project src/SampleMobile.Infrastructure --startup-project src/SampleMobile.Web --output-dir Persistence/Migrations
```

Secrets (`Jwt:SigningKey`, `Firebase:*`) go in user-secrets or environment variables, never in appsettings files. Development demo credentials are in `appsettings.Development.json` (`DemoSeed`).
