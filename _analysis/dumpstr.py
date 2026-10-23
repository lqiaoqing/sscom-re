import sys
from pathlib import Path
data=open(Path(__file__).resolve().parent.parent / "sscom5.13.1.unpacked.exe","rb").read()
def s(va,n=80):
    o=va-0x400000; seg=data[o:o+n]; e=seg.find(b'\x00')
    return seg[:e].decode('gbk','replace')
for va in (0x4eed0f,0x4eed18,0x4eed28,0x4eed39,0x4eed43,0x4eed5a,0x4eed68):
    print(hex(va), repr(s(va)))
