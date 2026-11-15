# DiceChaosDisplay — Website & Product Feature Reference

## 1. Product Positioning & Copy-Ready Blurbs

- **One-Line Tagline Options:**
  - *"Zero-lag 4K/8K battlemaps, animated video scenes, and true 1-inch physical miniature scale for tabletop TVs."*
  - *"The native Windows DM Studio built specifically for playing D&D and TTRPGs with real miniatures on a TV."*
  - *"Bring your tabletop TV to life—instant Fog of War, exact 1-inch mini calibration, and zero browser lag."*
- **Short Website Summary:**
  - **DiceChaosDisplay** is a native, offline-first Windows desktop application designed for Dungeon Masters and Game Masters who run in-person tabletop RPGs on a flat-screen TV, digital gaming table, or projector. It pairs a private **DM Studio** control screen with a borderless **Player TV Display**, delivering smooth 60 FPS 4K/8K static and animated battlemaps, instant feathered Fog of War, 1-click room reveals, true 1-inch physical miniature scaling, and glowing under-miniature condition and spell overlays—with no subscriptions, no accounts, and no web-browser stutter.
- **Core Design Philosophy:**
  - **Hybrid Tabletop First (Not a Bloated Online VTT):** Built around real dice and physical miniatures sitting on TV glass—enhancing at-the-table play instead of forcing players onto laptops or character-sheet servers.
  - **100% Local, Offline & Private:** Runs natively on your machine with local files (`.dicechaos` campaigns, images, `.uvtt` bundles, and videos). Zero cloud lock-in, zero internet requirement on game night, and zero recurring fees.

---

## 2. Dual-Screen DM Studio & Player TV Broadcast System

- **Private Stage-and-Push Workflow:**
  - Load, inspect, edit Fog of War, place secret notes, or align grids privately on the **DM Studio** screen (`Staged on DM Only`) while players continue viewing the current active map on the TV.
  - Broadcast the staged map to the table whenever you are ready via **Push to TV** (`Ctrl+Enter` or double-clicking a scene card), or enable the **Auto-Push** checkbox for instant 1-click scene switching.
  - Truthful broadcast status indicators across the top HUD and scene cards clearly distinguish **`● LIVE ON TV`**, **`◌ QUEUED (TV OFF)`**, and **`● STAGED ON DM ONLY`**.
- **True 1-Click Player TV Power Toggle (`F12`):**
  - Turn the Player TV window **ON** or **OFF** anytime via the stateful top-bar toggle button (`TV Off` ⇄ `● TV Live` / `● TV Preview`), the `TV Display` menu, **`F12`**, or pressing **`Esc`** while the TV window is focused.
  - Turning the TV **OFF** cleanly closes the player display back to the Windows desktop and halts secondary viewport rendering while preserving your exact queued scene, camera framing, handouts, and blackout state.
  - Pushing scenes or staging handouts while the TV is **OFF** updates the background queue quietly without unexpectedly popping open the TV window.
  - Launches on secondary TVs with `ShowActivated = false` so the DM never loses keyboard focus mid-encounter.
- **Smart Single-Monitor Prep Mode & HDMI Hotplug Detection:**
  - Automatically detects if the selected display is the same monitor hosting the DM Studio window (e.g., prepping on a laptop away from the game table) and opens a **resizable 16:9 Floating Preview Window** (`● TV Preview`) instead of trapping your screen behind a borderless overlay (with a manual *Force Fullscreen* menu override if ever needed).
  - Listens to live Windows display changes (`DisplaySettingsChanged`) so plugging in or unplugging an HDMI TV mid-session automatically refreshes connected monitors and adapts window placement.
- **Spoiler-Safe Blackout & Custom Campaign Standby Screen (`B`):**
  - Instant **`B`** hotkey toggles a **100% pure pitch-black** blackout screen (or an optional custom **Campaign Splash Image + Banner Title**) to hide map transitions or pause between encounters.
  - **Automatic Spoiler Guard:** Toggling the Player TV **ON** before any scene has been pushed automatically engages Blackout so players never see an unprepped flash.
