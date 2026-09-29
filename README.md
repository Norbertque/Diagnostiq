# Laptop Diagnostics

Portable Windows tool that checks a laptop's hardware: identity and specs, battery health,
storage health, temperatures under load, display, keyboard, touchpad, audio, webcam, USB and network.

## v1.0-refurb (this version)

Built for a used-Dell-Latitude refurbishment run. Besides the tests it can write each
laptop's condition and specs into the `Laptopy_prodej.xlsx` inventory:

- **Save result (JSON)** on each tested laptop, then **Batch import** on the PC that has the
  inventory (OneDrive-synced local copy, closed in Excel during the import).
- Unknown serial numbers are added as new rows; the price formulas and the `CELKEM` count
  follow automatically.

v2 replaces the Excel workflow with a printable report and works on any laptop vendor.

## Build

Requires the .NET 8 SDK on Windows.

```
dotnet publish KontrolniProtokol.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -o publish
```

`publish\KontrolniProtokol.exe` is a single portable file (~75 MB). It asks for administrator
rights on start (needed for TPM, sensors and raw disk reads).
