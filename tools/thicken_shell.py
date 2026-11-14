#!/usr/bin/env python3
"""Brings a bundled shell that was modelled too thin up to the real object's thickness, in place.

    python3 tools/thicken_shell.py ds-card
    python3 tools/thicken_shell.py megadrive-cartridge

Two of the downloaded shells are the right shape face-on and too thin: satchii_'s DS card is 1.76mm
where a real card is 3.8mm, and Naser's Mega Drive cartridge is 11.8mm where a real one is 17mm.
Both have a plane across their thickness that nothing crosses except straight side wall — on the
card, just behind its flat front plate; on the cartridge, behind the rounded front shell and its
board. So the missing thickness can be inserted there as a slab: everything behind the plane moves
back by the difference, and the walls, which run parallel to that move, simply get longer. The
front, the back's moulding and every normal are left exactly as authored. A plain scale along the
thickness would instead have doubled the depth of the card's pin bay, and turned the cartridge's
rounded front edges into ellipses.

Only the position data and the provenance note change. Textures, materials, UVs, normals, tangents
and the author's metadata stay byte-identical. Running it on an already-thickened shell does
nothing. The script refuses to run on a mesh where the plane touches a vertex or crosses anything
that is not a straight wall.
"""

import json
import struct
import sys

SHELLS = {
    "ds-card": dict(
        # 33 x 35 x 3.8mm. As placed by its nodes the label faces -Z.
        thickness_over_height=3.8 / 35.0,
        front_toward=-1.0,
        split_behind_front_over_height=0.19 / 35.0,
        note=(
            " Thickened to a real card's 3.8mm by moving everything behind the flat front plate"
            " back along the side walls (tools/thicken_shell.py); the front, the back's moulding"
            " and all normals are as authored."),
    ),
    "megadrive-cartridge": dict(
        # 109 x 70 x 17mm. The label faces +Z. The plane sits 7.8mm behind the front, in the
        # 0.45mm of the cartridge's thickness that holds no vertices: behind the front shell's
        # rounded edges, which end 5mm in, and behind the board.
        thickness_over_height=17.0 / 70.0,
        front_toward=1.0,
        split_behind_front_over_height=7.78 / 70.17,
        note=(
            " Thickened to a real cartridge's 17mm by moving the back shell away from the front"
            " along the straight side walls (tools/thicken_shell.py); the rounded front, the"
            " back's moulding and all normals are as authored."),
    ),
}


