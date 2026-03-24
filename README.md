# ImageZeus

Fast, lightweight Windows image viewer.

[![Release](https://github.com/YOUR_USERNAME/ImageZeus/actions/workflows/release.yml/badge.svg)](https://github.com/YOUR_USERNAME/ImageZeus/actions/workflows/release.yml)

## Download

Grab the latest `ImageZeusSetup.exe` from the [Releases](https://github.com/YOUR_USERNAME/ImageZeus/releases) page.

## Cutting a Release

```bash
git tag v1.0.0
git push origin v1.0.0
```

GitHub Actions will build the app, compile the installer, and attach `ImageZeusSetup.exe` to the release automatically.

Use a pre-release suffix for betas: `v1.1.0-beta1`

## Build from Source

### Prerequisites
- .NET SDK 8.x
- Windows 10/11

### Restore + Build
```powershell
dotnet build ImageZeus.sln
```

### Publish (self-contained executable)
```powershell
.\publish.bat
```

### Build installer
Requires [Inno Setup 6.x](https://jrsoftware.org/isinfo.php). After running `publish.bat`:
```
iscc installer\ImageZeus.iss
```

## Run from source
```powershell
dotnet run --project ImageViewer.App -- "C:\path\to\image.jpg"
```

## Architecture
See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Dependency Versions
- Avalonia: `11.3.12`
- SkiaSharp: `2.88.9`
