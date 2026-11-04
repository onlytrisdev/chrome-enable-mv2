"""Read-only MV2 locator experiment, NOT a patch authorizer.

Requires capstone and pefile. No Chrome-version table, stored offsets, or
version-specific byte signatures are used. ISA-level prefilters only generate
leads; decoded CFG features do not establish behavioral equivalence.
"""
from __future__ import annotations

import argparse
import bisect
import hashlib
import json
import re
import struct
import time
from pathlib import Path

import capstone as cs
import pefile
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_OP_REG

ERROR_TEXT = "Cannot install extension because it uses an unsupported manifest version."


def read_pak(data: bytes) -> dict[int, bytes]:
    """Parse Chromium GRIT v4/v5 packs, including aliases, with bounds checks."""
    if len(data) < 9:
        raise ValueError("Truncated PAK header")
    version, = struct.unpack_from("<I", data)
    if version == 4:
        count, encoding = struct.unpack_from("<IB", data, 4)
        header, aliases = 9, 0
    elif version == 5 and len(data) >= 12:
        encoding, count, aliases = struct.unpack_from("<BxxxHH", data, 4)
        header = 12
    else:
        raise ValueError("Unsupported/truncated PAK version")
    end_table = header + (count + 1) * 6 + aliases * 4
    if end_table > len(data) or encoding not in (1, 2):
        raise ValueError("Invalid PAK index/encoding")
    rows = [struct.unpack_from("<HI", data, header + i * 6) for i in range(count + 1)]
    result = {}
    for i, (rid, start) in enumerate(rows[:-1]):
        end = rows[i + 1][1]
        if not end_table <= start <= end <= len(data) or rid in result:
            raise ValueError("Invalid PAK resource bounds/identity")
        # Encoding is a pack declaration, not a guarantee every entry is text.
        # Mixed/compressed resources must not be decoded as arbitrary strings.
        result[rid] = data[start:end]
    for i in range(aliases):
        rid, index = struct.unpack_from("<HH", data, header + (count + 1) * 6 + i * 4)
        if index >= count or rid in result:
            raise ValueError("Invalid PAK alias")
        result[rid] = result[rows[index][0]]
    return result


