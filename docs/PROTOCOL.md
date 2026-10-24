# Compx configuration protocol

These are the notes CompxTray is built on. Unless a line says otherwise, everything here was confirmed on a **Svive Flow 7** (receiver `373B:11B3`, mouse firmware 4.04). Other Compx mice may differ.

## Transport

The receiver enumerates as a composite HID device. One of its collections is the configuration channel:

| | |
|---|---|
| Usage page / usage | `0xFF02` / `0x0002` |
| Report ID | `0x08` |
| Report size | 17 bytes including the report ID, both directions |
| Direction | Commands go out as output reports and replies come back as input reports with the same ID |

On Windows a plain `WriteFile` works. `HidD_SetOutputReport` is used as a fallback.

## Frame

```
offset  0     1        2       3      4      5       6 .. 15   16
        0x08  command  status  addr   addr   length  data      checksum
                               (hi)   (lo)
```

- `status` is 0 in requests. In replies, 0 means OK.
- `checksum` is chosen so that **the sum of all 17 bytes is `0x55`** (mod 256).
- A request with no arguments, e.g. battery, is `08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49`.

## Commands

| Command | Name | Reply |
|---|---|---|
| `0x03` | Online | `[6]` = 1 when the mouse is connected to the receiver |
| `0x04` | Battery | `[6]` percent, `[7]` charging flag, `[8..9]` millivolts (big-endian) |
| `0x07` | Write EEPROM | `addr`, `length` (1-10) and `data`; echoed back on success |
| `0x08` | Read EEPROM | `addr` and `length` (1-10); data returned at `[6..]` |
| `0x10` | CID / MID | model identifiers |
| `0x12` | Mouse firmware | `[6]` major, `[7]` minor |
| `0x1D` | Receiver firmware | not supported by the Flow 7 receiver (status 1) |

When the mouse is asleep, commands that need the mouse either time out or return a non-zero status. Moving the mouse wakes it.

## Settings memory

Single values are stored as **pairs `[value, 0x55 - value]`**, so a corrupt or unknown field can be detected before anything is written.

| Address | Length | Content |
|---|---|---|
| `0x0000` | 6 | polling code, stage count, active stage (three pairs) |
| `0x000A` | 2 | lift-off code (pair) |
| `0x000C` | 32 | DPI table: 8 stages × `[x, y, high bits, checksum]` |
| `0x002C` | 32 | stage colours: 8 × `[r, g, b, checksum]` |
| `0x0060` | 24 | button assignments: 6 × `[class, value, value, checksum]` |
| `0x00A9` | 10 | debounce ms, motion sync, sleep (10 s units), unknown, ripple control (five pairs) |
| `0x00B5` | 6 | sensor performance row |

### Polling rate codes

| Code | Rate |
|---|---|
| `0x08` | 125 Hz |
| `0x04` | 250 Hz |
| `0x02` | 500 Hz |
| `0x01` | 1000 Hz |
| `0x10` / `0x20` / `0x40` | 2000 / 4000 / 8000 Hz (other models; not offered for the Flow 7) |

### DPI encoding

The value is stored in 50 DPI steps: `count = dpi / 50 - 1`. The low 8 bits of `count` go in the x and y bytes. Bits 8-9 go in bits 2-3 of each axis nibble in the third byte (x in the low nibble, y in the high nibble). The fourth byte makes the four bytes sum to `0x55`.

| DPI | Stored |
|---|---|
| 400 | `07 07 00 47` |
| 800 | `0F 0F 00 37` |
| 1600 | `1F 1F 00 17` |
| 3200 | `3F 3F 00 D7` |
| 30000 | `57 57 88 1F` |

### Behaviour confirmed on the Flow 7

- **Active stage:** stored zero-based. The mouse updates it in memory every time the DPI button is pressed.
- **DPI changes:** new DPI values take effect immediately, without switching stage.
- **Stage count:** a higher stage count is used straight away by the DPI button.
- **Lift-off:** code 1 stops tracking closer to the surface than code 2.
- **Persistence:** all settings survive switching the mouse off and on, so they are stored in non-volatile memory.
- **Receiver writes:** writes through the 2.4 GHz receiver work. A cable is not needed.

### Defaults read from a new Flow 7

1000 Hz, four DPI stages (400 / 800 / 1600 / 3200), active stage index 2, lift-off code 1, debounce 8 ms, motion sync on, sleep after 60 s.

## Writing safely

CompxTray follows these rules:

1. Read the whole configuration and refuse to continue if any pair fails its checksum or holds an unknown value.
2. Write only the fields that changed, keeping unknown bytes in a row as they were.
3. Read every written address back and report any difference.
4. Save a backup of the original rows the first time a mouse is read, so they can be restored.
5. Allow writes only on models whose layout has been verified.

## Sources

The frame format and command list are documented by [atkctl](https://github.com/xiehuc/atkctl). The A9-family memory map by [OpenMouse mouse-protocol](https://github.com/OpenMouse-Project/mouse-protocol). Both were checked against a real Svive Flow 7 before CompxTray wrote anything.
