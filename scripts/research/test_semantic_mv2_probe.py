"""Synthetic tests: these do not establish Chrome runtime compatibility."""
import struct
import unittest

import capstone as cs

from semantic_mv2_probe import ERROR_TEXT, Probe, read_pak


def pack5(resources, aliases=()):
    header = struct.pack("<IBxxxHH", 5, 1, len(resources), len(aliases))
    offset = 12 + (len(resources) + 1) * 6 + len(aliases) * 4
    rows, payload = [], b""
    for rid, data in resources:
        rows.append(struct.pack("<HI", rid, offset))
        payload += data
        offset += len(data)
    return header + b"".join(rows) + struct.pack("<HI", 0, offset) + b"".join(
        struct.pack("<HH", *alias) for alias in aliases) + payload


def leaf(hex_bytes):
    probe = object.__new__(Probe)
    probe.code = b"\xcc" + bytes.fromhex(hex_bytes)
    probe.rva, probe.starts, probe.functions, probe.boundaries = 0x1000, [], [], {}
    probe.decoder = cs.Cs(cs.CS_ARCH_X86, cs.CS_MODE_64)
    probe.decoder.detail = True
    return probe


class PackTests(unittest.TestCase):
    def test_v5(self):
        self.assertEqual(read_pak(pack5([(65000, ERROR_TEXT.encode())])), {65000: ERROR_TEXT.encode()})

    def test_alias(self):
        self.assertEqual(read_pak(pack5([(50, b"hello")], [(51, 0)]))[51], b"hello")

    def test_mixed_binary_resource(self):
        self.assertEqual(read_pak(pack5([(50, b"\x1f\x8b\xff")])), {50: b"\x1f\x8b\xff"})

    def test_v4(self):
        data = struct.pack("<IIB", 4, 1, 1) + struct.pack("<HIHI", 19, 21, 0, 23) + b"ok"
        self.assertEqual(read_pak(data), {19: b"ok"})

    def test_truncated(self):
        for data in (b"", b"\x05\0\0\0", pack5([(1, b"abc")])[:-1]):
            with self.subTest(data=data), self.assertRaises(ValueError):
                read_pak(data)

    def test_unknown_version(self):
        with self.assertRaises(ValueError):
            read_pak(struct.pack("<I", 99) + b"\0" * 20)

    def test_invalid_alias(self):
        with self.assertRaises(ValueError):
            read_pak(pack5([(50, b"hello")], [(51, 1)]))

    def test_duplicate_identity(self):
        with self.assertRaises(ValueError):
            read_pak(pack5([(50, b"a"), (50, b"b")]))

    def test_invalid_encoding(self):
        data = bytearray(pack5([(50, b"a")]))
        data[4] = 99
        with self.assertRaises(ValueError):
            read_pak(data)

    def test_backwards_offsets(self):
        data = bytearray(pack5([(50, b"abc")]))
        struct.pack_into("<I", data, 20, 0)
        with self.assertRaises(ValueError):
            read_pak(data)


class InstructionTests(unittest.TestCase):
    def test_integer_bitset_discovery(self):
        probe = leaf("31 C0 83 F9 02 7F 1F 83 FA 08 77 1A B9 0A 01 00 00 0F A3 D1 73 10 "
                     "41 83 F8 05 0F 95 C1 41 83 F8 0A 0F 95 C0 20 C8 C3")
        leads, _ = probe.gate_leads(set())
        self.assertEqual(len(leads), 1)
        self.assertEqual(leads[0]["role_lead"], "integer-impact-checker")
        self.assertEqual(leads[0]["type_bit_masks"], ["0x10a"])

    def test_wrong_manifest_constant_rejected(self):
        probe = leaf("83 7F 50 03 7F 00 C3")
        self.assertEqual(probe.gate_leads(set())[0], [])

    def test_constants_without_cfg_not_enough(self):
        probe = leaf("83 7F 50 02 7F 00 C3 83 F9 01 83 F9 03 83 F9 08 83 F8 05 83 F8 0A")
        self.assertEqual(probe.gate_leads(set())[0], [])

    def test_out_of_window_jump_incomplete(self):
        probe = leaf("E9 FF 7F 00 00")
        _, complete = probe.cfg(0x1001)
        self.assertFalse(complete)

    def test_ret_closes_cfg(self):
        probe = leaf("31 C0 C3")
        nodes, complete = probe.cfg(0x1001)
        self.assertTrue(complete)
        self.assertEqual(len(nodes), 2)

    def test_padding_leaf_not_in_pdata(self):
        probe = leaf("31 C0 83 F9 02 C3")
        self.assertIsNone(probe.containing(0x1003))
        self.assertEqual(probe.boundary_status(0x1003), "padding-derived-leaf-candidate")


if __name__ == "__main__":
    unittest.main()
