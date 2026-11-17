"""Builds EmuShelf's PSP-1000 and UMD models, and a preview of the launch/exit motion.

Outlines and positions were measured from reference photographs rectified to millimetres
(tools/blender/psp-umd/REFERENCES.md). The photographs are tracing references only: nothing from
them is embedded in the models. No manufacturer logos or wordmarks are modelled.

Run inside Blender:  exec(open(".../build_psp_umd.py").read()); build_all()

Frame (Blender, millimetres in the code, metres in the scene):
    X right, Z up (the PSP's top edge), -Y out of the screen / out of the UMD's label.
    `t` is depth behind the PSP's faceplate; Blender y = t + FACE_Y.
The glTF exporter's +Y-up conversion turns this into EmuShelf's canonical shell space
(Y up, X right, +Z out of the face), so the shell orientation matrix is identity.

Parts (one glTF node each, so the app can move them):
    PSP              empty, the handheld's root
      PSP_Body       every static part
      PSP_Screen     the LCD, UV 0..1 over the active area
      PSP_DoorHinge  empty on the UMD door's hinge line; rotate about local X (negative opens)
        PSP_Door     the rear UMD door with its carrier rails
    UMD              disc centre at the origin, flat edge down (-Z), label on -Y
      UMD_Disc       the disc inside the shell; label UV 0..1 over its 60 mm box
"""

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

MM = 0.001
SCENE_NAME = "EmuShelf | PSP + UMD"

# --- Measured outline (mm, x right / z up, origin at the device centre; right half) ---------
# Front photo: MiNe, "PSP-1000 face 20150930.jpg"; rear: A. Davronov, "PSP-1000 - Rear Side".
# The body silhouette; the top corners are notched where the clear shoulder keys sit.
BODY_R = [(0.0, 37.0), (55.5, 37.0), (57.6, 34.6), (59.8, 32.6), (72.8, 31.4), (77.8, 27.4),
          (81.0, 21.4), (83.3, 12.0), (84.6, 0.0), (84.4, -9.0), (83.5, -18.5), (82.0, -23.5),
          (80.0, -25.6), (75.7, -32.3), (67.0, -35.9), (55.7, -37.0), (0.0, -37.0)]

SCREEN_C = (0.0, 5.9)
GLASS_HW, GLASS_HH = 49.5, 26.5
LCD_HW, LCD_HH = 47.5, 26.6                   # 95 x 53 active area

DPAD_C, DPAD_DISH = (-65.5, 7.2), 12.6
STICK_C, STICK_R = (-66.4, -15.0), 5.8
FACE_C, FACE_SP, FACE_BR = (65.6, 7.6), 9.65, 4.4
KEY_ROW_Z = -26.3

# Depth stack (t, mm behind the faceplate), read off the edge photos against the 23 mm spec.
FACE_Y = -10.0
R_FRONT = 4.6             # roll of the glossy face into the sides
T_FRONT = 8.0             # glossy front shell ends
BAND_INSET = 0.7          # the silver band sits inside the shells
T_BAND = 12.4             # silver band ends, rear shell begins
T_REAR = 19.4             # rear surface beside the door
GRIP_BULGE = 3.6          # grips swell to 23 mm at the ends
R_REAR = 5.0              # rear edge roll
DOOR_X, DOOR_Z = 37.8, 36.4
T_BAY = 13.2              # floor of the UMD bay
T_DOOR_IN, T_DOOR_OUT = 17.2, 19.9
HINGE_T, HINGE_Z = 16.0, -36.4
DOOR_OPEN_DEG = -30.0

# UMD (65 x 64 x 4.2): semicircular end r 32 round the disc centre, straight sides, flat end
# with r 9 corners 33 mm below the centre.
UMD_R, UMD_FLAT, UMD_CORNER, UMD_T = 32.0, -33.0, 9.0, 4.2
UMD_SEAT_T, UMD_SEAT_Z = 15.0, -0.5           # disc centre when loaded (round end down)


def y_of(t):
    return t + FACE_Y


HINGE = (0.0, y_of(HINGE_T), HINGE_Z)
UMD_SEAT = (0.0, y_of(UMD_SEAT_T), UMD_SEAT_Z)

# --- Materials ------------------------------------------------------------------------------

MATERIALS = {
    "PSP Gloss Black":    dict(base=(0.006, 0.006, 0.008), rough=0.07, clearcoat=0.6),
    "PSP Grille":         dict(base=(1.0, 1.0, 1.0), rough=0.35, image="PSP grille dots"),
    "PSP Rear Black":     dict(base=(0.016, 0.016, 0.018), rough=0.48),
    "PSP Silver Band":    dict(base=(0.60, 0.61, 0.63), metal=0.85, rough=0.30),
    "PSP Silver Plate":   dict(base=(0.70, 0.71, 0.73), metal=0.9, rough=0.25),
    "PSP Shoulder Clear": dict(base=(0.30, 0.33, 0.37), rough=0.05, alpha=0.5),
    "PSP Key Black":      dict(base=(0.010, 0.010, 0.012), rough=0.09, clearcoat=0.4),
    "PSP Legend Grey":    dict(base=(0.55, 0.57, 0.60), rough=0.40),
    "PSP Recess":         dict(base=(0.003, 0.003, 0.004), rough=0.65),
    "PSP Groove":         dict(base=(0.001, 0.001, 0.001), rough=0.95),
    "PSP Glass":          dict(base=(0.010, 0.011, 0.013), rough=0.03),
    "PSP Screen":         dict(base=(0.0, 0.0, 0.0), rough=0.20,
                               emit=(0.16, 0.26, 0.50), emit_strength=0.0),
    "PSP Stick":          dict(base=(1.0, 1.0, 1.0), rough=0.60, image="PSP grille dots"),
    "PSP Steel":          dict(base=(0.55, 0.57, 0.60), metal=0.9, rough=0.30),
    "PSP Door Ring":      dict(base=(0.80, 0.81, 0.83), metal=1.0, rough=0.20),
    "PSP Yellow":         dict(base=(0.85, 0.62, 0.02), rough=0.35),
    "PSP LED":            dict(base=(0.04, 0.55, 0.14), rough=0.30,
                               emit=(0.10, 1.0, 0.30), emit_strength=3.0),
    "UMD Clear":          dict(base=(0.85, 0.88, 0.92), rough=0.03, alpha=0.10),
    "UMD Shell":          dict(base=(0.88, 0.89, 0.90), rough=0.25),
    "UMD Label":          dict(base=(1.0, 1.0, 1.0), rough=0.30, image="UMD label placeholder"),
    "UMD Disc":           dict(base=(0.82, 0.84, 0.88), metal=1.0, rough=0.08),
    "UMD Hub":            dict(base=(0.10, 0.10, 0.11), rough=0.40),
}


