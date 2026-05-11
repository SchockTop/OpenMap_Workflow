"""DOM-Mesh polygon cutout: given a center (E,N) in EPSG:25832 and a half-size (m),
fetch only the I3S leaf nodes overlapping that AOI via HTTP range requests, decode the
uncompressed I3S triangle geometry + JPEG textures, and write a Blender-ready OBJ.

Usage: python cutout.py <out_dir> <cx> <cy> [half_m=150]
"""
import sys, os, json, struct, gzip, urllib.request, time

URL = "https://download1.bayernwolke.de/p/dom-mesh-slpk/125023_0/DSM_Mesh.slpk"
ENTRIES = json.load(open("entries.json"))   # name -> [local_off, csize, usize, method]
NODES   = {r["i"]: r for r in json.load(open("nodes_all.json"))}

_bytes_fetched = 0
def rng(a, b):
    global _bytes_fetched
    req = urllib.request.Request(URL, headers={"Range": f"bytes={a}-{b}"})
    with urllib.request.urlopen(req) as r:
        d = r.read()
    _bytes_fetched += len(d)
    return d

def fetch_entry(name):
    off, csize, usize, method = ENTRIES[name]
    hdr = rng(off, off + 30 + 256)               # local header + name + (small) extra
    assert hdr[:4] == b"PK\x03\x04", (name, hdr[:4])
    fnlen, eflen = struct.unpack("<HH", hdr[26:30])
    ds = off + 30 + fnlen + eflen
    data = rng(ds, ds + csize - 1)
    assert len(data) == csize, (name, len(data), csize)
    return gzip.decompress(data) if name.endswith(".gz") else data

def decode_geometry(blob):
    """I3S meshpyramids, PerAttributeArray, ordering [position(f32x3), uv0(f32x2)]."""
    vcount, fcount = struct.unpack("<II", blob[:8])
    p = 8
    pos = struct.unpack("<%df" % (vcount * 3), blob[p:p + vcount * 12]); p += vcount * 12
    uv  = struct.unpack("<%df" % (vcount * 2), blob[p:p + vcount * 8]);  p += vcount * 8
    return vcount, pos, uv

def aabb_overlap(r, a):
    return (r["cx"] + r["hx"] >= a[0] and r["cx"] - r["hx"] <= a[2] and
            r["cy"] + r["hy"] >= a[1] and r["cy"] - r["hy"] <= a[3])

def main():
    out, cx, cy = sys.argv[1], float(sys.argv[2]), float(sys.argv[3])
    half = float(sys.argv[4]) if len(sys.argv) > 4 else 150.0
    aoi = [cx - half, cy - half, cx + half, cy + half]
    os.makedirs(os.path.join(out, "tex"), exist_ok=True)

    leaves = [r for r in NODES.values() if r["mesh"] and not r["children"] and aabb_overlap(r, aoi)]
    leaves.sort(key=lambda r: r["i"])
    print("AOI %s  -> %d leaf nodes" % ([round(v, 1) for v in aoi], len(leaves)))
    if not leaves:
        print("nothing here"); return

    anchor = (cx, cy)  # Blender-local origin
    t0 = time.time()
    obj_lines = ["mtllib cutout.mtl"]
    mtl_lines = []
    vbase = 0
    kept = 0
    for r in leaves:
        idx = r["i"]; gres = r["geomRes"]; mres = r["matRes"]
        try:
            g = fetch_entry(f"nodes/{gres}/geometries/0.bin.gz")
            tex = fetch_entry(f"nodes/{mres}/textures/0.jpg")
        except Exception as e:
            print("  skip node", idx, e); continue
        vcount, pos, uv = decode_geometry(g)
        if vcount == 0:
            continue
        ocx, ocy, ocz = r["cx"], r["cy"], r["cz"]
        wx = [ocx + pos[3*k]   for k in range(vcount)]
        wy = [ocy + pos[3*k+1] for k in range(vcount)]
        wz = [ocz + pos[3*k+2] for k in range(vcount)]
        # keep a triangle if its centroid is inside the AOI rectangle
        tris = []
        for ti in range(vcount // 3):
            i0, i1, i2 = 3*ti, 3*ti+1, 3*ti+2
            ccx = (wx[i0]+wx[i1]+wx[i2])/3.0; ccy = (wy[i0]+wy[i1]+wy[i2])/3.0
            if aoi[0] <= ccx <= aoi[2] and aoi[1] <= ccy <= aoi[3]:
                tris.append((i0, i1, i2))
        if not tris:
            continue
        used = sorted({v for t in tris for v in t})
        remap = {v: n for n, v in enumerate(used)}
        texname = f"node_{idx}.jpg"
        open(os.path.join(out, "tex", texname), "wb").write(tex)
        mname = f"m{idx}"
        mtl_lines += [f"newmtl {mname}", "Ka 1 1 1", "Kd 1 1 1", "d 1", "illum 1",
                      f"map_Kd tex/{texname}", ""]
        obj_lines.append(f"o node_{idx}")
        for v in used:
            obj_lines.append("v %.4f %.4f %.4f" % (wx[v]-anchor[0], wy[v]-anchor[1], wz[v]))
        for v in used:
            obj_lines.append("vt %.6f %.6f" % (uv[2*v], 1.0 - uv[2*v+1]))
        obj_lines.append(f"usemtl {mname}")
        for (i0, i1, i2) in tris:
            a = vbase + remap[i0] + 1; b = vbase + remap[i1] + 1; c = vbase + remap[i2] + 1
            obj_lines.append(f"f {a}/{a} {b}/{b} {c}/{c}")
        vbase += len(used)
        kept += 1
        if kept % 10 == 0:
            print("  %d/%d nodes, %.1f MB fetched" % (kept, len(leaves), _bytes_fetched/1e6))

    open(os.path.join(out, "cutout.obj"), "w").write("\n".join(obj_lines) + "\n")
    open(os.path.join(out, "cutout.mtl"), "w").write("\n".join(mtl_lines) + "\n")
    meta = {"slpk": URL, "aoi_epsg25832": aoi, "anchor_epsg25832": anchor,
            "half_m": half, "leaf_nodes": kept, "vertices": vbase,
            "triangles": vbase // 3, "bytes_fetched": _bytes_fetched,
            "seconds": round(time.time() - t0, 1)}
    json.dump(meta, open(os.path.join(out, "meta.json"), "w"), indent=1)
    print(json.dumps(meta, indent=1))

main()