- **Tabletop TV & Projector Rotation (`0° / 90° / 180° / 270°`):**
  - Rotate the Player TV output in 90-degree increments independently of Windows display settings—ideal for TVs laid flat inside custom gaming tables or players seated across the opposite side of the table.

---

## 3. True 1-Inch Physical Miniature Scale & Display Calibration

- **Per-Monitor V2 High-DPI Precision:**
  - Queries native Win32 physical pixel bounds and per-monitor DPI scaling (`100%`, `125%`, `150%`, `200%+`) so 1 tactical grid square on the map renders at **1.00 real physical inch** (`25.4 mm`) on the TV glass regardless of Windows UI scaling.
- **Two Calibration Workflows:**
  - **Instant TV Diagonal Size Presets:** Enter your TV's diagonal measurement or click 1-tap presets (`32"`, `43"`, `50"`, `55"`, `65"`) to compute exact hardware Pixels-Per-Inch (PPI) automatically.
  - **Live On-TV 1-Inch Physical Ruler:** Project an interactive calibration square/circle directly onto the TV glass, place a real 1-inch miniature base on the screen, drag the slider until they match 1:1, and lock the scale permanently to your TV profile.
- **Three TV Camera Synchronization Modes:**
  - **Lock TV to 1-Inch Physical Mini Scale:** Locks TV zoom to exact 1-inch miniature scale while letting you pan which section of a large dungeon is currently shown on the TV.
  - **Mirror DM Pan & Zoom:** Smoothly mirrors the DM Studio camera center and aspect-adjusted zoom on the TV.
  - **Independent TV Camera:** Decouples DM zoom/pan completely from the TV camera.
- **Effortless TV Framing from Any Tool:**
  - Grab and drag the **`✥ TV VIEW`** pill badge on the DM canvas from *any* active tool (even while painting Fog of War), use **`Ctrl + Middle-Drag`**, or press **`C`** to snap the TV camera to your mouse cursor.
  - Even while the Player TV is **OFF**, a dashed **`✥ TV VIEW (OFF • PRE-AIM)`** guide sized to your target TV's resolution lets you pre-frame the starting room during prep.

---

## 4. Zero-Lag 4K / 8K Battlemap, Video & Universal VTT Engine

- **Tiled 4K / 8K GPU Pyramid Architecture:**
  - Decodes high-resolution battlemaps asynchronously on background threads into a fast overview layer plus frozen **`2048×2048` high-res GPU tiles**.
  - Real-time viewport frustum culling draws high-resolution tiles only when zoomed in and within view, maintaining fluid 60 FPS pan/zoom on massive 4K and 8K maps.
- **Looping Animated Video Battlemaps:**
  - Native support for **`MP4`**, **`WEBM`**, **`MKV`**, **`AVI`**, and **`MOV`** animated battlemaps (via integrated LibVLC offscreen rendering).
  - **Airspace-Free Compositing:** Video frames render seamlessly *underneath* WPF tactical grids, glowing miniature rings, spell AoE templates, sonar pings, and Fog of War—with dedicated Play/Pause, Mute, and Volume controls.
- **Universal VTT One-Step Importer (`.dd2vtt` / `.uvtt` / `.df2vtt`):**
  - Directly imports Universal VTT files exported from **Dungeondraft**, **Dungeon Alchemist**, **Arkenforge**, and other mapmakers.
  - Automatically extracts embedded high-res map imagery, sets exact `PixelsPerCell` grid dimensions without manual calibration, and renders optional **DM-Only Cyan Wall & Amber Door/Portal Guides** to assist with accurate Fog of War reveals.
- **Broad Static Image & Drag-and-Drop Support:**
  - Supports **`PNG`**, **`JPG` / `JPEG`**, **`WEBP`**, and **`BMP`** maps—drop single or multiple maps (or a `.dicechaos` campaign file) straight from Windows Explorer onto the studio window.
- **Smart Multi-Scene Memory Hygiene:**
  - Keeps your active **Staged DM Scene** and **Live TV Scene** hydrated in memory for instant response while automatically dehydrating inactive background scenes down to lightweight thumbnails until selected.

---

## 5. High-Speed Fog of War & 1-Click Dungeon Rooms

