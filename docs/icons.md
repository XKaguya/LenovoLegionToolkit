# WPF UI Icons Reference

This project uses **WPF UI 2.1.0** with **Fluent System Icons v1.1.210**.

## Full Icon List

- **Browse all icons (visual):** https://github.com/microsoft/fluentui-system-icons
- **Full `SymbolRegular` enum (2.1.0):** https://github.com/lepoco/wpfui/blob/2.1.0/src/Wpf.Ui/Common/SymbolRegular.cs
- **Full `SymbolFilled` enum (2.1.0):** https://github.com/lepoco/wpfui/blob/2.1.0/src/Wpf.Ui/Common/SymbolFilled.cs

---

## Naming Convention

```
[IconName][Size]
```

Examples: `Settings24`, `Battery024`, `ArrowClockwise32`, `Desktop16`

**Common sizes in this project:** `20`, `24` (dominant), `16`, `28`, `32`  
**Available sizes in the enum:** `12`, `16`, `20`, `24`, `28`, `32`, `48`

> Not every icon exists at every size. Always verify in the enum before using.

---

## Usage

### XAML — `Icon` property (on `CardControl`, `Button`, etc.)
```xml
<custom:CardControl Icon="Settings24" />
<wpfui:Button Icon="Checkmark24" />
```

### XAML — `SymbolIcon` control
```xml
<wpfui:SymbolIcon Symbol="Warning24" />
<wpfui:SymbolIcon Symbol="Circle16" Filled="True" FontSize="12" />
```

### C# — `SymbolRegular` enum
```csharp
using Wpf.Ui.Common;

card.Icon = SymbolRegular.Keyboard24;
icon.Symbol = SymbolRegular.BatteryCharge24;
```

---

## Icons Used in This Project

All icons below are confirmed to exist in `SymbolRegular` for wpfui 2.1.0.

### General UI
| Icon | Usage |
|------|-------|
| `Checkmark24` | Confirm, select all, apply |
| `CheckmarkCircle24` | Compatibility check flag, success state |
| `Dismiss24` | Close, kill process |
| `Warning24` | Warnings, conflict indicators |
| `Info24` | Info cards |
| `ErrorCircle24` | Unsupported device dialog |
| `ChevronDown24` | Expandable sections |
| `Open24` | Open external link |
| `Search24` | Filter/search inputs |
| `Circle16` | Status dot indicator |

### Navigation & Windows
| Icon | Usage |
|------|-------|
| `Window24` | OSD overlay style setting |
| `Desktop16` | Display/screen indicator |
| `Desktop24` | GPU working mode flag |
| `LocalLanguage24` | Language selector |

### Hardware & Sensors
| Icon | Usage |
|------|-------|
| `Gauge24` | Status window performance |
| `DeveloperBoard24` | CPU/GPU sensor rows |
| `PointScan24` | CPU overclocking |
| `Games24` | GPU overclocking |
| `ArrowAutofitContent24` | Global offset control |
| `Keyboard24` | Special keys, RGB/spectrum keyboard flags |

### Battery
| Icon | Usage |
|------|-------|
| `Battery024` – `Battery1024` | Battery level indicators (0–100%) |
| `BatteryCharge24` | Charging state |
| `BatterySaver24` | Conservation mode |
| `Battery624` | Static battery icon in status window |

### Actions & Editing
| Icon | Usage |
|------|-------|
| `Edit24` | Rename action in context menu |
| `Delete24` | Delete action in context menu |
| `ArrowClockwise32` | Refresh warranty info |
| `ArrowCounterclockwise24` | Reset |
| `ArrowSync24` | Update indicator, sync |
| `ArrowDownload24` | Disable update checker flag |
| `ArrowImport24` | Import/load |
| `ArrowExport24` | Export/save |

### Visibility
| Icon | Usage |
|------|-------|
| `Eye24` | Reveal serial number, show hidden keys |
| `EyeOff24` | Hide serial number, hide hidden keys |

### Misc / Settings Flags
| Icon | Usage |
|------|-------|
| `Subtract24` | Start minimized flag |
| `Add24` | Increment button |
| `Bug24` | Debug mode flag |
| `Globe24` | Proxy URL flag |
| `Person24` | Proxy username flag |
| `Password24` | Proxy password flag |
| `Certificate24` | Allow all certs flag |
| `Lightbulb24` | Disable Lenovo lighting flag |
| `Color24` | Disable spectrum/RGB keyboard flag |
| `LockClosed24` | OSD lock setting |
| `Timer24` | OSD refresh interval |
| `Target24` | OSD snap threshold |
| `TooltipQuote24` | Disable tray tooltip flag |
| `AppsListDetail24` | Trace logging flag |
