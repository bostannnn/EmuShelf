"""Generates EmuShelf's keep-case shells: the DVD case (PS2, GameCube, Wii) and the PSP's UMD case.

Run inside Blender (4.1 or newer):

    blender --background --python tools/blender/keep_cases.py

or, from a live session, exec() this file. It writes

    src/EmuShelf.Rendering/Assets/disc-keep-case.glb
    src/EmuShelf.Rendering/Assets/umd-case.glb

The geometry is original and fully described by the numbers below, so this file is the source the
shipped .glb files are regenerated from. Nothing here is downloaded, scanned or traced.

Each case is two meshes on two materials, the arrangement the Blu-ray shell already uses:

  body   The moulded case: lid and tray as one closed shell with the parting seam round the top,
         bottom and opening edges, and the long finger slot let into the opening edge's wall.
  cover  The clear film the printed sleeve sits under: front sheet, spine wrap and back sheet, and
         nothing else. EmuShelf scopes its artwork panels to this material, so the film's edges are
         the print's edges and the case's own rim stays bare plastic, as on the real object.

Authored in Blender's frame: X is width with the spine at -X, Z is up, and the front faces -Y, which
the glTF exporter turns into EmuShelf's canonical +Z front. Units are metres on export.
"""

import json
import math
import os
import struct

import bmesh
import bpy

MM = 0.001

# Linear base colour and roughness of the bare plastic. These are the measured medians of the maps
# the previous, downloaded keep case shipped with (sRGB 46 and 0.25), so every per-platform finish
# in MediaShellRenderer.MaterialVariantAppearance keeps the calibration it was tuned against.
PLASTIC_BASE = 0.027
PLASTIC_ROUGHNESS = 0.25

CASES = {
    # A DVD keep case: 135 x 190 x 14mm. Its sleeve is a 183mm-tall sheet whose front panel is
    # 129.5mm wide, which is the 0.708 every scraped PS2/GameCube/Wii cover is drawn at.
    "disc-keep-case": dict(
        title="DVD keep case",
        width=135.0, height=190.0, depth=14.0,
        sleeve_margin=3.5, sleeve_open_margin=5.5,
        recess_length=87.0,
    ),
    # A PSP UMD case: 104 x 178 x 15mm. Front panel 99.5 x 171mm, the 0.581 of a PSP box scan.
    "umd-case": dict(
        title="UMD case",
        width=104.0, height=178.0, depth=15.0,
        sleeve_margin=3.5, sleeve_open_margin=4.5,
        recess_length=110.0,
    ),
}

# Shared moulding details, in millimetres.
CORNER_RADIUS = 3.0      # silhouette corners, seen from the front
EDGE_RADIUS = 1.5        # fillet where a face turns into an edge wall
FILM_PROUD = 0.3         # how far the film stands off the body
FILM_SKIRT = 0.45        # how far the film's border turns in, so it closes against the body
SEAM_HALF_WIDTH = 0.3    # parting line between lid and tray
SEAM_DEPTH = 0.45
# The finger slot: a long pocket in the opening edge's wall, centred on the seam. On the real cases
# the lid's and tray's side walls are set inboard along the middle of this edge while both panels
# run on to the outline, which leaves a lip on each to pull apart. So it is let into the wall only,
# and the case's outline stays whole. Its length is per case, measured off photographs of an open
# PS2 case and an open UMD case: it runs 46% of a DVD case's height and 62% of a UMD case's.
#
# Two earlier versions were wrong in opposite ways. The first cut a 32mm bite through the lid, which
# took a notch out of the front silhouette; the second kept the outline but made a 32 x 5mm pocket,
# a third of the real slot's length and half its height.
RECESS_DEPTH = 2.0
RECESS_LIP = 2.3           # panel left standing on each side of the slot, across the thickness
RECESS_CORNER = 1.5        # radius of the slot's four corners
RECESS_WALL = 0.35         # run of the slot's end walls, so they are steep but not vertical
RECESS_FLOOR_POINTS = 3
RECESS_ARC_DEGREES = (60.0, 30.0, 0.0)
CORNER_SEGMENTS = 6
FILLET_SEGMENTS = 4
SMOOTH_ANGLE = math.radians(35.0)


