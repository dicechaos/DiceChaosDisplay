<p align="center">
  <img src="Assets/AppIcon-256.png" width="96" height="96" alt="Dice Chaos Display D20 Tabletop Emblem" />
</p>

<h1 align="center">Dice Chaos Display</h1>

<p align="center">
  <strong>Offline 4K/8K Battlemap Studio, True 1-Inch Physical Miniature Calibration &amp; Zero-Spoiler 2nd-Screen Tabletop TV Display</strong>
</p>

<p align="center">
  <a href="https://github.com/dicechaos/DiceChaosDisplay/releases"><img src="https://img.shields.io/github/v/release/dicechaos/DiceChaosDisplay?include_prereleases&style=flat-square&label=download" alt="Latest Release" /></a>
  <a href="https://github.com/dicechaos/DiceChaosDisplay/actions"><img src="https://img.shields.io/github/actions/workflow/status/dicechaos/DiceChaosDisplay/ci.yml?branch=main&style=flat-square&label=build" alt="Build Status" /></a>
  <a href="https://dicechaos.com"><img src="https://img.shields.io/badge/website-dicechaos.com-4f46e5?style=flat-square" alt="dicechaos.com" /></a>
  <a href="https://github.com/dicechaos/DiceChaosConnections"><img src="https://img.shields.io/badge/companion%20app-Dice%20Chaos%20Connections-0ea5e9?style=flat-square" alt="Dice Chaos Connections" /></a>
  <a href="https://github.com/dicechaos/DiceChaosMasters"><img src="https://img.shields.io/badge/companion%20app-Dice%20Chaos%20Masters-8b5cf6?style=flat-square" alt="Dice Chaos Masters" /></a>
  <a href="./LICENSE"><img src="https://img.shields.io/github/license/dicechaos/DiceChaosDisplay?style=flat-square" alt="MIT License" /></a>
</p>

---