def _image(name, size, fn):
    img = bpy.data.images.get(name) or bpy.data.images.new(name, size, size, alpha=False)
    if tuple(img.size) != (size, size):
        img.scale(size, size)
    px = []
    for j in range(size):
        for i in range(size):
            px.extend((*fn((i + 0.5) / size, (j + 0.5) / size), 1.0))
    img.pixels.foreach_set(px)
    img.pack()
    return img


def _grille_dot(u, v):
    r = math.hypot(u - 0.5, v - 0.5)
    if r < 0.27:
        return (0.0, 0.0, 0.0)
    if r < 0.34:
        return (0.06, 0.06, 0.065)
    return (0.010, 0.010, 0.012)


def _umd_label(u, v):
    """Stand-in for a game's printed disc face, mapped over the disc's 60 x 60 mm box."""
    x, z = (u - 0.5) * 60.0, (v - 0.5) * 60.0
    r = math.hypot(x, z)
    if r < 11.0:
        return (0.94, 0.94, 0.93) if r > 2.5 else (0.75, 0.75, 0.75)
    k = (z + 30.0) / 60.0
    base = (0.12 + 0.50 * k, 0.16 + 0.22 * k, 0.32 - 0.10 * k)
    if 20.5 < r < 21.3:
        return (0.85, 0.80, 0.70)
    return base


def ensure_materials():
    for name, spec in MATERIALS.items():
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        out.location = (300, 0)
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(bsdf.outputs[0], out.inputs[0])
        base = (*spec.get("base", (0.8, 0.8, 0.8)), 1.0)
        bsdf.inputs["Base Color"].default_value = base
        bsdf.inputs["Metallic"].default_value = spec.get("metal", 0.0)
        bsdf.inputs["Roughness"].default_value = spec.get("rough", 0.5)
        if "clearcoat" in spec:
            for key in ("Coat Weight", "Clearcoat"):
                if key in bsdf.inputs:
                    bsdf.inputs[key].default_value = spec["clearcoat"]
                    break
        if "emit" in spec:
            bsdf.inputs["Emission Color"].default_value = (*spec["emit"], 1.0)
            bsdf.inputs["Emission Strength"].default_value = spec.get("emit_strength", 1.0)
        if "alpha" in spec:
            bsdf.inputs["Alpha"].default_value = spec["alpha"]
            try:
                mat.surface_render_method = "BLENDED"
            except AttributeError:
                mat.blend_method = "BLEND"
            mat.show_transparent_back = False
        if "image" in spec:
            tex = nt.nodes.new("ShaderNodeTexImage")
            tex.location = (-300, 0)
            if spec["image"] == "PSP grille dots":
                tex.image = _image(spec["image"], 32, _grille_dot)
            else:
                tex.image = _image(spec["image"], 512, _umd_label)
                tex.label = "Game disc label (replaced at runtime)"
            nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        mat.diffuse_color = base


# --- 2D helpers -----------------------------------------------------------------------------

def arc(cx, cz, r, a0, a1, seg):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * k / seg)),
             cz + r * math.sin(math.radians(a0 + (a1 - a0) * k / seg))) for k in range(seg + 1)]


def dedupe(pts, eps=1e-4):
    out = []
    for p in pts:
        if not out or math.dist(p, out[-1]) > eps:
            out.append(p)
    if len(out) > 2 and math.dist(out[0], out[-1]) <= eps:
        out.pop()
    return out


def rrect(hw, hh, r, cx=0.0, cz=0.0, seg=6):
    r = max(0.0, min(r, hw, hh))
    if r == 0.0:
        return [(cx + hw, cz - hh), (cx + hw, cz + hh), (cx - hw, cz + hh), (cx - hw, cz - hh)]
    pts = arc(cx + hw - r, cz + hh - r, r, 0, 90, seg)
    pts += arc(cx - hw + r, cz + hh - r, r, 90, 180, seg)
    pts += arc(cx - hw + r, cz - hh + r, r, 180, 270, seg)
    pts += arc(cx + hw - r, cz - hh + r, r, 270, 360, seg)
    return dedupe(pts)


def shield(cx, cz, w, h):
    """HOME / SELECT / START: flat top, straight sides, the lower edge one arc."""
    hw, hh = w / 2.0, h / 2.0
    side = cz + hh - 2.0
    sag = side - (cz - hh)
    pts = [(cx - hw + 0.7, cz + hh), (cx + hw - 0.7, cz + hh)]
    pts += arc(cx + hw - 0.7, cz + hh - 0.7, 0.7, 90, 0, 3)[1:]
    pts.append((cx + hw, side))
    for k in range(1, 18):
        s = k / 18.0
        pts.append((cx + hw - w * s, side - sag * math.sin(math.pi * s) ** 0.8))
    pts.append((cx - hw, side))
    pts += arc(cx - hw + 0.7, cz + hh - 0.7, 0.7, 180, 90, 3)[:-1]
    return dedupe(pts)


def circle(cx, cz, r, seg=32):
    return dedupe(arc(cx, cz, r, 0, 360, seg))


def rotated(pts, deg, cx=0.0, cz=0.0):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return [(cx + x * c - z * s, cz + x * s + z * c) for x, z in pts]


def mirror_closed(right_half):
    """Closes a right-half outline (top centre -> bottom centre) into a full CCW loop."""
    right = list(right_half)
    left = [(-x, z) for x, z in right[1:-1]]
    return dedupe(list(reversed(right)) + left)


def smooth_polyline(pts, closed=True, iters=2):
    for _ in range(iters):
        out = []
        n = len(pts)
        rng = range(n) if closed else range(n - 1)
        if not closed:
            out.append(pts[0])
        for i in rng:
            p, q = pts[i], pts[(i + 1) % n]
            out.append((0.75 * p[0] + 0.25 * q[0], 0.75 * p[1] + 0.25 * q[1]))
            out.append((0.25 * p[0] + 0.75 * q[0], 0.25 * p[1] + 0.75 * q[1]))
        if not closed:
            out.append(pts[-1])
        pts = out
    return pts


def resample(pts, n, closed=True):
    seg = []
    m = len(pts)
    total = 0.0
    rng = range(m) if closed else range(m - 1)
    for i in rng:
        a, b = pts[i], pts[(i + 1) % m]
        d = math.dist(a, b)
        seg.append((a, b, total, d))
        total += d
    out = []
    count = n if closed else n - 1
    for k in range(n):
        s = total * k / count
        for a, b, s0, d in seg:
            if s0 + d >= s - 1e-9:
                f = 0.0 if d == 0 else (s - s0) / d
                out.append((a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f))
                break
        else:
            out.append(seg[-1][1])
    return out


