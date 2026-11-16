#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
rrs_tool.py -- Road Rash (1996, "暴力摩托") .RRS 存档解析/编辑工具

格式来源：Road Rash 存档读写代码（Roadrash.exe 0x42F000 保存 / 0x42F110 读取）
         与 EA 原版 shell 扩展 Rashprop.dll（存档属性页）的字段读取逻辑。

文件结构（固定 160 字节 = 0xA0）：
  0x00  u32   校验和（覆盖 0x04..0x9F 共 0x9C 字节，见 checksum()）
  0x04  u32   魔术字 0xCAFEDEAD（小端存为 AD DE FE CA）
  0x08  u8[32] 玩家档案块（= 游戏内存 0x4B8A10 起的 32 字节）
         +0x00 u8  未知/保留（通常 0）
         +0x01 u8  车手编号 1..8（对应文本表 8..15）
         +0x02 u8  级别 0..4（界面显示 +1）
         +0x03 u8  “有资格的”赛道位图（低 5 位）
         +0x04 u8  摩托车编号 0..14
         +0x05 u8  未知（1/2/5 ...）
         +0x06 u8  未知（通常 0）
         +0x07 u8  未知（通常 1）
         +0x08 i32 金钱
         +0x0C char[20] 玩家名称（ASCII/GBK，通常空）
  0x28  u8[120] AI/赛事状态：10 条 × 12 字节（每条 3 个 u32）
        （保存时从 AI 参数表 0x4C6B40 按车手槽位各拷 1 条，游戏中重新计算，与金钱/级别无关）