def outline(x0, x1, z0, z1, inset, recess_half_width, recess_cut):
    """One rounded-rectangle loop in the XZ plane, with the recess's vertices on its +X edge.

    Every loop carries those vertices whether or not it passes through the recess, so consecutive
    loops bridge with plain quads. Their spacing follows the recess's width at this loop's depth,
    which is what lets a stack of loops trace its rounded ends.
    """
    radius = CORNER_RADIUS - inset
    points = []
    centres = [
        (x1 - CORNER_RADIUS, z1 - CORNER_RADIUS, 0.0),
        (x0 + CORNER_RADIUS, z1 - CORNER_RADIUS, 90.0),
        (x0 + CORNER_RADIUS, z0 + CORNER_RADIUS, 180.0),
        (x1 - CORNER_RADIUS, z0 + CORNER_RADIUS, 270.0),
    ]
    for cx, cz, start in centres:
        for step in range(CORNER_SEGMENTS + 1):
            angle = math.radians(start + 90.0 * step / CORNER_SEGMENTS)
            points.append((cx + radius * math.cos(angle), cz + radius * math.sin(angle)))

    # The +X edge, walked bottom to top to close the loop back on the first corner: the recess's
    # rim, the foot of its wall, its floor, and the same again mirrored.
    centre_z = (z0 + z1) * 0.5
    floor = recess_half_width - RECESS_WALL
    points.append((x1 - inset, centre_z - recess_half_width))
    points.append((x1 - inset - recess_cut, centre_z - floor))
    for step in range(1, RECESS_FLOOR_POINTS + 1):
        z = -floor + 2.0 * floor * step / (RECESS_FLOOR_POINTS + 1)
        points.append((x1 - inset - recess_cut, centre_z + z))
    points.append((x1 - inset - recess_cut, centre_z + floor))
    points.append((x1 - inset, centre_z + recess_half_width))
    return points


def build_body(name, spec):
    width, height, depth = spec["width"], spec["height"], spec["depth"]

    # The film wraps the spine and both faces, so the body sits FILM_PROUD inside the case's
    # nominal size on those three sides and exactly on it at the top, bottom and opening edge.
    x0, x1 = -width * 0.5 + FILM_PROUD, width * 0.5
    z0, z1 = -height * 0.5, height * 0.5
    front, back = FILM_PROUD, depth - FILM_PROUD
    seam = depth * 0.5

    half_length = spec["recess_length"] * 0.5
    half_height = depth * 0.5 - RECESS_LIP
    straight = half_height - RECESS_CORNER

    # The slot is a rounded rectangle on the edge wall. Walking in from its rim toward the seam,
    # each of these is (distance from the seam, half the slot's length there).
    rim = (half_height, half_length - RECESS_CORNER)
    corner = [
        (straight + RECESS_CORNER * math.sin(math.radians(angle)),
         half_length - RECESS_CORNER + RECESS_CORNER * math.cos(math.radians(angle)))
        for angle in RECESS_ARC_DEGREES]

    # (distance behind the case's front plane, inset from the outline, slot half-length, slot cut)
    profile = []
    for step in range(FILLET_SEGMENTS + 1):
        angle = math.radians(90.0 * step / FILLET_SEGMENTS)
        profile.append((
            front + EDGE_RADIUS * (1.0 - math.cos(angle)),
            EDGE_RADIUS * (1.0 - math.sin(angle)),
            rim[1], 0.0))

    # Through the slot, front rim to back rim, with the parting seam crossing its floor.
    profile.append((seam - rim[0], 0.0, rim[1], 0.0))
    for offset, length in corner:
        profile.append((seam - offset, 0.0, length, RECESS_DEPTH))
    profile.append((seam - SEAM_HALF_WIDTH, 0.0, half_length, RECESS_DEPTH))
    profile.append((seam, SEAM_DEPTH, half_length, RECESS_DEPTH))
    profile.append((seam + SEAM_HALF_WIDTH, 0.0, half_length, RECESS_DEPTH))
    for offset, length in reversed(corner):
        profile.append((seam + offset, 0.0, length, RECESS_DEPTH))
    profile.append((seam + rim[0], 0.0, rim[1], 0.0))

    for step in range(FILLET_SEGMENTS, -1, -1):
        angle = math.radians(90.0 * step / FILLET_SEGMENTS)
        profile.append((
            back - EDGE_RADIUS * (1.0 - math.cos(angle)),
            EDGE_RADIUS * (1.0 - math.sin(angle)),
            rim[1], 0.0))

    mesh = bmesh.new()
    loops = []
    for distance, inset, half_width, cut in profile:
        y = -depth * 0.5 + distance
        loops.append([
            mesh.verts.new((x * MM, y * MM, z * MM))
            for x, z in outline(x0, x1, z0, z1, inset, half_width, cut)])

    count = len(loops[0])
    for near, far in zip(loops, loops[1:]):
        for index in range(count):
            following = (index + 1) % count
            mesh.faces.new((near[index], near[following], far[following], far[index]))
    mesh.faces.new(loops[0])
    mesh.faces.new(list(reversed(loops[-1])))

    return finish(mesh, f"{name}-body")


