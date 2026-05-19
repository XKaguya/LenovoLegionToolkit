# Custom Controls Reference

This project defines its own controls on top of WPF UI 2.1.0. There are two namespaces to know:

```xml
xmlns:custom="clr-namespace:LenovoLegionToolkit.WPF.Controls.Custom"
xmlns:controls="clr-namespace:LenovoLegionToolkit.WPF.Controls"
```

---

## `custom:CardControl`

A content card with an optional icon, header, and right-side content. Wraps `Wpf.Ui.Controls.CardControl` with compact mode support.

**Key properties:** `Icon` (SymbolRegular name), `Header` (usually a `CardHeaderControl`), `Click`

```xml
<custom:CardControl Margin="0,0,0,8" Icon="Info24">
    <custom:CardControl.Header>
        <controls:CardHeaderControl Title="Title here" Subtitle="Optional subtitle" />
    </custom:CardControl.Header>
    <wpfui:ToggleSwitch x:Name="_myToggle" />
</custom:CardControl>
```

> For a clickable card with no right-side content, use `CardAction` instead.

---

## `custom:CardAction`

A fully clickable card row with an optional icon and chevron. Wraps `Wpf.Ui.Controls.CardAction`.

**Key properties:** `Icon`, `Content`, `IsChevronVisible` (default `true`), `Click`

```xml
<custom:CardAction Margin="0,0,0,4" Icon="Keyboard24" Click="MyAction_Click">
    <controls:CardHeaderControl Title="Action label" />
</custom:CardAction>
```

---

## `custom:CardExpander`

An expandable card section. Wraps `Wpf.Ui.Controls.CardExpander`.

**Key properties:** `Header`, `Icon`, `IsExpanded`

```xml
<custom:CardExpander Icon="Settings24">
    <custom:CardExpander.Header>
        <controls:CardHeaderControl Title="Section title" />
    </custom:CardExpander.Header>
    <!-- expanded content -->
</custom:CardExpander>
```

---

## `custom:Badge`

A small badge/pill indicator. Wraps `Wpf.Ui.Controls.Badge`.

```xml
<custom:Badge Content="NEW" />
```

---

## `controls:CardHeaderControl`

The standard header block used inside cards. Defined in `LenovoLegionToolkit.WPF.Controls`.

**Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `Title` | `string` | Primary bold label (required) |
| `Subtitle` | `string` | Secondary smaller text below title |
| `SubtitleToolTip` | `string?` | Tooltip shown on subtitle |
| `Info` | `string` | Blue info line with icon (accent color) |
| `Warning` | `string` | Yellow warning line with icon |
| `Error` | `string` | Red error line with icon |
| `Success` | `string` | Green success line with icon |
| `Accessory` | `UIElement?` | Element placed on the right column of the header |

> In compact mode, `Subtitle`/`Info`/`Warning`/`Error`/`Success` are hidden and shown as a tooltip instead.

```xml
<controls:CardHeaderControl
    Title="{x:Static resources:Resource.MyKey_Title}"
    Subtitle="{x:Static resources:Resource.MyKey_Message}"
    Warning="{Binding MyWarningText}" />
```

---

## `controls:LoadableControl`

Wraps any content and shows a `ProgressRing` spinner while loading.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsLoading` | `bool` | `true` | Shows spinner when `true` |
| `IsIndeterminate` | `bool` | `true` | Indeterminate vs. progress ring |
| `Progress` | `double` | — | Progress value when not indeterminate |
| `ContentVisibilityWhileLoading` | `Visibility` | `Hidden` | Whether content is hidden or collapsed during load |
| `IndicatorWidth/Height` | `double` | `48` | Spinner size |

```xml
<controls:LoadableControl x:Name="_list" IsLoading="True">
    <ItemsControl x:Name="_items" />
</controls:LoadableControl>
```

---

## `controls:SelectableControl`

A rubber-band (drag-to-select) selection overlay wrapping any content. Fires a `Selected` event with a `ContainsCenter` delegate to determine which child elements fall within the drawn rectangle.

Used in fan curve and spectrum keyboard editors.

---

## WPF UI base controls (used directly)

These come directly from `Wpf.Ui` and are used without a custom wrapper:

| Control | Namespace prefix | Notes |
|---------|-----------------|-------|
| `SymbolIcon` | `wpfui:` | Render a single icon glyph. Use `Symbol=` and optionally `Filled="True"` |
| `Button` | `wpfui:` | Has `Icon=` and `Appearance=` (`Primary`, `Secondary`, `Transparent`) |
| `ToggleSwitch` | `wpfui:` | Standard on/off toggle |
| `TitleBar` | `wpfui:` | Window title bar with WinUI-style controls |
| `Snackbar` | `wpfui:` | Toast notification bar |
| `DynamicScrollViewer` | `wpfui:` | ScrollViewer that adjusts scrollbar visibility dynamically |
| `NumberBox` | `wpfui:` | Numeric input with increment/decrement |
| `Hyperlink` | `wpfui:` | Clickable link, use `Tag=` for URL and handle `Click` |