def multiply(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def node_matrix(node):
    if "matrix" in node:
        m = node["matrix"]  # column-major
        return [[m[column * 4 + row] for column in range(4)] for row in range(4)]
    if any(key in node for key in ("translation", "rotation", "scale")):
        raise SystemExit("This script expects matrix nodes; the card's export uses nothing else.")
    return [[1.0 if i == j else 0.0 for j in range(4)] for i in range(4)]


def world_matrices(document):
    result = {}

    def walk(index, parent):
        world = multiply(parent, node_matrix(document["nodes"][index]))
        result[index] = world
        for child in document["nodes"][index].get("children", []):
            walk(child, world)

    identity = [[1.0 if i == j else 0.0 for j in range(4)] for i in range(4)]
    for root in document["scenes"][document.get("scene", 0)]["nodes"]:
        walk(root, identity)
    return result


def inverse3(m):
    a, b, c = m[0][:3], m[1][:3], m[2][:3]
    det = (a[0] * (b[1] * c[2] - b[2] * c[1]) - a[1] * (b[0] * c[2] - b[2] * c[0])
           + a[2] * (b[0] * c[1] - b[1] * c[0]))
    return [
        [(b[1] * c[2] - b[2] * c[1]) / det, (a[2] * c[1] - a[1] * c[2]) / det,
         (a[1] * b[2] - a[2] * b[1]) / det],
        [(b[2] * c[0] - b[0] * c[2]) / det, (a[0] * c[2] - a[2] * c[0]) / det,
         (a[2] * b[0] - a[0] * b[2]) / det],
        [(b[0] * c[1] - b[1] * c[0]) / det, (a[1] * c[0] - a[0] * c[1]) / det,
         (a[0] * b[1] - a[1] * b[0]) / det],
    ]


def main(name, path=None):
    shell = SHELLS[name]
    path = path or f"src/EmuShelf.Rendering/Assets/{name}.glb"
    with open(path, "rb") as stream:
        blob = stream.read()
    json_length = struct.unpack_from("<I", blob, 12)[0]
    document = json.loads(blob[20:20 + json_length])
    binary_start = 20 + json_length + 8
    binary = bytearray(blob[binary_start:])

    worlds = world_matrices(document)
    drawn = [index for index, node in enumerate(document["nodes"])
             if "mesh" in node and index in worlds]
    if len(drawn) != 1:
        raise SystemExit(f"Expected one drawn mesh, found {len(drawn)}.")
    world = worlds[drawn[0]]
    primitives = document["meshes"][document["nodes"][drawn[0]]["mesh"]]["primitives"]
    if len(primitives) != 1:
        raise SystemExit(f"Expected one primitive, found {len(primitives)}.")

    accessor = document["accessors"][primitives[0]["attributes"]["POSITION"]]
    view = document["bufferViews"][accessor["bufferView"]]
    if accessor["componentType"] != 5126 or accessor["type"] != "VEC3":
        raise SystemExit("Positions are not plain float vectors.")
    stride = view.get("byteStride", 12)
    start = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    count = accessor["count"]

    local = [list(struct.unpack_from("<3f", binary, start + i * stride)) for i in range(count)]
    placed = [[sum(world[r][c] * p[c] for c in range(3)) + world[r][3] for r in range(3)]
              for p in local]
    size = [max(p[axis] for p in placed) - min(p[axis] for p in placed) for axis in range(3)]

    # As placed by its nodes each shell stands upright with its thickness along Z.
    if not (size[2] < size[0] and size[2] < size[1]):
        raise SystemExit(f"Thickness is not along Z as expected; the shell measures {size}.")
    height = size[1]
    target = height * shell["thickness_over_height"]
    delta = target - size[2]
    if delta <= height * 1e-4:
        print(f"{name}: already {size[2] / height:.4f} of its height thick; nothing to do.")
        return

    # Work in distance behind the front plane, so one rule serves a shell facing either way.
    toward = shell["front_toward"]
    front = max(p[2] * toward for p in placed)
    behind = [front - p[2] * toward for p in placed]
    split = height * shell["split_behind_front_over_height"]
    clearance = min(abs(depth - split) for depth in behind)
    if clearance < height * 1e-3:
        raise SystemExit("A vertex lies on the split plane; this is not the mesh this was written for.")

    # Every triangle the split crosses must be a straight side wall, or lengthening it would
    # change its slope and leave its stored normal wrong.
    indices_accessor = document["accessors"][primitives[0]["indices"]]
    indices_view = document["bufferViews"][indices_accessor["bufferView"]]
    index_format = {5123: "<H", 5125: "<I"}[indices_accessor["componentType"]]
    index_size = struct.calcsize(index_format)
    index_start = indices_view.get("byteOffset", 0) + indices_accessor.get("byteOffset", 0)
    indices = [struct.unpack_from(index_format, binary, index_start + i * index_size)[0]
               for i in range(indices_accessor["count"])]
    walls = 0
    for triangle in range(0, len(indices), 3):
        corners = [indices[triangle + corner] for corner in range(3)]
        depths = [behind[corner] for corner in corners]
        if not (min(depths) < split < max(depths)):
            continue
        a, b, c = (placed[corner] for corner in corners)
        u = [b[i] - a[i] for i in range(3)]
        v = [c[i] - a[i] for i in range(3)]
        normal = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
        length = sum(component * component for component in normal) ** 0.5
        if length > 0 and abs(normal[2]) / length > 0.01:
            raise SystemExit("The split crosses a face that is not a straight side wall.")
        walls += 1

    inverse = inverse3(world)
    shift = [inverse[row][2] * delta * -toward for row in range(3)]
    moved = 0
    for i in range(count):
        if behind[i] <= split:
            continue
        local[i] = [local[i][axis] + shift[axis] for axis in range(3)]
        struct.pack_into("<3f", binary, start + i * stride, *local[i])
        moved += 1

    accessor["min"] = [min(p[axis] for p in local) for axis in range(3)]
    accessor["max"] = [max(p[axis] for p in local) for axis in range(3)]

    extras = document["asset"].setdefault("extras", {})
    extras.setdefault("modifiedBy", "EmuShelf contributors")
    note = shell["note"]
    if note.strip() not in extras.get("modifications", ""):
        extras["modifications"] = (extras.get("modifications", "").rstrip() + note).strip()

    encoded = json.dumps(document, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    encoded += b" " * (-len(encoded) % 4)
    with open(path, "wb") as stream:
        stream.write(blob[:8])
        stream.write(struct.pack("<I", 12 + 8 + len(encoded) + 8 + len(binary)))
        stream.write(struct.pack("<I", len(encoded)))
        stream.write(blob[16:20])
        stream.write(encoded)
        stream.write(blob[binary_start - 8:binary_start])
        stream.write(binary)

    print(
        f"{name}: moved {moved} of {count} vertices back {delta / height:.4f} of its height across "
        f"{walls} side-wall triangles; thickness {size[2] / height:.4f} -> {target / height:.4f} "
        f"of height.")


if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1] not in SHELLS:
        raise SystemExit(f"usage: thicken_shell.py <{'|'.join(SHELLS)}> [path.glb]")
    main(*sys.argv[1:3])
