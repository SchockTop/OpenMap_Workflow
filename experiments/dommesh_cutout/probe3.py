import json, struct
from slpklib import SLPK, URL
s = SLPK(URL)
sl = json.load(open("3dSceneLayer.json"))
st = sl["store"]
print("vertexCRS:", st.get("vertexCRS"), "indexCRS:", st.get("indexCRS"))
print("normalReferenceFrame:", st.get("normalReferenceFrame"), "textureEncoding:", st.get("textureEncoding"))
print("lodType:", st.get("lodType"), "lodModel:", st.get("lodModel"))
print("extent:", st.get("extent"))
print("rootNode:", st.get("rootNode"), "resourcePattern:", st.get("resourcePattern"))

# decode node 1 geometry 0
blob = s.read("nodes/1/geometries/0.bin.gz")
print("geom blob bytes:", len(blob))
vcount, fcount = struct.unpack("<II", blob[:8])
print("vertexCount", vcount, "featureCount", fcount)
p = 8
pos = struct.unpack("<%df"%(vcount*3), blob[p:p+vcount*12]); p+=vcount*12
uv  = struct.unpack("<%df"%(vcount*2), blob[p:p+vcount*8]); p+=vcount*8
rest = len(blob)-p
print("bytes left after pos+uv:", rest, " expect featureAttrs: id(8)*fc + faceRange(8)*fc =", fcount*16)
xs=pos[0::3]; ys=pos[1::3]; zs=pos[2::3]
print("X range %.2f..%.2f  Y %.2f..%.2f  Z %.2f..%.2f"%(min(xs),max(xs),min(ys),max(ys),min(zs),max(zs)))
print("UV range %.3f..%.3f , %.3f..%.3f"%(min(uv[0::2]),max(uv[0::2]),min(uv[1::2]),max(uv[1::2])))
print("first 3 verts:", pos[:9])
# node1 obb center was [684041.80, 5512021.33, 449.69]