def offset_poly(pts, d):
    """Offsets a CCW polygon inward by d along the vertex bisectors (mitre-limited)."""
    n = len(pts)
    out = []
    for i in range(n):
        p0, p1, p2 = pts[i - 1], pts[i], pts[(i + 1) % n]
        e1 = (p1[0] - p0[0], p1[1] - p0[1])
        e2 = (p2[0] - p1[0], p2[1] - p1[1])
        l1, l2 = math.hypot(*e1) or 1.0, math.hypot(*e2) or 1.0
        n1 = (e1[1] / l1, -e1[0] / l1)            # outward normals for a CCW loop
        n2 = (e2[1] / l2, -e2[0] / l2)
        bx, bz = n1[0] + n2[0], n1[1] + n2[1]
        bl = math.hypot(bx, bz) or 1.0
        bx, bz = bx / bl, bz / bl
        scale = 1.0 / max(n1[0] * bx + n1[1] * bz, 0.6)
        out.append((p1[0] - bx * d * scale, p1[1] - bz * d * scale))
    return out


def scale_about(pts, c, s):
    return [(c[0] + (x - c[0]) * s, c[1] + (z - c[1]) * s) for x, z in pts]


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3.0 - 2.0 * t)


def _edge_z(poly, x, top):
    zs = []
    n = len(poly)
    for i in range(n):
        (x0, z0), (x1, z1) = poly[i], poly[(i + 1) % n]
        if (x0 - x) * (x1 - x) <= 0 and x0 != x1:
            zs.append(z0 + (z1 - z0) * (x - x0) / (x1 - x0))
    return max(zs) if top else min(zs)


BODY = resample(smooth_polyline(mirror_closed(BODY_R), iters=2), 360)
C_BODY = (0.0, 0.0)

# --- Mesh building --------------------------------------------------------------------------


class MeshBuilder:
    """Accumulates parts into one bmesh, in millimetres, with a material slot per name."""

    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.mats = []

    def mi(self, name):
        if name not in self.mats:
            self.mats.append(name)
        return self.mats.index(name)

    @staticmethod
    def _map(axis, a, b, h):
        if axis == "y":
            return Vector((a, h, b))
        if axis == "z":
            return Vector((a, b, h))
        return Vector((h, a, b))

    def loft(self, rings, mat, closed=True, cap_first=False, cap_last=False, mat_fn=None,
             out_from=None, smooth=True):
        """Quads between consecutive rings of 3D points, optional fan caps, oriented to face away
        from `out_from`."""
        bm = self.bm
        vr = [[bm.verts.new(p) for p in ring] for ring in rings]
        faces = []
        n = len(rings[0])
        last = n if closed else n - 1
        for i in range(len(vr) - 1):
            for k in range(last):
                j = (k + 1) % n
                f = bm.faces.new((vr[i][k], vr[i][j], vr[i + 1][j], vr[i + 1][k]))
                f.material_index = self.mi((mat_fn(i, k) if mat_fn else None) or mat)
                f.smooth = smooth
                faces.append(f)
        for do_cap, ring in ((cap_first, vr[0]), (cap_last, vr[-1])):
            if do_cap:
                c = sum((v.co for v in ring), Vector()) / len(ring)
                cv = bm.verts.new(c)
                for k in range(n):
                    f = bm.faces.new((cv, ring[k], ring[(k + 1) % n]))
                    f.material_index = self.mi(mat)
                    f.smooth = smooth
                    faces.append(f)
        if out_from is not None:
            score = 0.0
            for f in faces:
                f.normal_update()
                score += f.normal.dot(f.calc_center_median() - out_from) * f.calc_area()
            if score < 0:
                bmesh.ops.reverse_faces(bm, faces=faces)
        else:
            bmesh.ops.recalc_face_normals(bm, faces=faces)
        return faces

    def prism(self, pts, h0, h1, mat, axis="y", bev0=0.0, bev1=0.0, seg0=2, seg1=2):
        bm = self.bm
        pts = dedupe(pts)
        n = len(pts)
        v0 = [bm.verts.new(self._map(axis, a, b, h0)) for a, b in pts]
        v1 = [bm.verts.new(self._map(axis, a, b, h1)) for a, b in pts]
        faces = [bm.faces.new(v0), bm.faces.new(v1)]
        for i in range(n):
            j = (i + 1) % n
            faces.append(bm.faces.new((v0[i], v0[j], v1[j], v1[i])))
        idx = self.mi(mat)
        for f in faces:
            f.material_index = idx
            f.smooth = True
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        e0 = [bm.edges.get((v0[i], v0[(i + 1) % n])) for i in range(n)]
        e1 = [bm.edges.get((v1[i], v1[(i + 1) % n])) for i in range(n)]
        if bev0 > 0:
            bmesh.ops.bevel(bm, geom=e0, offset=bev0, segments=seg0, profile=0.5,
                            affect="EDGES", clamp_overlap=True)
        e1 = [e for e in e1 if e.is_valid]
        if bev1 > 0 and e1:
            bmesh.ops.bevel(bm, geom=e1, offset=bev1, segments=seg1, profile=0.5,
                            affect="EDGES", clamp_overlap=True)

    def box(self, x0, x1, y0, y1, z0, z1, mat, bev=0.0):
        self.prism([(x0, z0), (x1, z0), (x1, z1), (x0, z1)], y0, y1, mat,
                   bev0=bev, bev1=bev, seg0=1, seg1=1)

    def plate(self, pts, h, mat, axis="y", facing=-1, uv=None):
        bm = self.bm
        pts = dedupe(pts)
        verts = [bm.verts.new(self._map(axis, a, b, h)) for a, b in pts]
        f = bm.faces.new(verts)
        f.normal_update()
        if f.normal.dot(self._map(axis, 0, 0, facing)) < 0:
            f.normal_flip()
        f.material_index = self.mi(mat)
        f.smooth = False
        if uv:
            for loop in f.loops:
                co = loop.vert.co
                a, b = (co.x, co.z) if axis == "y" else ((co.x, co.y) if axis == "z" else (co.y, co.z))
                loop[self.uv].uv = uv(a, b)

    def ring(self, cx, cz, r0, r1, h, mat, axis="y", facing=-1, seg=40):
        bm = self.bm
        inner = [bm.verts.new(self._map(axis, *p, h)) for p in circle(cx, cz, r0, seg)]
        outer = [bm.verts.new(self._map(axis, *p, h)) for p in circle(cx, cz, r1, seg)]
        want = self._map(axis, 0, 0, facing)
        idx = self.mi(mat)
        for i in range(seg):
            j = (i + 1) % seg
            f = bm.faces.new((inner[i], inner[j], outer[j], outer[i]))
            f.normal_update()
            if f.normal.dot(want) < 0:
                f.normal_flip()
            f.material_index = idx

    def bar(self, cx, cz, length, width, deg, h0, h1, mat):
        self.prism(rotated(rrect(length / 2, width / 2, 0.0), deg, cx, cz), h0, h1, mat)

    def set_uv(self, faces, fn):
        for f in faces:
            for loop in f.loops:
                loop[self.uv].uv = fn(loop.vert.co)

    def to_object(self, name, collection, origin_mm=(0.0, 0.0, 0.0), sharp_deg=50.0):
        bm = self.bm
        bmesh.ops.translate(bm, vec=-Vector(origin_mm), verts=bm.verts)
        bmesh.ops.scale(bm, vec=(MM, MM, MM), verts=bm.verts)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for m in self.mats:
            me.materials.append(bpy.data.materials[m])
        try:
            me.set_sharp_from_angle(angle=math.radians(sharp_deg))
        except AttributeError:
            pass
        ob = bpy.data.objects.new(name, me)
        ob.location = Vector(origin_mm) * MM
        collection.objects.link(ob)
        return ob