class Probe:
    def __init__(self, dll: Path):
        self.path = dll.resolve()
        self.data = self.path.read_bytes()
        self.pe = pefile.PE(data=self.data, fast_load=True)
        if self.pe.FILE_HEADER.Machine != 0x8664:
            raise ValueError("This experiment supports x64 PE only")
        self.text = next(s for s in self.pe.sections if s.Name.rstrip(b"\0") == b".text")
        self.raw = self.text.PointerToRawData
        self.rva = self.text.VirtualAddress
        self.code = self.data[self.raw:self.raw + self.text.SizeOfRawData]
        self.decoder = cs.Cs(cs.CS_ARCH_X86, cs.CS_MODE_64)
        self.decoder.detail = True
        directory = self.pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
        off = self.pe.get_offset_from_rva(directory.VirtualAddress)
        self.functions = list(struct.iter_unpack("<III", self.data[off:off + directory.Size]))
        self.starts = [row[0] for row in self.functions]
        self.boundaries = {}

    def instruction(self, rva):
        index = rva - self.rva
        if index < 0 or index >= len(self.code):
            return None
        return next(self.decoder.disasm(self.code[index:index + 15], rva, count=1), None)

    def containing(self, rva):
        i = bisect.bisect_right(self.starts, rva) - 1
        if i >= 0 and self.functions[i][0] <= rva < self.functions[i][1]:
            return self.functions[i][:2]
        return None  # Leaf functions are legitimately absent from .pdata.

    def boundary_status(self, rva):
        owner = self.containing(rva)
        if owner:
            begin, end = owner
            if end - begin > 64 * 1024:
                return "unverified-large-function"
            if begin not in self.boundaries:
                index = begin - self.rva
                self.boundaries[begin] = {
                    i.address for i in self.decoder.disasm(self.code[index:index + end - begin], begin)
                }
            return "pdata-linear-decode" if rva in self.boundaries[begin] else "rejected"
        index = rva - self.rva
        if index == 0 or self.code[index - 1] == 0xCC:
            return "padding-adjacent-leaf-candidate"
        # Leaf functions can initialize a return register before the comparison.
        for back in range(2, min(index, 16) + 1):
            if self.code[index - back] != 0xCC:
                continue
            prefix = list(self.decoder.disasm(self.code[index - back + 1:index], rva - back + 1))
            if (prefix and prefix[-1].address + prefix[-1].size == rva
                    and all(i.mnemonic not in ("int3", "ret", "jmp") for i in prefix)):
                return "padding-derived-leaf-candidate"
            break
        return "unverified"

    def cfg(self, start, budget=160, window=4096):
        pending, nodes, complete = [start], {}, True
        while pending and len(nodes) < budget:
            address = pending.pop()
            if address in nodes:
                continue
            if not start - 256 <= address < start + window:
                complete = False
                continue
            ins = self.instruction(address)
            if ins is None or ins.mnemonic in ("int3", "ud2"):
                complete = False
                continue
            nodes[address] = ins
            if ins.group(cs.CS_GRP_RET):
                continue
            if ins.group(cs.CS_GRP_JUMP):
                if ins.operands and ins.operands[0].type == X86_OP_IMM:
                    pending.append(ins.operands[0].imm)
                else:
                    complete = False
                if ins.mnemonic == "jmp":
                    continue
            pending.append(address + ins.size)
        return nodes, complete and not pending

    def immediate_sites(self, value):
        """ISA-prefilter, then require MOV-immediate and a verified boundary."""
        needle = struct.pack("<I", value)
        pos = 0
        while True:
            pos = self.code.find(needle, pos)
            if pos < 0:
                return
            for prefix in (2, 1):
                start = pos - prefix
                if start < 0:
                    continue
                ins = self.instruction(self.rva + start)
                if (ins and ins.mnemonic == "mov" and len(ins.operands) == 2
                        and ins.operands[0].type == X86_OP_REG
                        and ins.operands[1].type == X86_OP_IMM
                        and ins.operands[1].imm == value
                        and start + ins.size == pos + 4
                        and self.boundary_status(ins.address) == "pdata-linear-decode"):
                    yield ins
                    break
            pos += 1

    def gate_leads(self, resource_functions):
        # CMP r/m32, imm8(2), mod=disp8/disp32; not a Chrome signature.
        prefilter = rb"\x83[\x78-\x7f].\x02|\x83[\xb8-\xbf].{4}\x02|\x83[\xf8-\xff]\x02"
        accepted, examined = [], 0
        for match in re.finditer(prefilter, self.code, re.DOTALL):
            examined += 1
            position = match.start()
            if position and 0x40 <= self.code[position - 1] <= 0x4f:
                position -= 1
            start = self.rva + position
            first = self.instruction(start)
            if not (first and first.mnemonic == "cmp" and len(first.operands) == 2
                    and first.operands[0].type in (X86_OP_MEM, X86_OP_REG) and first.operands[0].size == 4
                    and first.operands[1].type == X86_OP_IMM and first.operands[1].imm == 2):
                continue
            branch = self.instruction(start + first.size)
            if not branch or branch.mnemonic != "jg":
                continue
            boundary = self.boundary_status(start)
            if boundary not in ("pdata-linear-decode", "padding-adjacent-leaf-candidate", "padding-derived-leaf-candidate"):
                continue
            nodes, complete = self.cfg(start)
            compares = {}
            for ins in nodes.values():
                if (ins.mnemonic == "cmp" and len(ins.operands) == 2
                        and ins.operands[0].type == X86_OP_REG and ins.operands[1].type == X86_OP_IMM):
                    key = ins.reg_name(ins.operands[0].reg)
                    compares.setdefault(key, set()).add(ins.operands[1].imm)
            type_regs = [reg for reg, values in compares.items() if {1, 3, 8} <= values]
            location_regs = [reg for reg, values in compares.items() if {5, 10} <= values]
            # An optimized type check can use BT over a bitset instead of three CMPs.
            type_masks = []
            for ins in nodes.values():
                if (ins.mnemonic == "mov" and len(ins.operands) == 2
                        and ins.operands[0].type == X86_OP_REG and ins.operands[1].type == X86_OP_IMM):
                    mask = ins.operands[1].imm
                    if 0 <= mask <= 0xffff and {bit for bit in range(16) if mask & (1 << bit)} == {1, 3, 8}:
                        mask_reg = ins.operands[0].reg
                        if any(i.mnemonic == "bt" and i.operands[0].type == X86_OP_REG
                               and i.operands[0].reg == mask_reg for i in nodes.values()):
                            type_masks.append(hex(mask))
            if not (type_regs or type_masks) or not location_regs or not branch or branch.mnemonic != "jg":
                continue
            owner = self.containing(start)
            calls = sum(ins.group(cs.CS_GRP_CALL) for ins in nodes.values())
            immediates = {op.imm for ins in nodes.values() for op in ins.operands if op.type == X86_OP_IMM}
            role = ("integer-impact-checker" if first.operands[0].type == X86_OP_REG and type_masks and complete and not calls else
                    "installation-policy" if owner and owner[0] in resource_functions else
                    "impact-checker" if complete and not calls and any(i.mnemonic == "setne" for i in nodes.values()) else
                    "disable-or-reenable-clone" if 0x800000 in immediates else "unclassified")
            accepted.append({
                "compare_rva": hex(start), "branch_rva": hex(branch.address),
                "branch_target_rva": hex(branch.operands[0].imm),
                "manifest_field_displacement": hex(first.operands[0].mem.disp) if first.operands[0].type == X86_OP_MEM else None,
                "role_lead": role, "boundary_evidence": boundary,
                "function_range": [hex(x) for x in owner] if owner else None,
                "type_registers": type_regs, "type_bit_masks": type_masks, "location_registers": location_regs,
                "cfg_complete_within_budget": complete, "decoded_nodes": len(nodes),
            })
        return accepted, examined

    def run(self, pak: Path):
        begin = time.perf_counter()
        pack = pak.read_bytes()
        anchors = (ERROR_TEXT.encode("utf-8"), ERROR_TEXT.encode("utf-16-le"))
        ids = [rid for rid, value in read_pak(pack).items() if value in anchors]
        if len(ids) != 1:
            raise ValueError(f"Expected one exact English error anchor; found {len(ids)}")
        rid = ids[0]
        resource_sites = list(self.immediate_sites(rid))
        owners = {self.containing(i.address)[0] for i in resource_sites if self.containing(i.address)}
        gates, examined = self.gate_leads(owners)
        reasons = list(self.immediate_sites(0x800000))
        return {
            "mode": "READ_ONLY_RESEARCH_NOT_PATCH_AUTHORIZATION", "dll": str(self.path),
            "dll_sha256": hashlib.sha256(self.data).hexdigest(), "pak": str(pak.resolve()),
            "pak_sha256": hashlib.sha256(pack).hexdigest(), "resource_id_discovered": rid,
            "resource_load_sites": [{"rva": hex(i.address), "assembly": i.mnemonic + " " + i.op_str,
                                      "function_range": [hex(x) for x in self.containing(i.address)]}
                                     for i in resource_sites],
            "isa_compare_prefilter_hits": examined, "semantic_gate_leads": gates,
            "unsupported_manifest_reason_sites": len(reasons),
            "reason_function_count": len({self.containing(i.address) for i in reasons}),
            "scan_seconds_excluding_pe_load": round(time.perf_counter() - begin, 3),
            "limitations": [
                "Features identify leads, not proof of intended behavior or a complete patch set.",
                "Only x64 CMP forms covered by the ISA prefilter and JG gates are explored.",
                "English text is an anchor; wording changes or absent English packs cause rejection.",
                "Linear decoding/padding proximity is not universal instruction-boundary proof.",
                "No startup-loop selection, symbolic execution, or runtime test here.",
                "No process is launched, attached, stopped, or modified; no target bytes are written.",
            ],
        }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=Path, required=True)
    parser.add_argument("--pak", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    # Never permit the optional report to overwrite an analysis input.
    if args.output and args.output.resolve() in (args.dll.resolve(), args.pak.resolve()):
        parser.error("Report path must not be an input path")
    result = Probe(args.dll).run(args.pak)
    text = json.dumps(result, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("x", encoding="utf-8") as stream:
            stream.write(text + "\n")
    print(text)


if __name__ == "__main__":
    main()
