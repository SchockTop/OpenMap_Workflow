import urllib.request, struct, gzip, io, json

URL = "https://download1.bayernwolke.de/p/dom-mesh-slpk/125023_0/DSM_Mesh.slpk"

def rng(a, b):
    req = urllib.request.Request(URL, headers={"Range": f"bytes={a}-{b}"})
    return urllib.request.urlopen(req).read()

# total size
req = urllib.request.Request(URL, method="HEAD")
size = int(urllib.request.urlopen(req).headers["Content-Length"])
print("size", size)

# read last 64KB to find ZIP64 EOCD locator + EOCD
tail = rng(size-65536, size-1)
# EOCD64 locator signature: PK\x06\x07
i = tail.rfind(b"PK\x06\x07")
print("eocd64 locator at tail offset", i)
# locator: sig(4) disk(4) offset_of_zip64_eocd(8) total_disks(4)
disk, eocd64_ofs, ndisks = struct.unpack("<IQI", tail[i+4:i+4+16])
print("zip64 eocd offset", eocd64_ofs)

# read the zip64 EOCD record
ze = rng(eocd64_ofs, eocd64_ofs+56+200)
assert ze[:4] == b"PK\x06\x06", ze[:4]
# sig(4) size(8) vmade(2) vneed(2) disk(4) cddisk(4) entries_disk(8) entries(8) cdsize(8) cdoffset(8)
(rec_size, vmade, vneed, d1, d2, e_disk, e_tot, cd_size, cd_ofs) = struct.unpack("<QHHIIQQQQ", ze[4:4+52])
print("entries", e_tot, "cd_size", cd_size, "cd_ofs", cd_ofs)
