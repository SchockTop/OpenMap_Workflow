"""Fetch the contiguous nodepages block of a DOM-Mesh SLPK and build a flat node table."""
import urllib.request, struct, gzip, json, time, collections

URL = "https://download1.bayernwolke.de/p/dom-mesh-slpk/125023_0/DSM_Mesh.slpk"

def rng(a, b):
    req = urllib.request.Request(URL, headers={"Range": f"bytes={a}-{b}"})
    with urllib.request.urlopen(req) as r:
        return r.read()

def main():
    entries = json.load(open("entries.json"))  # name -> [local_off, csize, usize, method]
    nps = {k: v for k, v in entries.items() if k.startswith("nodepages/")}
    sl = entries["3dSceneLayer.json.gz"]
    block_lo = min(min(v[0] for v in nps.values()), sl[0])
    block_hi = max(v[0] + v[1] for v in nps.values()) + 4096  # pad for last local header
    block_hi = min(block_hi, 99149084039)
    t = time.time()
    buf = rng(block_lo, block_hi - 1)
    print("fetched %.1f MB in %.1fs" % (len(buf)/1e6, time.time() - t))

    def extract(name):
        off, csize, usize, method = entries[name]
        p = off - block_lo
        assert buf[p:p+4] == b"PK\x03\x04", (name, buf[p:p+4])
        fnlen, eflen = struct.unpack("<HH", buf[p+26:p+30])
        ds = p + 30 + fnlen + eflen
        data = buf[ds:ds+csize]
        assert len(data) == csize, (name, len(data), csize)
        if name.endswith(".gz"):
            return gzip.decompress(data)
        return data

    scene = json.loads(extract("3dSceneLayer.json.gz"))
    json.dump(scene, open("3dSceneLayer.json", "w"), indent=1)
    nper = scene["nodePages"]["nodesPerPage"]
    n_pages = len(nps)
    nodes = {}
    bad = 0
    for pi in range(n_pages):
        try:
            page = json.loads(extract(f"nodepages/{pi}.json.gz"))
        except Exception as e:
            bad += 1; print("bad page", pi, e); continue
        for nd in page["nodes"]:
            nodes[nd["index"]] = nd
    print("nodepages ok=%d bad=%d  total nodes=%d" % (n_pages-bad, bad, len(nodes)))

    table = []
    for idx in sorted(nodes):
        nd = nodes[idx]; obb = nd["obb"]
        c, h, q = obb["center"], obb["halfSize"], obb["quaternion"]
        has_mesh = "mesh" in nd
        rec = {"i": idx, "cx": c[0], "cy": c[1], "cz": c[2],
               "hx": h[0], "hy": h[1], "hz": h[2], "q": q,
               "lod": nd.get("lodThreshold", 0.0), "mesh": has_mesh,
               "children": bool(nd.get("children")),
               "geomRes": nd["mesh"]["geometry"]["resource"] if has_mesh else None,
               "matRes": nd["mesh"]["material"]["resource"] if has_mesh else None,
               "vcount": nd["mesh"]["geometry"]["vertexCount"] if has_mesh else 0}
        table.append(rec)
    json.dump(table, open("nodes_all.json", "w"))
    leaves = [r for r in table if r["mesh"] and not r["children"]]
    meshnodes = [r for r in table if r["mesh"]]
    print("mesh nodes=%d  leaf(mesh,no children)=%d" % (len(meshnodes), len(leaves)))
    for label, rs in [("ALL-mesh", meshnodes), ("leaves", leaves)]:
        hx = sorted(r["hx"] for r in rs); hy = sorted(r["hy"] for r in rs)
        print("  %-9s halfSize x[min %.0f med %.0f max %.0f] y[min %.0f med %.0f max %.0f]" %
              (label, hx[0], hx[len(hx)//2], hx[-1], hy[0], hy[len(hy)//2], hy[-1]))
    print("scene extent:", scene["store"]["extent"])

main()
