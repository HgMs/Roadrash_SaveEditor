# RRS 存档格式

> 目标游戏：Road Rash（1996, PC）——国内常称「暴力摩托 2002 / 暴力摩托」。
> 存档文件：`<游戏目录>\Save\*.RRS`，固定 **160 字节（0xA0）**。

本文结论来自两条独立证据链，互相印证：

1. **反汇编游戏本体**：`Roadrash.exe` 带 ASPack 壳，运行时 dump 内存拿到解壳映像，
   定位到读档 `0x42F110`、存档 `0x42F000`、校验和 `0x42EFC0`（模块基址 `0x400000`）。
2. **EA 原版 shell 扩展 `Rashprop.dll`**（"Road Rash Save File property sheet handler"）：
   它是官方读档显示工具，字段偏移与上面完全对应。

另外三个真实存档的校验和全部命中，可作为算法正确性的回归向量。

---

## 1. 整体结构

| 偏移 | 长度 | 类型 | 内容 |
|---|---|---|---|
| `0x00` | 4 | u32 LE | 校验和（覆盖 `0x04..0x9F`，共 156 字节） |
| `0x04` | 4 | u32 LE | 魔术字 `0xCAFEDEAD`（文件里是 `AD DE FE CA`） |
| `0x08` | 32 | 结构 | 玩家档案块（对应游戏内存 `0x4B8A10` 起 32 字节） |
| `0x28` | 120 | 数组 | AI / 赛事状态：10 条 × 12 字节 |

存档时的实际行为（`0x42F000`）：

```
CreateFile → 清零 160 字节缓冲
           → 拷入 32 字节档案（源 0x4B8A10）
           → 拷入 10×12 字节 AI 记录（源 0x4C6B40，块步长 0x204）
           → 写入魔术字 0xCAFEDEAD
           → 计算校验和并写入首 dword
           → WriteFile 160 字节
```

读档时反过来，并校验魔术字与校验和。

---

## 2. 玩家档案块（`0x08`–`0x27`）

| 偏移 | 类型 | 含义 | 说明 |
|---|---|---|---|
| `0x08` | u8 | 保留 | 通常 0 |
| `0x09` | u8 | **车手编号 1–8** | 1=戴维(Axle) … 7=汤姆(Slim Jim) |
| `0x0A` | u8 | **级别（0 基）** | 界面显示 `+1`，即 0–4 ↔ 1–5 |
| `0x0B` | u8 | **已通关/有资格赛道位图** | 低 5 位，对应 5 条赛道 |
| `0x0C` | u8 | **摩托车编号 0–14** | 15 台 |
| `0x0D` | u8 | 未知 | 观测值 1/2/5 |
| `0x0E` | u8 | 未知 | 通常 0 |
| `0x0F` | u8 | 未知 | 通常 1 |
| `0x10` | i32 | **金钱** | 直接整数 |
| `0x14` | char[20] | 玩家名称 | ASCII/GBK，通常为空（游戏用文件名当名字） |

车手编号与文本表（资源 id = `7 + 编号`）对应：

| 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|
| 戴维 / Axle | 杰斐逊 / Bose Jefferson | 露西娅 / Cydney Bass | 荘臣 / Milwaukee Jon | 姆克迪 / Pearl McKurdy | 塞利娜 / Rhonda the Rash | 汤姆 / Slim Jim | 迈克 / Teflon Mike |

赛道位（`0x0B`）：

| bit0 | bit1 | bit2 | bit3 | bit4 |
|---|---|---|---|---|
| 城市街道 / City | 内华达山脉 / Sierra Nevada | 城镇街道 / Peninsula | 山丘公路 / Napa Valley | 沿海公路 / Pacific Highway |

摩托车（`0x0C`）：

| 0 | 1 | 2 | 3 | 4 |
|---|---|---|---|---|
| 450cc Banzai Sport 450 | 400cc Corsair Swallow | 250cc Kamikaze ZYX 250 | 500cc Killer Rat | 125cc Perro Pico |

