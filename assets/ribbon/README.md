# Ribbon icon sources

Edit the original SVGs in `svg/`. Each command has a separately drawn 16 px and
32 px optical master. Small masters remove detail instead of scaling the large
drawing. All canvases have transparent backgrounds; shapes stay inside them.

| Command ID | Symbol |
| --- | --- |
| `view-range` | Section planes and vertical range indicator |
| `inspect-view` | Plan outline and magnifying glass |
| `room-dimensions` | Rectangular room and architectural dimension ticks |
| `open-agent` | Chat pane with speech-bubble tail |
| `check-updates` | Two circular update arrows |

The masters use explicit palette tokens, with no CSS or external resources:

| Role | Master / light | Dark |
| --- | --- | --- |
| Neutral geometry | `#52606D` | `#D6DEE8` |
| Action / emphasis | `#006EAF` | `#70C5FF` |

Export from the repository root on Windows; WPF is included with Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/export-ribbon-icons.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/export-ribbon-icons.ps1 -Check
```

The script produces 40 committed PNGs in `png/`. Names follow
`<id>.<light|dark>.<16|32>[@2x].png`. A `.16.png` file is 16 physical pixels at
96 DPI; `.16@2x.png` is 32 physical pixels at 192 DPI, retaining a 16 DIP logical
size. The corresponding `.32` files are 32/64 physical pixels. Both themes and
scales are derived from the corresponding optical SVG master.

`-Check` renders in memory and compares exact PNG bytes, dimensions, and DPI.
It fails on missing or stale outputs; generation also refuses unexpected output
files rather than removing them automatically. Both sizes are required for every
command. Export is deterministic for the same Windows/WPF renderer; another
renderer version may require deliberate regeneration and visual review.

The exporter supports only flat `path`, `rect`, `line`, `circle`, and `polyline`
elements, plus plain-text `title`/`desc` metadata. Shapes require explicit `fill`
and `stroke` using the two palette tokens or `none`, and a positive
`stroke-width` for visible strokes. It supports `butt`/`square`/`round` line caps,
`miter`/`bevel`/`round` joins, unitless invariant-culture numbers, and standard SVG
path commands. SVG defaults for nonzero fill and miter limit 4 are preserved.
The root dimensions and viewBox must match the optical size. Unsupported tags,
attributes, CSS, transforms, external references, DTDs, and drawings outside the
canvas fail explicitly. Convert editor-added groups or shapes into this bounded
format before export; do not substitute a silent approximation.