**Dice Chaos Display** is a local-first Windows desktop studio built by **[Dice Chaos](https://dicechaos.com)** for Dungeon Masters, Game Masters, and in-person tabletop groups who love rolling real handcrafted polyhedral dice and moving real physical miniatures around the table. Pair a private **DM Studio** screen with a borderless **Player TV Display** to run 4K/8K battlemaps, looping video scenes, sub-millisecond feathered Fog of War, 1-click dungeon room reveals, and exact **1.00-inch (`25.4 mm`) physical miniature scaling** at locked 60 FPS—with zero browser lag, zero accounts, and zero subscriptions.

> **Part of the Dice Chaos Tabletop Suite:** Built side-by-side with **[Dice Chaos Connections](https://github.com/dicechaos/DiceChaosConnections)** (offline TTRPG campaign mind-map, conspiracy board & zero-spoiler 2nd-screen lore display) and **[Dice Chaos Masters](https://github.com/dicechaos/DiceChaosMasters)** (free offline parametric 3D polyhedral dice master, matched insert blank & MSLA resin support studio). Forge your custom dice masters at **[dicechaos.com](https://dicechaos.com)** with *Masters*, map out your world, NPCs, and plot webs in *Connections* (or export High-DPI Map Posters straight into *Display*), and roll real handmade resin dice right under your miniatures in *Display*. Found a bug or have a feature request? [Open an issue on GitHub](https://github.com/dicechaos/DiceChaosDisplay/issues) or visit **[dicechaos.com](https://dicechaos.com)**.

---

## Download & Install

1. Grab the latest self-contained portable archive (`DiceChaosDisplay-v1.0.0-win-x64.zip`) from **[GitHub Releases](https://github.com/dicechaos/DiceChaosDisplay/releases/latest)**.
2. Extract the `.zip` into any folder and launch **`DiceChaosDisplay.exe`**—the `.NET 10` runtime and `LibVLC` video codecs are bundled inside, so no extra installations are required.

> **Windows SmartScreen Note:** Because builds are not yet signed with a commercial EV certificate, Windows may display *"Windows protected your PC"* on first launch. Click **More info** -> **Run anyway**.

---

## Key Features

### Zero-Spoiler Dual-Screen DM Studio & Smart TV Toggle
- **Private Stage-and-Push Workflow (`Ctrl+Enter`):** Load maps, paint Fog of War, align grids, and drop secret trap notes privately on your DM screen (`Staged on DM Only`) before broadcasting to the table—or toggle **Auto-Push** for instant 1-click scene switching.
- **1-Click Player TV Power Toggle (`F12`):** Turn the borderless Player TV window **ON** or **OFF** anytime via the header status pill (`TV Off` ⇄ `● TV Live` / `● TV Preview`), **`F12`**, or **`Esc`** on the TV—without stealing keyboard focus from your DM Studio.
- **Smart Single-Monitor Prep Mode:** Automatically detects when you are prepping on a single laptop/desktop monitor and opens a resizable **16:9 Floating Preview Window** instead of trapping your screen behind a fullscreen overlay, then upgrades seamlessly when you plug in an HDMI TV.
- **Spoiler-Safe Blackout & Custom Splash Art (`B`):** Instant pure pitch-black TV blackout (or custom campaign standby artwork + title banner), with automatic blackout protection if the TV is turned on before a map is pushed.

### True 1-Inch Physical Miniature Calibration
- **Per-Monitor V2 High-DPI Precision:** Accounts for native panel resolution and Windows DPI scaling (`100%`–`200%+`) so 1 tactical map square equals **1.00 real physical inch (`25.4 mm`)** on the TV glass underneath real miniature bases.
- **Diagonal Presets + Live On-TV Physical Ruler:** Tap your TV's diagonal size (`32"`, `43"`, `50"`, `55"`, `65"`, or custom inches) **or** project live 1-inch calibration shapes onto the glass, set a real miniature base on the screen, and lock exact hardware PPI in seconds.
- **Tabletop Orientation & Effortless Framing:** Rotate the Player TV output `0° / 90° / 180° / 270°` for flat-mounted table TVs, and drag the **`✥ TV VIEW`** canvas pill (or press **`C`**) to pan the TV camera from *any* active tool.

### 60 FPS 4K / 8K Battlemap, Video & Universal VTT Engine
- **Tiled 4K / 8K GPU Pyramid:** Asynchronous background decoding into frozen `2048×2048` frustum-culled GPU tiles keeps pan and zoom silky-smooth on massive 8K battlemaps—even High-DPI Map Posters exported from **[Dice Chaos Connections](https://github.com/dicechaos/DiceChaosConnections)**.
- **Looping Video Battlemaps (`MP4` / `WEBM` / `MKV`):** Airspace-free animated battlemaps render *underneath* tactical grids, glowing miniature rings, spell AoE templates, and Fog of War, complete with Play/Pause, Mute, and Volume controls.
- **1-Step Universal VTT Import (`.dd2vtt` / `.uvtt` / `.df2vtt`):** Directly imports maps from Dungeondraft, Dungeon Alchemist, and Arkenforge with automatic `PixelsPerCell` grid alignment and optional DM-only cyan wall & amber door guides.

### Sub-Millisecond Feathered Fog of War & 1-Click Dungeon Rooms
- **Zero-Copy SkiaSharp Fog Engine:** ~1 ms feathered `Reveal` and `Hide` strokes with **Fog Brush** (`2`), **Fog Box** (`3`), and **Fog Polygon** (`4`), plus instant `Right-Drag` inverse painting and adjustable DM fog translucency (`100%` solid black on the Player TV).
- **Pre-Pped 1-Click Fog Rooms (`5`):** Outline and name dungeon chambers ahead of session night, then click floating **`LOCKED` / `OPEN`** room badges on the DM canvas to reveal or re-fog entire rooms in a single click.

### Under-Miniature Glow Rings, 5e Spell AoEs & Table Handouts
- **Under-Mini Condition & Aura Rings (`T`):** Clean glow-only neon rings built to shine beneath real physical minis, with 1-click 5e presets (*Concentrating*, *Bloodied*, *Blessed*, *Hexed*, *Poisoned*, *Prone*, *10ft / 15ft / 30ft Auras*), creature footprints (`1×1` Medium through `4×4` Gargantuan), and auto-numbered digital monster tokens.
- **Rotatable 5e Spell AoEs (`A`) & Distance Ruler (`9`):** Drop `Sphere`, `5e Cone`, `Cube`, and `Line` templates (`Shift+Wheel` to rotate, clutter-free hover labels) and measure distances with `5-5-5`, `5-10-5`, or `Euclidean` diagonal rules across Square and Hex grids (aligned in seconds via the **3×3 Visual Grid Matcher**).
- **Sonar Pings, Secret DM Pins & Handout Gallery:** `Alt+Click` from any tool to ping both screens, drop **`🔒 Secret DM Note Pins`** (`NEVER ON TV`), and project pre-staged NPC, monster, or item artwork onto the rotated Player TV in one click.

👉 See **[FEATURES.md](./FEATURES.md)** for the exhaustive capability reference.

---

## Quick Shortcuts

| Shortcut / Gesture | Action |
| :--- | :--- |
| **`F12`** (or **`Esc`** on TV) | Toggle Player TV Screen **ON / OFF** |
| **`Ctrl+Enter`** | Push privately staged DM scene to Live TV |
| **`B`** | Toggle TV Blackout / Campaign Standby Splash |
| **`Middle-Drag` / `Space+Drag`** | Pan DM map from any tool (`Mouse Wheel` to zoom) |
| **`C` · Drag `✥ TV VIEW` Pill** | Center TV at cursor · Drag TV camera directly from any tool |
| **`2` / `3` / `4` / `5`** | Fog Brush / Fog Box / Fog Polygon / 1-Click Room Prep |
| **`[` / `]` · `Shift+Wheel`** | Decrease / Increase Fog Brush radius (or rotate selected AoE) |
| **`Right-Drag` (in Fog Tools)** | Paint inverse fog on the fly (Hide while Revealing) |
| **`6` / `7` / `8` / `9`** | 3×3 Grid Matcher / Pencil / Sonar Ping / 5e Distance Ruler |
| **`A` / `T` / `N`** | 5e Spell AoE / Under-Mini Glow Ring / Secret DM Pin |
| **`Alt+Click`** | Trigger animated Sonar Ping on DM & TV from any tool |
| **`Ctrl+S` / `Ctrl+Shift+S`** | Save `.dicechaos` campaign / Save Campaign As… |
| **`Ctrl+Z` / `Ctrl+Y`** | Unified Undo / Redo (Fog, Rooms, AoEs, Rings, Sketches, Pins) |
| **`Tab` / `F11` / `F1`** | Focus Mode (hide sidebars) / Fullscreen DM Studio / Cheat-Sheet |

---

## Development & Building From Source

Requires the **[.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** on Windows (`x64`).

```powershell
# Run automated unit & 8K stress tests
dotnet test Tests\DiceChaosDisplay.Tests.csproj

# Launch Release DM Studio locally
dotnet run --project DiceChaosDisplay.csproj -c Release

# Package self-contained Windows x64 release folder
dotnet publish DiceChaosDisplay.csproj -c Release -r win-x64 --self-contained true -o publish\DiceChaosDisplay-win-x64
```

---

## Links & Feedback

- **Handmade Dice & Tabletop Gear:** [dicechaos.com](https://dicechaos.com)
- **Companion Campaign & Conspiracy Studio:** [Dice Chaos Connections](https://github.com/dicechaos/DiceChaosConnections)
- **Companion 3D Dice Master & MSLA CAD Studio:** [Dice Chaos Masters](https://github.com/dicechaos/DiceChaosMasters)
- **Bug Reports & Feature Requests:** [GitHub Issues](https://github.com/dicechaos/DiceChaosDisplay/issues)
- **Third-Party Notices:** Uses [SkiaSharp](https://github.com/mono/SkiaSharp) (MIT) and [LibVLCSharp / VideoLAN LibVLC](https://www.videolan.org/vlc/libvlc.html) (LGPL-2.1, dynamically linked in `libvlc/`).
- **License:** MIT License — Copyright © 2026 dicechaos (Gus). See [LICENSE](./LICENSE).