def P(x, z, t):
    return Vector((x, y_of(t), z))


def fillet_rings(outline, t_edge, radius, depth_fn, J=9, inward=True):
    """Rings that roll a flat surface at depth_fn(x, z) (measured from t_edge, where the side
    wall ends) round into the wall of `outline`."""
    rings = []
    for j in range(J + 1):
        a = 0.5 * math.pi * j / J
        off = offset_poly(outline, radius * (1.0 - math.cos(a))) if inward else outline
        ring = []
        for (x, z), (ox, oz) in zip(off, outline):
            h = depth_fn(ox, oz)
            ring.append(P(x, z, t_edge + (h - t_edge) * math.sin(a)))
        rings.append(ring)
    return rings


def rolled_solid(b, outline, t0, t1, mat, r0=0.0, r1=0.0, depth1=None, dome=None, J=9,
                 mat_fn=None, out_from=None):
    """A closed solid: flat cap at t0, side wall, and a surface at t1 rolled into the wall with
    radius r1 (and r0 at t0). `dome(x, z)` replaces the flat t1 surface with a height field."""
    inner_c = C_BODY
    h1 = dome or (lambda x, z: t1)
    d = 1.0 if t1 > t0 else -1.0          # direction from the cap towards the rolled surface
    rings = []
    # cap at t0 (flat), rolled with r0
    if r0 > 0:
        for j in range(J, -1, -1):
            a = 0.5 * math.pi * j / J
            off = offset_poly(outline, r0 * (1.0 - math.cos(a)))
            rings.append([P(x, z, t0 + d * r0 * (1.0 - math.sin(a))) for x, z in off])
    else:
        rings.append([P(x, z, t0) for x, z in outline])
    # side wall: from the end of the t0 roll to the start of the t1 roll
    rings.append([P(x, z, h1(x, z) - d * r1) for x, z in outline])
    # t1 roll
    for j in range(1, J + 1):
        a = 0.5 * math.pi * j / J
        off = offset_poly(outline, r1 * (1.0 - math.cos(a)))
        rings.append([P(x, z, h1(ox, oz) - d * r1 + d * r1 * math.sin(a))
                      for (x, z), (ox, oz) in zip(off, outline)])
    # t1 surface: shrink the rolled ring toward the centre, following the dome
    top = offset_poly(outline, r1)
    for s in (0.85, 0.7, 0.55, 0.4, 0.25, 0.12):
        rings.append([P(x, z, h1(x, z)) for x, z in scale_about(top, inner_c, s)])
    faces = b.loft(rings, mat, cap_first=True, cap_last=True, mat_fn=mat_fn,
                   out_from=out_from or P(inner_c[0], inner_c[1], (t0 + t1) / 2))
    return faces


# --- PSP shell ------------------------------------------------------------------------------

def build_front_shell(b):
    """Glossy faceplate: the full silhouette, its face rolled into the sides with R_FRONT."""
    J = 10

    def mat(i, k):
        # ring index i counts from the back cap; the front roll is the last J+... rings.
        return None

    faces = rolled_solid(b, BODY, T_FRONT, 0.0, "PSP Gloss Black", r0=0.5, r1=R_FRONT, J=J,
                         out_from=P(0.0, 0.0, T_FRONT / 2))
    # speaker grille: the part of the front roll along the top edge between the shoulders
    grille = b.mi("PSP Grille")
    tagged = []
    for f in faces:
        c = f.calc_center_median()
        t = c.y - FACE_Y
        if 0.25 < t < R_FRONT - 0.4 and c.z > 30.5 and abs(c.x) < 55.0 and f.normal.z > 0.2:
            f.material_index = grille
            tagged.append(f)
    b.set_uv(tagged, lambda co: (co.x / 1.25, (co.y - FACE_Y) * 1.2 / 1.25))


def build_band(b):
    outline = offset_poly(BODY, BAND_INSET)
    rolled_solid(b, outline, T_FRONT - 0.6, T_BAND + 0.6, "PSP Silver Band", r0=0.35, r1=0.35,
                 J=3, out_from=P(0.0, 0.0, (T_FRONT + T_BAND) / 2))


def rear_height(x, z):
    ax = abs(x)
    grip = smoothstep(DOOR_X + 0.6, DOOR_X + 16.0, ax)
    return T_REAR + GRIP_BULGE * grip * (1.0 - 0.30 * ((z + 1.0) / 37.0) ** 2)


def build_rear_shell(b):
    rolled_solid(b, BODY, T_BAND - 0.6, T_REAR, "PSP Rear Black", r0=0.4, r1=R_REAR,
                 dome=rear_height, J=10, out_from=P(0.0, 0.0, (T_BAND + T_REAR) / 2))


def cut_channel(ob, collection):
    """Boolean-cuts the UMD door channel through the rear shell."""
    cutter = bpy.data.objects.new("channel cutter", bpy.data.meshes.new("channel cutter"))
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(2 * DOOR_X * MM, 40 * MM, 100 * MM), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(0.0, y_of(T_BAY + 20.0) * 1.0, 0.0), verts=bm.verts)
    bm.to_mesh(cutter.data)
    bm.free()
    collection.objects.link(cutter)
    mod = ob.modifiers.new("channel", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cutter
    mod.solver = "EXACT"
    depsgraph = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(depsgraph))
    old = ob.data
    ob.modifiers.remove(mod)
    ob.data = me
    me.name = old.name
    bpy.data.meshes.remove(old)
    bpy.data.objects.remove(cutter, do_unlink=True)
    try:
        me.set_sharp_from_angle(angle=math.radians(50.0))
    except AttributeError:
        pass


def build_shoulder(b, sign):
    """Clear L/R key: fills the notch at the top corner, wraps the end down to ~z 19, and stands
    about 1.2 mm proud of the body."""
    seg = [p for p in BODY_R if 55.0 <= p[0] and p[1] >= 19.0]        # notch -> corner -> end
    seg = [(55.5, 37.0)] + [p for p in seg if p[0] > 55.5]
    outer = smooth_polyline([(56.0, 36.9), (70.0, 36.9)] + [(x + 0.6, z + 0.6) for x, z in seg[2:]],
                            closed=False, iters=2)
    inner = smooth_polyline([(x - 0.9, z - 0.9) for x, z in reversed(seg[1:])] + [(56.0, 34.0)],
                            closed=False, iters=1)
    poly = dedupe(outer + inner)
    if sign < 0:
        poly = [(-x, z) for x, z in reversed(poly)]
    b.prism(poly, y_of(3.6), y_of(13.2), "PSP Shoulder Clear", bev0=0.6, bev1=0.6, seg0=3, seg1=3)