def build_film(name, spec):
    width, height, depth = spec["width"], spec["height"], spec["depth"]
    spine = -width * 0.5
    open_end = width * 0.5 - spec["sleeve_open_margin"]
    z0 = -height * 0.5 + spec["sleeve_margin"]
    z1 = height * 0.5 - spec["sleeve_margin"]
    radius = EDGE_RADIUS + FILM_PROUD

    # The film's cross-section, walked from the opening edge across the front, round the spine and
    # back along the rear: (x, distance behind the front plane, outward normal).
    path = [(open_end, 0.0, 0.0, -1.0)]
    for step in range(CORNER_SEGMENTS + 1):
        angle = math.radians(90.0 * step / CORNER_SEGMENTS)
        normal = (-math.sin(angle), -math.cos(angle))
        path.append((
            spine + radius + radius * normal[0], radius + radius * normal[1], *normal))
    for step in range(CORNER_SEGMENTS + 1):
        angle = math.radians(90.0 * step / CORNER_SEGMENTS)
        normal = (-math.cos(angle), math.sin(angle))
        path.append((
            spine + radius + radius * normal[0],
            depth - radius + radius * normal[1],
            *normal))
    path.append((open_end, depth, 0.0, 1.0))

    def turned_in(point):
        x, distance, nx, nd = point
        return (x - nx * FILM_SKIRT, distance - nd * FILM_SKIRT)

    surface = [(x, distance) for x, distance, _, _ in path]
    skirt = [turned_in(point) for point in path]
    # The two free ends turn in as well, so the film is a shallow tray closed against the body on
    # all four borders and no pose can look under it.
    surface = [skirt[0], *surface, skirt[-1]]
    skirt = [skirt[0], *skirt, skirt[-1]]

    mesh = bmesh.new()
    rows = []
    for z, section in ((z0, skirt), (z0, surface), (z1, surface), (z1, skirt)):
        rows.append([
            mesh.verts.new((x * MM, (-depth * 0.5 + distance) * MM, z * MM))
            for x, distance in section])

    for lower, upper in zip(rows, rows[1:]):
        for index in range(len(lower) - 1):
            mesh.faces.new((lower[index], lower[index + 1], upper[index + 1], upper[index]))

    return finish(mesh, f"{name}-sleeve", closed=False)