- **Sub-Millisecond Zero-Copy Fog Engine:**
  - Powered by SkiaSharp `SKBlendMode.DstOut` alpha masking mapped directly into a WPF `WriteableBitmap` back-buffer (~1 ms per brush step even on 8K maps).
  - **Adjustable Soft Feathered Edges (`0%–100%`):** Smooth radial/gaussian edge feathering gives revealed rooms organic, atmospheric shadows rather than harsh pixelated blocks.
- **Three Interactive Fog Painting Tools:**
  - **Fog Brush (`2`):** Smooth freehand reveal/hide strokes with live cursor radius preview (`[` / `]` or `Shift + Mouse Wheel` to resize on the fly).
  - **Fog Box (`3`):** Click-and-drag rectangular reveals or hides for clean dungeon rooms and hallways.
  - **Fog Polygon (`4`):** Click corner-by-corner to carve out angled caverns, cones of vision, or irregular chambers (`Enter`, `Double-Click`, or `Right-Click` to finish).
  - **Instant Inverse Painting:** Hold **`Right-Drag`** at any time to paint the opposite fog operation (re-hide fog while in Reveal mode) without switching buttons.
- **Pre-Pped 1-Click Fog Rooms (`5`):**
  - Outline and name every room of a dungeon during session prep (`Entry Hall`, `Guard Barracks`, `Vault`, etc.).
  - During live play, clickable **`LOCKED • Room Name`** / **`OPEN • Room Name`** badges float on the DM map (and in the Layers sidebar)—click any badge once to instantly reveal or re-fog that entire room on the Player TV.
- **Dual-Opacity DM vs. Player Fog:**
  - Players always see **100% solid opaque black fog** on the TV (zero accidental transparency leaks), while the DM adjusts a dedicated **DM Fog Opacity** slider (`0%–95%`) or toggles DM fog preview off completely to read room details underneath.

---

## 6. Physical-Miniature Tactical Overlay Suite

- **Under-Mini Glowing Condition & Aura Rings (`T`):**
  - Purpose-built to shine *underneath* real physical miniatures placed on the TV screen!
  - **Clutter-Free Glow-Only Default:** Places clean neon rings with zero floating text boxes by default, plus 1-click D&D 5e presets: **`Glow Only`**, **`Concentrating`**, **`Bloodied`**, **`Blessed`**, **`Hexed`**, **`Poisoned`**, **`Prone`**, **`Aura 10ft`**, and **`Spirit 15ft`**.
  - **Creature Footprints & Auras:** Supports **`1×1` Medium**, **`2×2` Large**, **`3×3` Huge**, and **`4×4` Gargantuan** bases (up to `10×10`), optional outer aura rings (`10ft`, `15ft`, `30ft`, up to `120ft`), optional grid-center snapping, per-ring Player TV visibility, and auto-numbered digital **Monster Tokens** (`Goblin 1`, `Goblin 2`, etc.) when running horde encounters without enough physical minis.
- **5e Spell Area-of-Effect (AoE) Templates (`A`):**
  - Drop accurate **`Sphere`**, **`5e Cone`**, **`Cube`**, and **`Line`** spell overlays (`5ft`–`500ft` sizes, `10ft`/`15ft`/`20ft`/`30ft`/`60ft` quick buttons, custom line widths, and elemental color swatches for Fire, Cold, Lightning, Necrotic, Acid/Poison, and Radiant).
  - Rotate cones, cubes, and lines live via `Shift + Mouse Wheel` or the inspector angle slider (`-180°` to `+180°`).
  - **Clean Hover-Only Size Labels:** Overlapping AoE zones stay visually clean by showing size pills only when hovered or selected (with an optional *Always Show Labels* toggle) and 1-click **Clear All AoEs**.
- **Interactive 3×3 Visual Grid Matcher (`6`) & Square/Hex Grids:**
  - Align baked-in map grids in seconds: drag the **White Top-Left** handle to a grid intersection and drag the **Gold Bottom-Right** handle across a `3×3` block of squares to auto-calculate exact `PixelsPerCell` and X/Y offsets.
  - Supports **Square**, **Hex Flat-Top**, and **Hex Pointy-Top** tactical grids, `±0.1 px` / `±1 px` fine-nudge buttons, adjustable opacity, and White/Black/Gold/Cyan grid colors.