def build_front_details(b):
    tf = y_of(0.0)
    # screen glass, flush, with a hairline step
    gx, gz = SCREEN_C
    b.prism(rrect(GLASS_HW, GLASS_HH, 1.2, gx, gz), tf + 0.4, tf - 0.10, "PSP Glass", bev1=0.1, seg1=1)

    # D-pad: shallow circular dish and four chunky keys
    dx, dz = DPAD_C
    b.plate(circle(dx, dz, DPAD_DISH, 72), tf - 0.03, "PSP Recess")
    b.ring(dx, dz, DPAD_DISH - 0.05, DPAD_DISH + 0.3, tf - 0.04, "PSP Groove", seg=72)
    key = smooth_polyline([(2.2, 0.0), (5.6, 3.6), (10.0, 3.6), (11.3, 2.4), (11.3, -2.4),
                           (10.0, -3.6), (5.6, -3.6)], closed=True, iters=1)
    arrow = [(9.3, 0.0), (7.5, 1.45), (7.5, -1.45)]
    top = tf - 2.6
    for deg in (0, 90, 180, 270):
        b.prism(rotated(key, deg, dx, dz), tf + 0.3, top, "PSP Key Black", bev1=1.1, seg1=4)
        pts = rotated(arrow, deg, dx, dz)
        for i in range(3):
            (x0, z0), (x1, z1) = pts[i], pts[(i + 1) % 3]
            b.bar((x0 + x1) / 2, (z0 + z1) / 2, math.dist(pts[i], pts[(i + 1) % 3]) + 0.25, 0.28,
                  math.degrees(math.atan2(z1 - z0, x1 - x0)), top + 0.05, top - 0.12, "PSP Legend Grey")

    # analog nub: knurled disc in a shallow collar
    ax, az = STICK_C
    b.ring(ax, az, STICK_R + 0.1, STICK_R + 0.8, tf - 0.02, "PSP Groove", seg=48)
    b.prism(circle(ax, az, STICK_R, 48), tf + 0.3, tf - 1.9, "PSP Key Black", bev1=1.0, seg1=3)
    b.plate(circle(ax, az, STICK_R - 1.05, 40), tf - 1.92, "PSP Stick",
            uv=lambda a, c: (a / 0.75, c / 0.75))

    # face buttons: domed caps inside the thin ring
    fx, fz = FACE_C
    b.ring(fx, fz, 11.35, 11.6, tf - 0.02, "PSP Groove", seg=64)
    top = tf - 2.4
    centres = {"tri": (fx, fz + FACE_SP), "cir": (fx + FACE_SP, fz),
               "crs": (fx, fz - FACE_SP), "sqr": (fx - FACE_SP, fz)}
    for (cx, cz) in centres.values():
        b.ring(cx, cz, FACE_BR + 0.05, FACE_BR + 0.45, tf - 0.03, "PSP Groove", seg=40)
        b.prism(circle(cx, cz, FACE_BR, 40), tf + 0.3, top, "PSP Key Black", bev1=1.3, seg1=4)
    lt0, lt1 = top + 0.05, top - 0.12
    cx, cz = centres["tri"]
    tri = [(cx + 2.1 * math.cos(math.radians(a)), cz - 0.25 + 2.1 * math.sin(math.radians(a)))
           for a in (90, 210, 330)]
    for i in range(3):
        (x0, z0), (x1, z1) = tri[i], tri[(i + 1) % 3]
        b.bar((x0 + x1) / 2, (z0 + z1) / 2, math.dist(tri[i], tri[(i + 1) % 3]) + 0.35, 0.36,
              math.degrees(math.atan2(z1 - z0, x1 - x0)), lt0, lt1, "PSP Legend Grey")
    cx, cz = centres["cir"]
    b.ring(cx, cz, 1.65, 2.05, lt1 - 0.01, "PSP Legend Grey", seg=32)
    cx, cz = centres["crs"]
    for deg in (45, -45):
        b.bar(cx, cz, 4.0, 0.36, deg, lt0, lt1, "PSP Legend Grey")
    cx, cz = centres["sqr"]
    for (ox, oz, deg) in ((0, 1.55, 0), (0, -1.55, 0), (1.55, 0, 90), (-1.55, 0, 90)):
        b.bar(cx + ox, cz + oz, 3.45, 0.36, deg, lt0, lt1, "PSP Legend Grey")

    # system keys along the lower edge
    kz = KEY_ROW_Z
    ktop = tf - 1.2
    for x, w in ((-49.1, 9.6), (36.8, 10.0), (47.9, 10.0)):
        b.prism(shield(x, kz + 0.2, w, 4.6), tf + 0.3, ktop, "PSP Key Black", bev1=0.5, seg1=2)
    for x in (-36.4, -24.7, 18.1, 25.9):
        b.prism(circle(x, kz, 2.7, 28), tf + 0.3, ktop, "PSP Key Black", bev1=0.6, seg1=2)
    b.bar(-36.4, kz, 1.5, 0.3, 0, ktop + 0.04, ktop - 0.04, "PSP Legend Grey")
    b.bar(-24.7, kz, 1.5, 0.3, 0, ktop + 0.04, ktop - 0.04, "PSP Legend Grey")
    b.bar(-24.7, kz, 1.5, 0.3, 90, ktop + 0.04, ktop - 0.04, "PSP Legend Grey")
    for (ox, oz, deg, ln) in ((0, 0.55, 0, 1.6), (0, -0.55, 0, 1.6), (0.8, 0, 90, 1.1), (-0.8, 0, 90, 1.1)):
        b.bar(18.1 + ox, kz + oz, ln, 0.26, deg, ktop + 0.04, ktop - 0.04, "PSP Legend Grey")
    b.plate(circle(25.5, kz - 0.5, 0.5, 12), ktop - 0.05, "PSP Legend Grey")
    b.bar(25.95, kz + 0.2, 1.5, 0.26, 90, ktop + 0.04, ktop - 0.04, "PSP Legend Grey")

    # speaker slots and indicator LEDs
    for sx in (-57.2, 57.2):
        b.prism(rrect(1.9, 0.7, 0.65, sx, -21.2), tf + 0.2, tf - 0.03, "PSP Recess")
    for (x, z, mat) in ((-81.1, -11.2, "PSP Recess"), (-78.6, -18.7, "PSP Recess"),
                        (80.7, -11.2, "PSP LED"), (77.7, -19.0, "PSP Recess")):
        b.prism(circle(x, z, 0.55, 12), tf + 0.2, tf - 0.05, mat)