"""
import struct, sys, json, argparse, io

SIZE = 0xA0
MAGIC = 0xCAFEDEAD

RIDERS = ["戴维", "杰斐逊", "露西娅", "荘臣", "姆克迪", "塞利娜", "汤姆", "迈克"]
RIDERS_EN = ["Axle", "Bose Jefferson", "Cydney Bass", "Milwaukee Jon",
             "Pearl McKurdy", "Rhonda the Rash", "Slim Jim", "Teflon Mike"]
COURSES = ["城市街道", "内华达山脉", "城镇街道", "山丘公路", "沿海公路"]
COURSES_EN = ["City", "Sierra Nevada", "Peninsula", "Napa Valley", "Pacific Highway"]
BIKES = [
    "450cc Banzai Sport 450", "400cc Corsair Swallow", "250cc Kamikaze ZYX 250",
    "500cc Killer Rat", "125cc Perro Pico", "750/1100cc Banzai Super Sport",
    "600cc Corsair Raven N", "1000cc Diablo Vipera N", "750cc Kamikaze ZYX 750N",
    "900cc Stiletto Assassino N", "750cc Diablo Vampiro", "1000cc DMG M10",
    "750cc Kamikaze ZYX 750", "250cc Perro Grande", "600cc Stiletto Aggressore",
]


def checksum(data: bytes) -> int:
    """与游戏 0x42EFC0 / Rashprop.dll 0x100018F0 完全一致的校验和。
    data 为 0x04..0x9F 的 0x9C 字节（含魔术字）。"""
    n = len(data)
    plain = 0
    weighted = 0
    alt = 0
    for i, b in enumerate(data):
        rem = n - i            # 汇编里用递减的 ecx 做乘数 / 判奇偶
        plain += b
        weighted += b * rem
        if rem & 1:
            alt += b
        else:
            alt -= b
    return (((alt << 16) & 0xFFFF0000) | (plain & 0xFFFF)) ^ weighted


def read_name(raw: bytes) -> str:
    raw = raw.split(b"\x00", 1)[0]
    for enc in ("gbk", "ascii", "latin-1"):
        try:
            return raw.decode(enc)
        except Exception:
            pass
    return raw.hex()


def parse(path: str) -> dict:
    b = open(path, "rb").read()
    if len(b) != SIZE:
        raise SystemExit("文件长度 %d != 160，不是标准 RRS 存档" % len(b))
    stored, magic = struct.unpack_from("<II", b, 0)
    rider = b[9]
    level0 = b[10]
    mask = b[11]
    bike = b[12]
    cash = struct.unpack_from("<i", b, 0x10)[0]
    name = read_name(b[0x14:0x28])
    qual = [COURSES[i] for i in range(5) if mask >> i & 1]
    ai = []
    for k in range(10):
        o = 0x28 + k * 12
        ai.append(struct.unpack_from("<III", b, o))
    return {
        "file": path,
        "stored_checksum": stored,
        "computed_checksum": checksum(b[4:]),
        "checksum_ok": stored == checksum(b[4:]),
        "magic_ok": magic == MAGIC,
        "rider_id": rider,
        "rider": RIDERS[rider - 1] if 1 <= rider <= 8 else "?",
        "rider_en": RIDERS_EN[rider - 1] if 1 <= rider <= 8 else "?",
        "level": level0 + 1,
        "level_raw": level0,
        "qualified_mask": mask,
        "qualified": qual,
        "bike_id": bike,
        "bike": BIKES[bike] if 0 <= bike < 15 else "?",
        "cash": cash,
        "name": name,
        "profile_extra": b[8], "unknown5": b[13], "unknown6": b[14], "unknown7": b[15],
        "ai_records": ai,
    }


def report(d: dict) -> str:
    L = []
    L.append("文件      : %s" % d["file"])
    L.append("校验和    : 存储 %08X / 计算 %08X  -> %s" %
             (d["stored_checksum"], d["computed_checksum"], "OK" if d["checksum_ok"] else "不匹配"))
    L.append("魔术字    : %s" % ("0xCAFEDEAD OK" if d["magic_ok"] else "错误"))
    L.append("车手      : %d = %s (%s)" % (d["rider_id"], d["rider"], d["rider_en"]))
    L.append("级别      : %d (存档原始值 %d，界面显示 +1)" % (d["level"], d["level_raw"]))
    L.append("金钱      : %d" % d["cash"])
    L.append("摩托车    : %d = %s" % (d["bike_id"], d["bike"]))
    L.append("资格赛道  : 0x%02X -> %s" % (d["qualified_mask"],
                                           "、".join(d["qualified"]) if d["qualified"] else "无"))
    L.append("玩家名称  : %r" % d["name"])
    L.append("未知字节  : +0x08=%d +0x0D=%d +0x0E=%d +0x0F=%d" %
             (d["profile_extra"], d["unknown5"], d["unknown6"], d["unknown7"]))
    L.append("AI 状态   : 10 条 × 12 字节")
    for k, rec in enumerate(d["ai_records"]):
        L.append("   #%d  %08X %08X %08X" % (k, rec[0], rec[1], rec[2]))
    return "\n".join(L)


def rebuild(b: bytearray) -> bytearray:
    b = bytearray(b)
    struct.pack_into("<I", b, 4, MAGIC)
    struct.pack_into("<I", b, 0, checksum(bytes(b[4:])))
    return b


def main():
    ap = argparse.ArgumentParser(description="Road Rash .RRS 存档解析/编辑")
    ap.add_argument("file")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--set", action="append", default=[], metavar="FIELD=VALUE",
                    help="cash/level/bike/rider/qualified/name")
    ap.add_argument("-o", "--out", help="修改后另存为（默认覆盖原文件）")
    ap.add_argument("--fix", action="store_true", help="仅重算校验和")
    a = ap.parse_args()

    d = parse(a.file)
    if a.json:
        print(json.dumps(d, ensure_ascii=False, indent=1))
    else:
        print(report(d))

    if a.set or a.fix:
        b = bytearray(open(a.file, "rb").read())
        for kv in a.set:
            k, v = kv.split("=", 1)
            if k == "cash":
                struct.pack_into("<i", b, 0x10, int(v))
            elif k == "level":
                b[10] = int(v) - 1
            elif k == "rider":
                b[9] = int(v)
            elif k == "bike":
                b[12] = int(v)
            elif k == "qualified":
                b[11] = int(v, 0)
            elif k == "name":
                raw = v.encode("gbk")[:20]
                b[0x14:0x28] = raw + b"\x00" * (20 - len(raw))
            else:
                raise SystemExit("未知字段 %s" % k)
        b = rebuild(b)
        out = a.out or a.file
        open(out, "wb").write(bytes(b))
        print("\n已写入 %s（校验和已重算）" % out)


if __name__ == "__main__":
    main()
