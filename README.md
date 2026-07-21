# O-Helper

O-Helper is a lightweight Windows tray application for controlling supported HP OMEN systems. It uses confirmed HP WMI BIOS commands and model capability data, hiding write-capable controls when support has not been established.

See the [full documentation](docs/README.md) for features, hardware requirements, and current limitations.

## Build

Requirements: Windows and the .NET SDK selected by `global.json`.

```powershell
dotnet restore app/OHelper.sln --locked-mode
dotnet build app/OHelper.sln --no-restore
```

Publish a framework-dependent, single-file Windows executable:

```powershell
dotnet restore app/OHelper.sln --locked-mode --runtime win-x64
dotnet publish app/OHelper.sln --configuration Release --runtime win-x64 --no-restore -p:PublishSingleFile=true --no-self-contained
```

Runtime firmware operations require administrator privileges. Building does not.
