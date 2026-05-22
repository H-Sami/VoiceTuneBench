# VoiceTune Bench

VoiceTune Bench helps you preview simple voice presets before you copy the settings into ReaEQ and ReaComp.

It is made for regular speech: podcasting, streaming, voice chat, meetings, and dialogue. It is not for singing, music mixing, mastering, or creative vocal effects.

<img width="1201" height="733" alt="image" src="https://github.com/user-attachments/assets/575d08f3-6850-4171-9796-ac5051349ab8" />


## What It Does

- Load a local voice sample.
- Record a quick voice sample in the app.
- Pick a speech preset.
- Listen to the original audio.
- Listen to the preset preview.
- Watch the frequency graph while audio plays.
- Copy the ReaEQ and ReaComp settings.

VoiceTune Bench does not record your microphone, upload audio, use accounts, or send telemetry.

## What You Need

To actually use a preset outside VoiceTune Bench, you need:

1. ReaEQ and ReaComp.
2. A program that can host VST audio plugins.

ReaEQ and ReaComp are part of the free ReaPlugs VST FX Suite from Cockos:

https://www.reaper.fm/reaplugs/

You can host those plugins in a DAW or audio mixer/plugin host. Examples include REAPER and Elgato Wave Link 3.

Elgato Wave Link downloads:

https://www.elgato.com/us/en/s/downloads

## How To Use

1. Install ReaPlugs VST FX Suite.
2. Open your DAW or plugin host.
3. Add ReaEQ first.
4. Add ReaComp after ReaEQ.
5. Open VoiceTune Bench.
6. Load a short voice sample, or click Record and speak normally.
7. Try the presets and listen.
8. Pick the preset that sounds best on your voice.
9. Click Copy in VoiceTune Bench.
10. Enter those settings into ReaEQ and ReaComp.

Suggested chain:

```text
Microphone -> ReaEQ -> ReaComp
```

## Build From Source

Requirements:

- Windows 10/11.
- Visual Studio with WinUI 3 / Windows App SDK tooling.
- .NET SDK.

Build:

```powershell
dotnet restore .\VoiceTuneBench.slnx
dotnet build .\src\VoiceTuneBench.WinUI\VoiceTuneBench.WinUI.csproj -c Debug -p:Platform=x64
```

Run:

```powershell
.\src\VoiceTuneBench.WinUI\bin\x64\Debug\net8.0-windows10.0.19041.0\VoiceTuneBench.exe
```

Test:

```powershell
dotnet test .\tests\VoiceTuneBench.Core.Tests\VoiceTuneBench.Core.Tests.csproj -c Debug -p:Platform=x64
```

Build an MSIX package after restore:

```powershell
powershell -ExecutionPolicy Bypass -File .\eng\build-msix.ps1
```

The MSIX output is much smaller and cleaner than the old release ZIP. Sign it with a trusted code-signing certificate before publishing it publicly.

## Privacy

VoiceTune Bench is local-only. Your audio stays on your PC.
