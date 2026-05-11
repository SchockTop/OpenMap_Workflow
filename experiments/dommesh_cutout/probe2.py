import urllib.request, struct, json, os, time, collections

URL = "https://download1.bayernwolke.de/p/dom-mesh-slpk/125023_0/DSM_Mesh.slpk"
def rng(a, b):
    req = urllib.request.Request(URL, headers={"Range": f"bytes={a}-{b}"})
    return urllib.request.urlopen(req).read()

CD_OFS = 99086700747
CD_SIZE = 62383194
if not os.path.exists("cd.bin"):
    t=time.time(); open("cd.bin","wb").write(rng(CD_OFS, CD_OFS+CD_SIZE-1)); print("downloaded CD in %.1fs"%(time.time()-t))
data = open("cd.bin","rb").read()
print("cd bytes", len(data))

entries = {}
p = 0; n = 0
while p+4 <= len(data) and data[p:p+4] == b"PK\x01\x02":
    (sig, vmade, vneed, flags, method, mtime, mdate, crc, csize, usize,
     fnlen, eflen, cmlen, dstart, iattr, eattr, lho) = struct.unpack("<IHHHHHHIIIHHHHHII", data[p:p+46])
    name = data[p+46:p+46+fnlen].decode("utf-8","replace")
    extra = data[p+46+fnlen:p+46+fnlen+eflen]
    off, cs, us = lho, csize, usize
    ep = 0
    while ep+4 <= len(extra):
        tag, sz = struct.unpack("<HH", extra[ep:ep+4]); body = extra[ep+4:ep+4+sz]; bp=0
        if tag == 0x0001:
            if us == 0xffffffff: us, = struct.unpack("<Q", body[bp:bp+8]); bp+=8
            if cs == 0xffffffff: cs, = struct.unpack("<Q", body[bp:bp+8]); bp+=8
            if off == 0xffffffff: off, = struct.unpack("<Q", body[bp:bp+8]); bp+=8
        ep += 4+sz
    entries[name] = [off, cs, us, method]
    p += 46+fnlen+eflen+cmlen; n += 1
print("entries parsed", n)
tops = collections.Counter(k.split("/")[0] for k in entries)
print("top-level dirs/files:", dict(tops))
for k in sorted(entries):
    if "/" not in k: print("ROOT:", k, entries[k])
nps = sorted(k for k in entries if k.startswith("nodepages/"))
print("nodepages:", len(nps), nps[:2], nps[-2:])
ex = [k for k in sorted(entries) if k.startswith("nodes/")][:8]
for e in ex: print(e, entries[e])
json.dump(entries, open("entries.json","w"))