- **Multi-Rule Distance Ruler (`9`):**
  - Click-and-drag measurement showing both **Feet** and **Squares/Cells** using **`5-5-5` (Standard 5e)**, **`5-10-5` (Variant/3.5e/PF Alternating Diagonals)**, or **`Euclidean` (True Geometric)** rules, with a toggle to show or hide the ruler on the Player TV.
- **Sonar Table Ping (`8` or `Alt + Click` from Any Tool):**
  - **`Alt + Left-Click`** anywhere on the map triggers an animated, expanding neon sonar beacon on both the DM screen and Player TV—point out doors, traps, or targets without reaching across players' miniatures.
- **Freehand Tactical Pencil (`7`) & Secret DM-Only Note Pins (`N`):**
  - Sketch live hazards, rubble, or spell walls in multiple colors/thicknesses on the shared map.
  - Drop **Secret DM Pins** (`🔒 NEVER ON TV`) with custom map codes (`1`, `A2`, `Trap`), titles, color tags, and multi-line encounter/DC notes visible exclusively on the DM Studio canvas.

---

## 7. Handout Presenter, Campaign Files & Studio UX

- **Pre-Staged Handout Gallery & Ad-Hoc Flash Presenter:**
  - Pre-load NPC portraits, monster illustrations, handwritten letters, and puzzle handouts directly into your campaign's **Handouts** tab with visual thumbnails.
  - Click **Project on TV** to display crisp fullscreen framed artwork over the map on the Player TV (auto-rotated to match your TV's table orientation!), or flash an ad-hoc image file on the fly and click **Dismiss** to return instantly to combat.
- **Portable `.dicechaos` Campaign Saves & Recent Files:**
  - Single-file `.dicechaos` JSON campaign format persists your scene list, compressed Fog of War masks, 1-click room polygons, grid calibration, miniature rings, spell AoEs, pencil sketches, secret DM pins, handout gallery, and TV calibration profile.
  - Includes `Ctrl+S` Save, `Ctrl+Shift+S` Save As, unsaved-changes exit protection, and a dynamically populated **Recent Campaigns** menu.
- **Unified Multi-Domain Undo / Redo (`Ctrl+Z` / `Ctrl+Y`):**
  - Per-scene undo/redo stack seamlessly tracks and reverses Fog strokes, Reveal/Hide All, 1-Click Room toggles, Spell AoE placements & bulk clears, Mini Rings, Pencil drawings, and DM Pins in exact chronological order.
- **Purpose-Built Dark DM Studio Ergonomics:**
  - Custom low-glare midnight-slate UI designed for dimly lit game rooms, featuring crisp vector tool iconography, a dynamic **Contextual Tool Options Strip** above the canvas, a **4-Tab Collapsible Inspector** (`Layers`, `Handouts`, `Grid & Media`, `TV & 1"`), a collapsible **Bottom Visual Scene Strip** with right-click scene management (`Duplicate`, `Reorder`, `Reset Fog`, `Push`), right-click canvas object menus, **`Tab` Focus Mode**, **`F11` Fullscreen DM Studio**, and an in-app **`F1` Keyboard & Mouse Cheat-Sheet**.

---

## 8. Technical Summary (For Download / Specs Section)

- **Platform:** Native Windows (`x64`, Windows 10 / Windows 11) with Per-Monitor V2 High-DPI awareness.
- **Runtime & Rendering Stack:** `.NET 10` WPF hardware-accelerated `MatrixTransform` viewport + `SkiaSharp` zero-copy alpha compositing + `LibVLCSharp` offscreen video decoding.
- **Supported Map & Media Formats:**
  - **Static Maps:** `.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp` (tested up to `8192×8192` 8K+ resolutions)
  - **Universal VTT Bundles:** `.dd2vtt`, `.uvtt`, `.df2vtt` (auto-grid + wall/portal extraction)
  - **Animated Video Maps:** `.mp4`, `.webm`, `.mkv`, `.avi`, `.mov`
  - **Campaign Project:** `.dicechaos`