def finish(mesh, name, closed=True):
    bmesh.ops.remove_doubles(mesh, verts=mesh.verts, dist=1e-7)
    bmesh.ops.dissolve_degenerate(mesh, edges=mesh.edges, dist=1e-7)
    bmesh.ops.recalc_face_normals(mesh, faces=mesh.faces)
    if not closed:
        # An open sheet has no inside for recalc to find, so settle it explicitly: the film's
        # front sheet must face -Y.
        mesh.faces.ensure_lookup_table()
        reference = min(mesh.faces, key=lambda face: face.calc_center_median().y)
        if reference.normal.y > 0.0:
            bmesh.ops.reverse_faces(mesh, faces=mesh.faces)

    data = bpy.data.meshes.new(name)
    mesh.to_mesh(data)
    mesh.free()
    data.shade_smooth()
    data.set_sharp_from_angle(angle=SMOOTH_ANGLE)
    return data


def material(name):
    result = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    result.use_nodes = True
    shader = next(node for node in result.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    shader.inputs["Base Color"].default_value = (PLASTIC_BASE, PLASTIC_BASE, PLASTIC_BASE, 1.0)
    shader.inputs["Roughness"].default_value = PLASTIC_ROUGHNESS
    shader.inputs["Metallic"].default_value = 0.0
    result.use_backface_culling = False
    return result


def build_case(name, spec, collection):
    objects = []
    for data, material_name in (
            (build_body(name, spec), "body"), (build_film(name, spec), "cover")):
        data.materials.append(material(material_name))
        # Re-running in a live session replaces the previous build rather than adding beside it.
        object_name = data.name.split(".")[0]
        item = bpy.data.objects.get(object_name)
        if item is None:
            item = bpy.data.objects.new(object_name, data)
        else:
            previous = item.data
            item.data = data
            if previous.users == 0:
                bpy.data.meshes.remove(previous)
        data.name = object_name
        if item.name not in collection.objects:
            collection.objects.link(item)
        objects.append(item)
    return objects


def stamp(path, title):
    """Writes the provenance block every bundled shell carries into the GLB's asset.extras."""
    with open(path, "rb") as stream:
        blob = stream.read()
    json_length = struct.unpack_from("<I", blob, 12)[0]
    document = json.loads(blob[20:20 + json_length])
    rest = blob[20 + json_length:]

    document["asset"]["extras"] = {
        "author": "EmuShelf contributors",
        "title": title,
        "license": "GPL-3.0-or-later, as part of EmuShelf",
        "source": "tools/blender/keep_cases.py",
    }
    encoded = json.dumps(document, separators=(",", ":")).encode("utf-8")
    encoded += b" " * (-len(encoded) % 4)

    with open(path, "wb") as stream:
        stream.write(blob[:8])
        stream.write(struct.pack("<I", 12 + 8 + len(encoded) + len(rest)))
        stream.write(struct.pack("<I", len(encoded)))
        stream.write(blob[16:20])
        stream.write(encoded)
        stream.write(rest)


def export(name, collection, directory):
    path = os.path.join(directory, f"{name}.glb")
    # By collection rather than by selection: a selection is per view layer, and an object left
    # selected in another scene of the same file is exported along with the case.
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        collection=collection.name,
        export_apply=True,
        export_yup=True,
        export_normals=True,
        export_texcoords=False,
        export_materials="EXPORT",
        export_animations=False,
        export_cameras=False,
        export_lights=False,
        export_extras=False)
    stamp(path, CASES[name]["title"])
    return path


def main(directory=None):
    if directory is None:
        here = os.path.dirname(os.path.abspath(__file__))
        directory = os.path.normpath(
            os.path.join(here, "..", "..", "src", "EmuShelf.Rendering", "Assets"))

    scene = bpy.context.scene
    written = []
    offset = 0.0
    for name, spec in CASES.items():
        collection = bpy.data.collections.get(name + "-generated")
        if collection is None:
            collection = bpy.data.collections.new(name + "-generated")
            scene.collection.children.link(collection)
        objects = build_case(name, spec, collection)
        for item in objects:
            item.location = (0.0, 0.0, 0.0)
        written.append(export(name, collection, directory))
        # Stood apart afterwards so both can be looked at in one viewport.
        for item in objects:
            item.location.x = offset
        offset += (spec["width"] + 40.0) * MM
    return written


if __name__ == "__main__":
    for path in main():
        print("wrote", path)
