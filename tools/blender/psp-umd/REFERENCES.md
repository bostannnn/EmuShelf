# PSP-1000 and UMD: tracing references

`build_psp_umd.py` builds both models from measurements taken off these photographs. They are
tracing references only: none of them is embedded in, or shipped with, the models. Local copies
live outside the repo in the owner's `Documents/Everything/PSP_1000/references/` folder.

## How the photos were measured

Each photo was resampled in Blender's Python (numpy) into a millimetre grid and read off at
7–14 px/mm. The outlines in the script (`FACE_R`, `BODY_R`, `FRONT_BACK_R`, `SHOULDER_OUT`) and
every control position are those readings.

- **Front** (`PSP-1000_face_20150930.jpg`): horizontal scale from the 170 mm body width
  (18.76 px/mm). The camera looks slightly down at the face, so the vertical scale was set separately
  (16.3 px/mm) and checked: the face buttons, D-pad ring and analog nub all come out round to
  within 2–3 %. The screen measures 99 × 54 mm, matching the 4.3-inch 480 × 272 panel.
- **Rear** (`PSP-1000 - Rear Side.jpg`): near-orthographic; one scale (10.08 px/mm) from the
  170 mm width. Door 75.6 × 72.6 mm, full height; ring 53 mm outside, 39 mm inside.
- **Edges and depth** (top, bottom, left and right photos): perspective shots, used for the order
  and proportion of the depth layers (grille chamfer, black front shell, silver band about 7–13 mm
  deep, rear shell, grips) and for port positions along the edges. Total depth follows the published
  23 mm.
- **UMD** (`Psp_umd_disk.jpg`, the loaded-UMD and open-bay photos): outline (semicircular end
  r 32 round the disc centre, straight sides, flat end with r 9 corners), the 60 mm disc window, and the
  loading orientation: round end first, label facing the door, flat edge left at the top.

The model is compared against the front and rear photos by rendering an orthographic view at the
same mm-per-pixel scale and setting the two side by side.

## Sources

| Photo | Author | Licence | Use |
| --- | --- | --- | --- |
| [PSP-1000 face 20150930.jpg](https://commons.wikimedia.org/wiki/File:PSP-1000_face_20150930.jpg) | MiNe | CC BY 2.0 | Front outline, every front control |
| [PSP-1000 - Rear Side.jpg](https://commons.wikimedia.org/wiki/File:PSP-1000_-_Rear_Side.jpg) | Alexander Davronov | CC BY-SA 4.0 | Rear outline, door, grips |
| [PSP Top View.JPG](https://commons.wikimedia.org/wiki/File:PSP_Top_View.JPG), [Bottom](https://commons.wikimedia.org/wiki/File:PSP_Bottom_View.JPG), [Left](https://commons.wikimedia.org/wiki/File:PSP_Left_View.JPG), [Right](https://commons.wikimedia.org/wiki/File:PSP_Right_View.JPG) | Punkidow | Public domain | Edge layers, ports, switches |
| [Psp-back.jpg](https://commons.wikimedia.org/wiki/File:Psp-back.jpg), [Psp-front2.jpg](https://commons.wikimedia.org/wiki/File:Psp-front2.jpg) | Asim18 | Public domain | Top edge from behind, shoulder keys |
| [Psp-1000.jpg](https://commons.wikimedia.org/wiki/File:Psp-1000.jpg), [Sony-PSP-1000-Body.png](https://commons.wikimedia.org/wiki/File:Sony-PSP-1000-Body.png) | Evan-Amos | Public domain | General form |
| [UMD slot at PSP-1000 rear 20150930.jpg](https://commons.wikimedia.org/wiki/File:UMD_slot_at_PSP-1000_rear_20150930.jpg) | MiNe | CC BY 2.0 | Door edge and ring detail |
| [Psp umd disk.jpg](https://commons.wikimedia.org/wiki/File:Psp_umd_disk.jpg) | Kryganas | CC BY-SA 4.0 | UMD outline and window |
| [iFixit guide 1786](https://www.ifixit.com/Guide/PSP+1000+UMD+Door+Replacement/1786), steps 12–18 | Henry Lee and contributors | CC BY-NC-SA | Door hinge, carrier, spindle position |

## Deliberately left out

Manufacturer logos and wordmarks: the PlayStation mark, SONY, the PSP wordmarks on the face, door and
UMD, UMD, MagicGate, and the printed key legends HOME, SELECT, START, VOL, POWER and HOLD. The door
keeps its plain brushed ring with no wordmark inside it.
