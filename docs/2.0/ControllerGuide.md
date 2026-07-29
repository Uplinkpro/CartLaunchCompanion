# Cart Launch Companion 2.0 Controller Guide

## Action model

Views react to shared launcher actions rather than controller-specific button numbers.

```text
NavigateLeft
NavigateRight
NavigateUp
NavigateDown
Confirm
Back
Trailer
Options
```

## Primary flow

### Home

| Input | Action |
|---|---|
| Left / Right | Select game |
| Up / Down | Navigate when multiple rows are present |
| A | Open selected game's metadata |
| B | Open exit confirmation |

### Metadata

| Input | Action |
|---|---|
| A | Launch game |
| B | Return Home |
| X | Play or pause trailer |
| Directional input | Navigate visible actions when needed |

### Exit confirmation

| Input | Action |
|---|---|
| A | Exit Cart Launch Companion |
| B | Cancel and return Home |

## Keyboard equivalents

| Controller | Keyboard |
|---|---|
| Directional input | Arrow keys |
| A | Enter |
| B | Escape |
| X | X or Space when contextually appropriate |

## Mouse behavior

- Hovering a game selects it.
- Clicking a game on Home opens Metadata.
- Visible actions are fully clickable.
- Mouse focus and controller focus must remain synchronized.

## Input-service architecture

SDL is the preferred cross-platform controller backend.

Avalonia handles:

- Focus
- Visual states
- Pointer input
- Keyboard input
- View navigation

The controller service handles:

- Device discovery
- Hot-plugging
- Button state
- Stick thresholds
- Input repeat
- Controller type
- Optional vibration

## Navigation rules

- Navigation must be deterministic.
- Focus must never disappear.
- Returning to Home restores the previously selected game.
- Holding a direction uses controlled repeat timing.
- Input is debounced during page transitions.
- Decorative animation never delays Confirm or Back.
