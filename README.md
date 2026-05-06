# POOLSEC — AutoCAD Pool Section Generator

AutoCAD .NET plugin that generates complete pool section drawings (plan, long section, cross section, details) with one command: `POOLSEC`.

## Features
- Plan view with dimensions and plumbing symbols
- Long section A-A with transition slope calculation
- Cross section B-B
- Detail A — wall and floor reinforcement
- Detail B — Skimmer or Overflow system
- Pool types: Skimmer, Overflow, Hybrid
- Optional pump room and balance tank
- ISPSC 2024 compliant (1:7 max slope)
- Arabic interface

## How to use the GitHub Actions build

1. **Push** to the repository (or go to Actions → Build POOLSEC → Run workflow)
2. Wait 2-3 minutes for the build to complete
3. Open the completed workflow run
4. Download **POOLSEC-x64** artifact
5. Extract `POOLSEC.dll`

## How to install in AutoCAD 2021

1. Open AutoCAD 2021
2. Type `NETLOAD` and press Enter
3. Browse and select `POOLSEC.dll`
4. Type `POOLSEC` and press Enter
5. Enter pool parameters when prompted

## Requirements
- AutoCAD 2021 (64-bit)
- .NET Framework 4.8 (included with Windows 10/11)

## Build locally (requires AutoCAD SDK references)
```powershell
dotnet restore
dotnet build -c Release
```

## Repository structure
```
POOLSEC/
├── POOLSEC.cs          # Main source code
├── POOLSEC.csproj      # Project file (net48, x64)
├── ref-cad/            # AutoCAD reference assemblies
│   ├── AcMgd.dll
│   ├── AcDbMgd.dll
│   └── AcCoreMgd.dll
├── .github/workflows/  # CI/CD
│   └── build.yml
└── README.md
```
