# Project Structure

This document describes the repository as it exists today. Feature documents under `docs/feature` describe scope and acceptance criteria; implementation status is recorded in the relevant plan when verified.

## Repository map

```text
Utilities/
├── docs/
│   ├── PROJECT_STRUCTURE.md
│   ├── THIRD_PARTY_NOTICES.md
│   ├── TESSERACT_MODEL_LICENSE.txt
│   └── feature/
│       ├── AGENT.MD
│       ├── 00-foundation/
│       ├── 01-domain/
│       ├── 02-persistence/
│       ├── 03-addresses-and-services/
│       ├── 04-readings-and-tariffs/
│       ├── 05-data-recovery/
│       ├── 06-ocr/
│       ├── 07-reminders/
│       ├── 08-ui-localization/
│       ├── 09-settings/
│       └── 10-quality/
├── src/
│   ├── Utilities.Core/
│   ├── Utilities.Infrastructure/
│   └── Utilities.Mobile/
├── tests/
│   └── Utilities.Core.Tests/
├── .gitignore
├── dotnet-tools.json
├── METER_APP_PLAN.md
└── Utilities.sln
```

## Solution projects

### `src/Utilities.Core`

The inner application layer. It holds domain types, business rules, application use cases, and storage or device-facing contracts. It targets `net10.0` and has no external package references, so it does not depend on MAUI, EF Core, SQLite, Android APIs, or an OCR engine.

- `Domain/Models/` — address, meter, subscription, reading, tariff, reminder, and soft-delete entities.
- `Domain/ValueObjects/` — small validated values, including the normalized meter reading area.
- `BusinessLogic/` — domain calculations and rules such as reading assessment, tariff selection, reminder rules, and address deletion.
- `Application/UseCases/UtilitiesApplicationService.cs` — application operations used by the presentation layer.
- `Application/Abstractions/IUtilitiesStore.cs` — persistence boundary and recently-deleted item contract.
- `Infrastructure/Abstractions/` — ports for external capabilities such as temporary photos, reading recognition, and local reminders. These are contracts, not platform implementations.

### `src/Utilities.Infrastructure`

The persistence adapter. It references Core and contains the EF Core SQLite implementation.

- `Persistence/UtilitiesDbContext.cs` — EF model, mappings, query filters for soft-deleted entities, and value conversions.
- `Persistence/EfUtilitiesStore.cs` — implements `IUtilitiesStore` using EF Core.
- `Persistence/PersistenceRegistration.cs` — registers the SQLite context and store with dependency injection.
- `Persistence/UtilitiesDbContextFactory.cs` — design-time context factory for EF tooling.
- `Persistence/Migrations/` — versioned database schema changes and the model snapshot. Add migrations here when the persisted model changes.

### `src/Utilities.Mobile`

The .NET MAUI presentation and platform-adapter project. It currently targets `net10.0-android` and references both Core and Infrastructure.

- `App.xaml` / `App.xaml.cs` — MAUI application resources and root-window setup.
- `MainPage.xaml` / `MainPage.xaml.cs` — home screen markup and its page lifecycle/navigation glue.
- `*Page.xaml` and matching code-behind files — screen markup and thin MAUI lifecycle or native gesture glue. Screen state, actions, and dialog-driven workflows live in ViewModels.
- `ViewModels/` — presentation state and application operations for home, address, meter, service type, recovery, reminders, settings, and photo-crop screens. `ViewModelBase.cs` provides property-change notification support.
- `MauiProgram.cs` — dependency registration and application startup configuration.
- `AppText.cs` — localized text and language selection.
- `NumericInputParser.cs` — culture-aware parsing for numeric values entered in MAUI dialogs.
- `Infrastructure/Media/` — MAUI implementation of temporary meter-photo operations.
- `Infrastructure/Ocr/` — Tesseract adapter implementing Core's `IReadingRecognizer` contract.
- `Services/` — MAUI implementations of dialog, navigation, and camera permission boundaries.
- `Platforms/Android/` — Android application entry points, manifest, and local notification scheduling adapter.
- `Platforms/iOS/`, `Platforms/MacCatalyst/`, `Platforms/Windows/` — MAUI platform bootstrap and platform metadata. The current project target configuration is Android.
- `Resources/Strings/` — English and Ukrainian resource files.
- `Resources/Styles/`, `Images/`, `Fonts/`, `AppIcon/`, `Splash/`, `Raw/` — MAUI resources.

Keep view models and view code in Mobile. Keep domain and application rules in Core. Keep database providers and platform APIs in outer adapters, with Core-facing contracts where required.

## Documentation under `docs`

### `docs/PROJECT_STRUCTURE.md`

This repository map. Update it when projects, major folders, or documentation conventions change.

### `docs/feature/AGENT.MD`

Instructions for contributors and coding agents working with feature plans. The `feature` folder is exclusively for English-language task and feature descriptions; it is not an application source folder.

## Root files and folders

- `Utilities.sln` — solution containing Core, Infrastructure, and Mobile.
- `METER_APP_PLAN.md` — original product plan and requirements reference.
- `dotnet-tools.json` — .NET local-tool manifest, currently with no tools declared. The ignored `.tools/ef/` folder contains an EF command-line binary in this workspace.
- `.gitignore` — excludes build output, IDE state, local database files, and local tool binaries.
- `tests/Utilities.Core.Tests/` — automated unit and SQLite integration tests for business rules, OCR parsing/workflow, and soft delete/restore behavior.

## Dependency direction

```text
Utilities.Mobile ──▶ Utilities.Infrastructure ──▶ Utilities.Core
       └────────────────────────────────────────▶ Utilities.Core
```

Core must remain independent of UI, database, and platform libraries. Infrastructure implements Core's persistence contract. Mobile composes the projects and supplies MAUI/Android adapters. Feature plans should preserve this direction.