def build_edge_details(b):
    zt = _edge_z(BODY, 0.0, True)
    zb = _edge_z(BODY, 0.0, False)
    bt0, bt1 = y_of(T_FRONT + 0.2), y_of(T_BAND - 0.2)
    # top: silver connector plate with mini-USB and two accessory screws, IR window, OPEN latch
    b.box(-27.5, 19.7, bt0, bt1, zt - 1.2, zt + 0.30, "PSP Silver Plate", bev=0.25)
    b.box(-8.4, -0.4, bt0 + 0.0008, bt1 - 0.0008, zt - 0.9, zt + 0.36, "PSP Recess", bev=0.3)
    for x in (15.9, -23.2):
        b.prism(circle(x, (bt0 + bt1) / 2, 1.2, 20), zt - 0.9, zt + 0.36, "PSP Recess", axis="z")
    b.box(-47.9, -37.3, bt0 + 0.0006, bt1 - 0.0006, zt - 1.2, zt + 0.05, "PSP Glass", bev=0.6)
    b.box(27.5, 37.3, bt0, bt1, zt - 1.2, zt + 0.42, "PSP Silver Plate", bev=0.4)
    for k in range(5):
        b.box(33.0 + k * 0.9, 33.35 + k * 0.9, bt0 + 0.0005, bt1 - 0.0005, zt + 0.4, zt + 0.52, "PSP Recess")
    # bottom: headset jack + remote contacts, DC-in, centre screw, two slots
    xj = -72.2
    zj = _edge_z(BODY, xj, False)
    b.prism(circle(xj, (bt0 + bt1) / 2, 1.9, 24), zj + 1.2, zj - 0.3, "PSP Recess", axis="z")
    b.ring(xj, (bt0 + bt1) / 2, 1.9, 2.5, zj - 0.31, "PSP Silver Plate", axis="z", facing=-1, seg=24)
    b.box(-68.5, -61.5, bt0 + 0.0008, bt1 - 0.0008, zb + 1.0, _edge_z(BODY, -65.0, False) - 0.2,
          "PSP Recess", bev=0.2)
    xd = 66.0
    zd = _edge_z(BODY, xd, False)
    b.box(xd - 2.6, xd + 2.6, y_of(3.0), y_of(9.5), zd + 1.2, zd - 0.35, "PSP Yellow", bev=0.4)
    b.prism(circle(xd, y_of(6.2), 1.3, 20), zd + 0.5, zd - 0.40, "PSP Recess", axis="z")
    b.prism(circle(0.0, (bt0 + bt1) / 2, 1.1, 16), zb + 0.6, zb - 0.3, "PSP Silver Plate", axis="z")
    for x in (-50.4, 50.4):
        b.prism(rrect(1.6, 0.6, 0.55, x, (bt0 + bt1) / 2), zb + 0.6, zb - 0.05, "PSP Recess", axis="z")
    # ends: POWER/HOLD slider (right), WLAN switch (left), on the band
    for sx in (1, -1):
        xe = sx * (84.6 - BAND_INSET)
        b.prism(rrect(2.0, 3.2, 0.8, (bt0 + bt1) / 2, -14.0), xe - sx * 1.2, xe + sx * 0.55,
                "PSP Silver Plate", axis="x", bev1=0.3, seg1=1)
    # rear: battery PUSH button on the +x grip's end, strap eyelet high on the -x grip
    b.prism(rrect(2.6, 4.2, 1.0, y_of(17.5), 0.0), 81.5, 84.0, "PSP Rear Black",
            axis="x", bev1=0.5, seg1=2)
    hz = rear_height(-70.0, 25.5)
    b.prism(circle(-70.0, 25.5, 1.1, 16), y_of(hz - 1.2), y_of(hz + 0.3), "PSP Recess")


def build_bay(b):
    """Inside the door channel: dark floor, spindle and the top lip."""
    b.plate(rrect(DOOR_X - 0.05, DOOR_Z + 0.5, 0.0), y_of(T_BAY + 0.03), "PSP Recess", facing=1)
    b.ring(0.0, UMD_SEAT_Z, 2.0, 5.2, y_of(T_BAY) + 0.08, "PSP Steel", facing=1, seg=40)
    b.box(-DOOR_X, DOOR_X, y_of(T_BAY - 0.2), y_of(T_DOOR_IN - 0.1), DOOR_Z - 1.0, DOOR_Z + 0.6,
          "PSP Rear Black", bev=0.3)
    b.box(-DOOR_X, DOOR_X, y_of(T_BAY - 0.2), y_of(T_DOOR_IN - 0.4), -DOOR_Z - 0.6, -DOOR_Z + 0.9,
          "PSP Rear Black", bev=0.2)


def build_psp_body(col):
    b = MeshBuilder()
    build_rear_shell(b)
    rear = b.to_object("PSP_Body", col)
    cut_channel(rear, col)
    b = MeshBuilder()
    build_front_shell(b)
    build_band(b)
    for s in (1, -1):
        build_shoulder(b, s)
    build_bay(b)
    build_front_details(b)
    build_edge_details(b)
    rest = b.to_object("PSP_Body_front", col)
    # join into one object
    bm = bmesh.new()
    bm.from_mesh(rear.data)
    mats = [m for m in rear.data.materials]
    offset = len(mats)
    for m in rest.data.materials:
        if m not in mats:
            mats.append(m)
    remap = {i: mats.index(m) for i, m in enumerate(rest.data.materials)}
    bm2 = bmesh.new()
    bm2.from_mesh(rest.data)
    for f in bm2.faces:
        f.material_index = remap[f.material_index]
    tmp = bpy.data.meshes.new("tmp")
    bm2.to_mesh(tmp)
    bm2.free()
    bm.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)
    me = bpy.data.meshes.new("PSP_Body")
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    try:
        me.set_sharp_from_angle(angle=math.radians(50.0))
    except AttributeError:
        pass
    old = rear.data
    rear.data = me
    bpy.data.meshes.remove(old)
    bpy.data.objects.remove(rest, do_unlink=True)
    return rear


def build_psp_screen(col):
    b = MeshBuilder()
    sx, sz = SCREEN_C
    b.plate(rrect(LCD_HW, LCD_HH, 0.0, sx, sz), y_of(0.0) - 0.12, "PSP Screen",
            uv=lambda a, c: ((a - (sx - LCD_HW)) / (2 * LCD_HW), (c - (sz - LCD_HH)) / (2 * LCD_HH)))
    return b.to_object("PSP_Screen", col)


