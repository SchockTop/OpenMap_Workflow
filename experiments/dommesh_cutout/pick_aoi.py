import json, collections
T = json.load(open("nodes_all.json"))
leaves = [r for r in T if r["mesh"] and not r["children"]]
# focus on FINE leaves: small footprint
fine = [r for r in leaves if r["hx"] < 120 and r["hy"] < 120 and r["hx"] > 0]
print("fine leaves (<120m halfsize):", len(fine), "of", len(leaves))
# grid 200m cells, count fine leaves per cell
CELL = 200.0
g = collections.Counter()
for r in fine:
    g[(int(r["cx"]//CELL), int(r["cy"]//CELL))] += 1
top = g.most_common(15)
for (gx, gy), n in top:
    print("cell e=%d n=%d  fine-leaves=%d" % (gx*CELL, gy*CELL, n))
# pick the densest cell, AOI = 300x300 centered on its centroid of fine leaves
best = top[0][0]
pts = [r for r in fine if int(r["cx"]//CELL)==best[0] and int(r["cy"]//CELL)==best[1]]
ecx = sum(p["cx"] for p in pts)/len(pts); ncy = sum(p["cy"] for p in pts)/len(pts)
half = 150.0
aoi = [ecx-half, ncy-half, ecx+half, ncy+half]
print("AOI (300x300m) bbox EPSG:25832:", [round(x,2) for x in aoi])
# how many leaves intersect it (xy rectangle overlap with node OBB-xy extent, ignoring rotation since quaternion identity)
def overlaps(r, a):
    return (r["cx"]+r["hx"] >= a[0] and r["cx"]-r["hx"] <= a[2] and
            r["cy"]+r["hy"] >= a[1] and r["cy"]-r["hy"] <= a[3])
sel = [r for r in leaves if overlaps(r, aoi)]
print("leaf nodes intersecting AOI:", len(sel), " total verts:", sum(r["vcount"] for r in sel))
sel_fine = [r for r in fine if overlaps(r, aoi)]
print("  of which fine:", len(sel_fine))
json.dump({"aoi": aoi, "leaf_indices": [r["i"] for r in sel]}, open("aoi.json","w"))
# also dump a few alternative AOIs from other dense cells for later
alts=[]
for (gx,gy),n in top[1:6]:
    pts=[r for r in fine if int(r["cx"]//CELL)==gx and int(r["cy"]//CELL)==gy]
    cx=sum(p["cx"] for p in pts)/len(pts); cy=sum(p["cy"] for p in pts)/len(pts)
    alts.append([cx-half,cy-half,cx+half,cy+half])
json.dump(alts, open("aoi_alts.json","w"))
