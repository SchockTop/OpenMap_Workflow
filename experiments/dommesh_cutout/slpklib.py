import urllib.request, struct, gzip, json, os, time

class SLPK:
    def __init__(self, url, cd_path="cd.bin", entries_path="entries.json"):
        self.url = url
        self.entries = json.load(open(entries_path))  # name -> [local_off, csize, usize, method]
    def _rng(self, a, b):
        req = urllib.request.Request(self.url, headers={"Range": f"bytes={a}-{b}"})
        with urllib.request.urlopen(req) as r:
            return r.read()
    def read_raw(self, name):
        off, csize, usize, method = self.entries[name]
        # read local header (30 bytes fixed) then fn+extra
        hdr = self._rng(off, off+30+512)
        assert hdr[:4] == b"PK\x03\x04", (name, hdr[:4])
        fnlen, eflen = struct.unpack("<HH", hdr[26:30])
        data_start = off + 30 + fnlen + eflen
        blob = self._rng(data_start, data_start + csize - 1)
        assert len(blob) == csize, (name, len(blob), csize)
        return blob
    def read(self, name):
        blob = self.read_raw(name)
        if name.endswith(".gz"):
            return gzip.decompress(blob)
        return blob

URL = "https://download1.bayernwolke.de/p/dom-mesh-slpk/125023_0/DSM_Mesh.slpk"

if __name__ == "__main__":
    s = SLPK(URL)
    sl = json.loads(s.read("3dSceneLayer.json.gz"))
    json.dump(sl, open("3dSceneLayer.json","w"), indent=1)
    print("=== 3dSceneLayer keys:", list(sl.keys()))
    print("name:", sl.get("name"), "version:", sl.get("version"))
    print("spatialReference:", json.dumps(sl.get("spatialReference")))
    print("heightModelInfo:", sl.get("heightModelInfo"))
    print("store keys:", list(sl.get("store",{}).keys()))
    st = sl["store"]
    print("  store.version:", st.get("version"), "profile:", st.get("profile"))
    print("  store.index.nodesPerPage:", (sl.get("nodePages") or {}).get("nodesPerPage"), (st.get("index") or {}))
    print("  nodePages def:", sl.get("nodePages"))
    print("  defaultGeometrySchema:", json.dumps(st.get("defaultGeometrySchema"), indent=1)[:3000])
    print("  geometryDefinitions:", json.dumps(sl.get("geometryDefinitions"), indent=1)[:3000])
    print("  materialDefinitions:", json.dumps(sl.get("materialDefinitions"), indent=1)[:1500])
    print("  textureSetDefinitions:", json.dumps(sl.get("textureSetDefinitions"), indent=1)[:1500])
    # one node page
    np0 = json.loads(s.read("nodepages/0.json.gz"))
    json.dump(np0, open("nodepage0.json","w"), indent=1)
    print("=== nodepage 0: keys", list(np0.keys()), "num nodes:", len(np0.get("nodes",[])))
    for nd in np0["nodes"][:3]:
        print("  node:", json.dumps(nd))