def build_psp_door(col):
    b = MeshBuilder()
    b.prism(rrect(DOOR_X, DOOR_Z, 1.2), y_of(T_DOOR_IN), y_of(T_DOOR_OUT), "PSP Rear Black",
            bev0=0.3, seg0=1, bev1=1.1, seg1=4)
    b.ring(0.0, 0.0, 19.4, 26.6, y_of(T_DOOR_OUT) + 0.05, "PSP Door Ring", facing=1, seg=96)
    for s in (1, -1):
        x0, x1 = sorted((s * 32.8, s * 34.4))
        b.box(x0, x1, y_of(T_BAY + 0.3), y_of(T_DOOR_IN + 0.05), -DOOR_Z + 1.2, 18.0, "PSP Steel", bev=0.2)
        b.box(x0 - s * 1.6 if s > 0 else x0, x1 if s > 0 else x1 + 1.6, y_of(T_BAY + 0.3),
              y_of(T_BAY + 0.8), -DOOR_Z + 1.2, 18.0, "PSP Steel", bev=0.1)
    b.box(-30.0, 30.0, y_of(T_BAY + 0.3), y_of(T_DOOR_IN + 0.05), -DOOR_Z + 0.2, -DOOR_Z + 1.4,
          "PSP Steel", bev=0.2)
    return b.to_object("PSP_Door", col, origin_mm=HINGE)


# --- UMD ------------------------------------------------------------------------------------

def umd_outline(d=0.0):
    r = UMD_R + d
    cr = UMD_CORNER + d
    pts = arc(0.0, 0.0, r, 0, 180, 48)
    pts += [(-r, UMD_FLAT + UMD_CORNER)]
    pts += arc(-UMD_R + UMD_CORNER, UMD_FLAT + UMD_CORNER, cr, 180, 270, 8)
    pts += arc(UMD_R - UMD_CORNER, UMD_FLAT + UMD_CORNER, cr, 270, 360, 8)
    return dedupe(pts)


def build_umd(col):
    """Clear shell with an opaque white rim and base, the disc inside, and the shutter on the
    read side. The label lives on the disc, so the game's art shows through the window."""
    h = UMD_T / 2
    b = MeshBuilder()
    b.prism(umd_outline(), -h, h, "UMD Clear", bev0=0.9, bev1=0.9, seg0=3, seg1=3)
    # opaque white rim (2 mm) on both faces, and the solid base below the disc
    bm = b.bm
    outer = dedupe(umd_outline(-0.7))
    inner = dedupe(umd_outline(-3.2))
    for side in (-1, 1):
        yy = side * (h + 0.015)
        vo = [bm.verts.new(Vector((x, yy, z))) for x, z in outer]
        vi = [bm.verts.new(Vector((x, yy, z))) for x, z in inner]
        n = len(outer)
        faces = []
        for i in range(n):
            j = (i + 1) % n
            f = bm.faces.new((vo[i], vo[j], vi[j], vi[i]))
            f.material_index = b.mi("UMD Shell")
            faces.append(f)
        for f in faces:
            f.normal_update()
            if f.normal.y * side < 0:
                f.normal_flip()
        # base strip below the disc
        base = [(x, z) for x, z in inner if z < -26.4]
        zl = _edge_z(inner, -28.6, True) if False else -26.5
        b.plate(dedupe([(-28.75, zl), (28.75, zl)] + sorted(base, key=lambda p: -p[0])),
                yy, "UMD Shell", facing=side)
    umd = b.to_object("UMD", col)

    d = MeshBuilder()
    dt = 0.6
    d.prism(circle(0.0, 0.0, 30.0, 96), -dt, dt, "UMD Disc", bev0=0.15, bev1=0.15, seg0=1, seg1=1)
    d.plate(circle(0.0, 0.0, 29.6, 96), -dt - 0.02, "UMD Label",
            uv=lambda a, c: ((a + 30.0) / 60.0, (c + 30.0) / 60.0))
    d.ring(0.0, 0.0, 7.0, 7.6, -dt - 0.03, "UMD Hub", seg=48)
    d.ring(0.0, 0.0, 2.6, 7.0, dt + 0.02, "UMD Hub", facing=1, seg=48)
    d.plate(circle(0.0, 0.0, 2.6, 24), dt + 0.03, "PSP Steel", facing=1)
    # shutter plate over the read window
    d.prism(rrect(6.5, 12.0, 1.0, 0.0, -17.0), dt + 0.05, dt + 0.35, "PSP Steel", bev1=0.15, seg1=1)
    disc = d.to_object("UMD_Disc", col)
    disc.parent = umd
    return umd


# --- Scene ----------------------------------------------------------------------------------

def _fresh_scene():
    old = bpy.data.scenes.get(SCENE_NAME)
    if old:
        for ob in list(old.collection.all_objects):
            if len(ob.users_scene) <= 1:
                data = ob.data
                bpy.data.objects.remove(ob)
                if data is not None and data.users == 0:
                    if isinstance(data, bpy.types.Mesh):
                        bpy.data.meshes.remove(data)
                    elif isinstance(data, bpy.types.Camera):
                        bpy.data.cameras.remove(data)
                    elif isinstance(data, bpy.types.Light):
                        bpy.data.lights.remove(data)
        for c in list(old.collection.children_recursive):
            bpy.data.collections.remove(c)
        bpy.data.scenes.remove(old)
    sc = bpy.data.scenes.new(SCENE_NAME)
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.length_unit = "MILLIMETERS"
    return sc


def _collection(sc, name):
    c = bpy.data.collections.new(name)
    sc.collection.children.link(c)
    return c


def build_models(sc):
    ensure_materials()
    psp_col = _collection(sc, "EmuShelf PSP")
    umd_col = _collection(sc, "EmuShelf UMD")
    psp = bpy.data.objects.new("PSP", None)
    psp.empty_display_size = 0.02
    psp_col.objects.link(psp)
    body = build_psp_body(psp_col)
    screen = build_psp_screen(psp_col)
    hinge = bpy.data.objects.new("PSP_DoorHinge", None)
    hinge.empty_display_type = "SINGLE_ARROW"
    hinge.empty_display_size = 0.01
    hinge.location = Vector(HINGE) * MM
    psp_col.objects.link(hinge)
    door = build_psp_door(psp_col)
    for ob in (body, screen, hinge):
        ob.parent = psp
    door.parent = hinge
    door.location = (0.0, 0.0, 0.0)
    umd = build_umd(umd_col)
    umd.rotation_mode = "QUATERNION"
    psp.rotation_mode = "QUATERNION"
    return psp, hinge, umd