| 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|
| 750/1100cc Banzai Super Sport | 600cc Corsair Raven N | 1000cc Diablo Vipera N | 750cc Kamikaze ZYX 750N | 900cc Stiletto Assassino N |

| 10 | 11 | 12 | 13 | 14 |
|---|---|---|---|---|
| 750cc Diablo Vampiro | 1000cc DMG M10 | 750cc Kamikaze ZYX 750 | 250cc Perro Grande | 600cc Stiletto Aggressore |

---

## 3. 校验和

```python
def checksum(data: bytes) -> int:
    # data = 文件 0x04..0x9F，共 156 字节（含魔术字）
    n = len(data)
    plain = weighted = alt = 0
    for i, b in enumerate(data):
        rem = n - i
        plain    += b
        weighted += b * rem
        alt      += b if (rem & 1) else -b
    return (((alt << 16) & 0xFFFF0000) | (plain & 0xFFFF)) ^ weighted
```

对应汇编（`0x42EFC0`，fastcall：`ecx`=指针、`edx`=0x9C）：

```asm
loop:  mov  bl, [ecx]
       add  edi, ebx          ; plain
       imul ebp, ebx, edx     ; b * 剩余长度
       add  esi, ebp          ; weighted
       test dl, 1
       jz   minus
       add  eax, ebx          ; alt += b
       jmp  next
minus: sub  eax, ebx          ; alt -= b
next:  inc  ecx
       dec  edx
       jnz  loop
       shl  eax, 16
       and  edi, 0xFFFF
       or   eax, edi
       xor  eax, esi
```

**验证向量**（三份真实存档，全部一致）：

| 文件 | 头部存储 | 计算值 |
|---|---|---|
| 戴维.RRS | `0x002B826F` | `0x002B826F` |
| 汤姆.RRS | `0x017B5175` | `0x017B5175` |
| 962.RRS | `0x01F422F8` | `0x01F422F8` |

> 改了内容不重算校验和：游戏读档会拒绝，Windows 属性页会显示「伪造的游戏进度文件」。

---

## 4. AI / 赛事状态（`0x28`–`0x9F`）

120 字节 = 10 条 × 12 字节，每条 3 个 u32。

- 存档时从内存 `0x4C6B40` 的 AI 参数表按「当前车手槽位」各拷 1 条（源块步长 `0x204`）；
- 读档时写回同一张表，游戏随后自行重算；
- **与本工具要改的金钱/摩托车/关卡无关**，属于对手与赛事进度状态，因此工具只读不动。

样例（戴维.RRS）：

```
#0 00002000 00000000 00007000      #5 00000000 00000000 00007DCA
#1 00000000 00000000 0000A8F0      #6 00000000 00000000 00005000
#2 00000000 00000000 000062FC      #7 00000000 00000000 0000B274
#3 00000000 00000000 00005B86      #8 00000000 00000000 00000000
#4 00000000 00000000 00000D66      #9 00000000 00000000 00009C40
```

---

## 5. 修改示例

把「金钱」改成 999999 并保持校验和正确：

```python
import struct

def checksum(data):
    n = len(data); plain = weighted = alt = 0
    for i, b in enumerate(data):
        rem = n - i
        plain += b; weighted += b * rem
        alt += b if (rem & 1) else -b
    return (((alt << 16) & 0xFFFF0000) | (plain & 0xFFFF)) ^ weighted

b = bytearray(open("戴维.RRS", "rb").read())
assert len(b) == 160 and struct.unpack_from("<I", b, 4)[0] == 0xCAFEDEAD

struct.pack_into("<i", b, 0x10, 999999)          # 金钱
struct.pack_into("<I", b, 0x00, checksum(b[4:]))  # 重算校验和
open("戴维_new.RRS", "wb").write(b)
```

---

## 6. 兼容性

- 只要文件仍是 **160 字节**、魔术字 `0xCAFEDEAD`、校验和规则一致，就是同一结构。
- 已确认：中文硬盘版（2002 打包）、英文原版存档结构相同。
- 其它版本（如 Steam 30 周年）如果字段有偏移，工具会因「大小/魔术字/校验和」不符而拒绝写入，不会破坏原档。
