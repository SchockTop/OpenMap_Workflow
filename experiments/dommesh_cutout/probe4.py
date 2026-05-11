import json
e = json.load(open("entries.json"))
nps = {k:v for k,v in e.items() if k.startswith("nodepages/")}
offs = sorted((v[0], v[0]+v[1], k) for k,v in nps.items())
lo = offs[0][0]; hi = max(o[1] for o in offs)
tot = sum(v[1] for v in nps.values())
print("nodepages: n=%d  span=[%d..%d] = %d bytes,  sum(sizes)=%d  -> contiguous? %s"%(len(nps), lo, hi, hi-lo, tot, (hi-lo) < tot*1.5))
print("first few by offset:", offs[:3])
print("last few by offset:", offs[-3:])
# also: are node geometry files for a given node contiguous?
n10 = {k:v for k,v in e.items() if k.startswith("nodes/10/")}
print("node 10 files:", {k:(v[0],v[1]) for k,v in sorted(n10.items())})
# overall file layout: where do nodes/ live vs nodepages vs sceneLayer
alloffs = sorted(v[0] for v in e.values())
print("min off", alloffs[0], "max off", alloffs[-1], "file size 99149084039")
print("3dSceneLayer at", e["3dSceneLayer.json.gz"][0])