def stage_world():
    """Dark stage with a studio HDR for reflections: metals and gloss read as such."""
    world = bpy.data.worlds.get("EmuShelf Stage") or bpy.data.worlds.new("EmuShelf Stage")
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    mix = nt.nodes.new("ShaderNodeMixShader")
    dark = nt.nodes.new("ShaderNodeBackground")
    dark.inputs[0].default_value = (0.035, 0.04, 0.05, 1.0)
    env_bg = nt.nodes.new("ShaderNodeBackground")
    env_bg.inputs[1].default_value = 0.8
    env = nt.nodes.new("ShaderNodeTexEnvironment")
    path = os.path.join(bpy.utils.system_resource("DATAFILES"), "studiolights", "world", "studio.exr")
    if os.path.exists(path):
        env.image = bpy.data.images.load(path, check_existing=True)
    lp = nt.nodes.new("ShaderNodeLightPath")
    nt.links.new(env.outputs[0], env_bg.inputs[0])
    nt.links.new(lp.outputs["Is Camera Ray"], mix.inputs[0])
    nt.links.new(env_bg.outputs[0], mix.inputs[1])
    nt.links.new(dark.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs[0])
    return world


# --- Launch / exit choreography ------------------------------------------------------------
#
# A reference for the app, which drives the same parts in code. Everything is a pure function of
# time, so the app can port pose() directly; the exit is the launch played backwards.

FPS = 30
INSERT_FRAMES = 84
HOLD_FRAMES = 30
SLIDE_MM = 64.0


def clamp01(x):
    return max(0.0, min(1.0, x))


def phase(f, f0, f1):
    return clamp01((f - f0) / float(f1 - f0))


def ease_in_out(t):
    return t * t * (3.0 - 2.0 * t)


def ease_out(t):
    return 1.0 - (1.0 - t) ** 3


def ease_in(t):
    return t ** 3


def lerp(a, b, t):
    return a + (b - a) * t


def _rot(axis, deg):
    return Matrix.Rotation(math.radians(deg), 4, axis)


def door_world(psp_m, door_deg):
    return psp_m @ Matrix.Translation(Vector(HINGE) * MM) @ _rot("X", door_deg)


def umd_in_door(slide_mm):
    seat = Vector(UMD_SEAT) - Vector(HINGE)
    return Matrix.Translation((seat + Vector((0.0, 0.0, slide_mm))) * MM) @ _rot("X", 180.0)


def pose(f):
    """Launch at frame f (0..INSERT_FRAMES) -> (psp_matrix, door_deg, umd_matrix, screen 0..1)."""
    rise = ease_out(phase(f, 2, 24))
    turn = ease_in_out(phase(f, 62, 80))
    psp_loc = Vector((0.0, lerp(0.03, 0.0, rise), lerp(-0.17, 0.0, rise)))
    psp_loc.z += 0.012 * math.sin(math.pi * turn)
    psp_m = Matrix.Translation(psp_loc) @ _rot("Z", 180.0 + 180.0 * turn)

    opening = ease_in_out(phase(f, 14, 26))
    door = DOOR_OPEN_DEG * opening
    if f >= 50:
        door = DOOR_OPEN_DEG * (1.0 - ease_in(phase(f, 50, 57)))
        door -= 2.0 * math.sin(math.pi * phase(f, 57, 61))      # small rebound off the catch

    lift = ease_out(phase(f, 0, 14))
    stage = Matrix.Translation((0.0, lerp(0.0, -0.035, lift), lerp(0.0, 0.05, lift)))
    door_m = door_world(psp_m, door)
    target = door_m @ umd_in_door(SLIDE_MM)
    align = ease_in_out(phase(f, 20, 38))
    if f < 38:
        loc = stage.to_translation().lerp(target.to_translation(), align)
        rot = stage.to_quaternion().slerp(target.to_quaternion(), align)
        umd_m = Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
    else:
        umd_m = door_m @ umd_in_door(SLIDE_MM * (1.0 - ease_in_out(phase(f, 38, 50))))

    return psp_m, door, umd_m, ease_in_out(phase(f, 76, 84))


def build_preview(sc, psp, hinge, umd):
    rig = _collection(sc, "Preview rig (not exported)")
    sc.render.fps = FPS
    eject0 = INSERT_FRAMES + HOLD_FRAMES
    sc.frame_start, sc.frame_end = 0, eject0 + INSERT_FRAMES + 20
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            sc.render.engine = engine
            break
        except TypeError:
            continue
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.world = stage_world()

    cam = bpy.data.objects.new("Stage camera", bpy.data.cameras.new("Stage camera"))
    cam.data.lens = 50
    rig.objects.link(cam)
    sc.camera = cam
    for name, loc, energy, size in (("Key", (-0.25, -0.35, 0.30), 6.0, 0.30),
                                    ("Fill", (0.30, -0.30, 0.05), 2.0, 0.40),
                                    ("Rim", (0.10, 0.35, 0.25), 5.0, 0.25)):
        lo = bpy.data.objects.new(name, bpy.data.lights.new(name, "AREA"))
        lo.data.energy, lo.data.size = energy, size
        lo.location = loc
        lo.rotation_euler = (-Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        rig.objects.link(lo)

    emit = next(n for n in bpy.data.materials["PSP Screen"].node_tree.nodes
                if n.type == "BSDF_PRINCIPLED").inputs["Emission Strength"]

    def key(frame, f):
        psp_m, door, umd_m, screen = pose(f)
        psp.matrix_world = psp_m
        psp.keyframe_insert("location", frame=frame)
        psp.keyframe_insert("rotation_quaternion", frame=frame)
        hinge.rotation_euler = (math.radians(door), 0.0, 0.0)
        hinge.keyframe_insert("rotation_euler", frame=frame)
        umd.matrix_world = umd_m
        umd.keyframe_insert("location", frame=frame)
        umd.keyframe_insert("rotation_quaternion", frame=frame)
        emit.default_value = 2.5 * screen
        emit.keyframe_insert("default_value", frame=frame)
        pull = ease_in_out(phase(f, 0, 26))
        settle = ease_in_out(phase(f, 58, 82))
        eye = Vector((0.0, -0.24, 0.03)).lerp(Vector((0.17, -0.27, 0.16)), pull)
        eye = eye.lerp(Vector((0.0, -0.40, 0.06)), settle)
        aim = Vector((0.0, 0.0, 0.0)).lerp(Vector((0.0, -0.012, 0.004)), pull * (1.0 - settle))
        cam.location = eye
        cam.rotation_euler = (aim - eye).to_track_quat("-Z", "Y").to_euler()
        cam.keyframe_insert("location", frame=frame)
        cam.keyframe_insert("rotation_euler", frame=frame)

    for frame in range(sc.frame_end + 1):
        if frame <= INSERT_FRAMES:
            key(frame, frame)
        elif frame < eject0:
            key(frame, INSERT_FRAMES)
        else:
            key(frame, max(0, INSERT_FRAMES - (frame - eject0)))
    sc.frame_set(0)


def build_all(preview=True):
    sc = _fresh_scene()
    try:
        bpy.context.window.scene = sc
    except AttributeError:
        pass
    psp, hinge, umd = build_models(sc)
    if preview:
        build_preview(sc, psp, hinge, umd)
    else:
        sc.world = stage_world()
    return sc
