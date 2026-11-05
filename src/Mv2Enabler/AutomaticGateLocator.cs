using Iced.Intel;

namespace Mv2Enabler;

// A deliberately bounded semantic grammar, not a fuzzy/signature-score patcher.
// New addresses, branch distances, GPR allocation and field displacements are
// discovered locally. Unsupported instructions/layouts must reject the plan.
internal sealed class AutomaticGateLocator
{
    internal const string Rule = "chromium.mv2-semantic.local.v2";
    private readonly PeImage image;
    private readonly PeSection text;
    private readonly Reader reader;
    private readonly Decoder decoder;
    private readonly List<(int Start, int End)> functions = [];
    private readonly Dictionary<int, Instruction> instructions = [];
    private sealed class Reader(byte[] data) : CodeReader
    {
        internal int Position;
        public override int ReadByte() => Position < data.Length && Position >= 0 ? data[Position++] : -1;
    }
    internal sealed record Layout(int Manifest, int Pointer, int Location, int Type, int Flag);
    private sealed record Gate(int Start, Instruction Compare, Instruction Branch, Register Root, Layout? Layout,
        int? Function, bool Integer = false);
    private readonly record struct Input(int Manifest, int Type, int Location, byte Flag);
    private sealed record Outcome(string Kind, int Address, ulong?[] Registers, byte? ReturnByte = null);
    private static readonly int[] Classes = [int.MinValue, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, int.MaxValue];

    internal AutomaticGateLocator(PeImage image)
    {
        this.image = image; text = image.GetSection(".text");
        reader = new(image.Bytes); decoder = Decoder.Create(64, reader);
        var pdata = image.Sections.SingleOrDefault(s => s.Name == ".pdata");
        if (pdata is null) return;
        for (var p = pdata.RawOffset; p + 12 <= (long)pdata.RawOffset + Math.Min(pdata.RawSize, pdata.VirtualSize); p += 12)
        {
            var a = BitConverter.ToInt32(image.Bytes, p); var b = BitConverter.ToInt32(image.Bytes, p + 4);
            if (a >= text.VirtualAddress && b > a && (long)b <= (long)text.VirtualAddress + text.RawSize)
                functions.Add((a - text.VirtualAddress + text.RawOffset, b - text.VirtualAddress + text.RawOffset));
        }
        functions.Sort((a, b) => a.Start.CompareTo(b.Start));
    }

    public static LocatorResult Locate(PeImage image, ChromeResourceAnchor resource)
    {
        try { return new AutomaticGateLocator(image).Locate(resource); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException)
        { return new(false, null, 0, 0, [$"Automatic discovery rejected the image: {error.Message}"]); }
    }

    internal bool ValidateChecker(int compareRaw, bool integer)
    {
        try
        {
            var cmp = At(compareRaw); var branch = At(Next(cmp));
            if (cmp.Mnemonic != Mnemonic.Cmp || !Imm(cmp, 1, 2) || branch.Mnemonic != Mnemonic.Jg) return false;
            var gate = integer ? IntegerGate(compareRaw, cmp, branch) : ObjectGate(compareRaw, cmp, branch);
            return gate is not null && (integer || Boundary(compareRaw)) && Prove(gate, null, null);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException) { return false; }
    }

    internal (int Objects, int Integers) DiscoverCounts()
    {
        var gates = Discover();
        return (gates.Count(g => !g.Integer), gates.Count(g => g.Integer));
    }

    private Instruction At(int address)
    {
        if (!Inside(address)) throw new InvalidDataException("Instruction/control-flow target leaves .text.");
        if (instructions.TryGetValue(address, out var result)) return result;
        reader.Position = address; decoder.IP = (ulong)address; decoder.Decode(out result);
        if (result.IsInvalid || result.Length == 0 || !Inside(address + result.Length - 1))
            throw new InvalidDataException("Invalid/truncated instruction.");
        // Keep candidate discovery bounded even on unrelated CMP-heavy binaries.
        if (instructions.Count < 100_000) instructions[address] = result;
        return result;
    }
    private bool Inside(int p) => p >= text.RawOffset && (long)p < (long)text.RawOffset + text.RawSize;
    private static int Next(Instruction i) => checked((int)i.NextIP);
    private static int Target(Instruction i) => checked((int)i.NearBranchTarget);
    private static Register Full(Register r) => r.GetFullRegister();
    private Instruction NextMeaningful(ref int cursor)
    {
        for (var n = 0; n < 8; n++)
        { var i = At(cursor); cursor = Next(i); if (i.Mnemonic != Mnemonic.Nop) return i; }
        throw new InvalidDataException("Excessive padding in semantic graph.");
    }
    private int? Owner(int address)
    {
        var lo = 0; var hi = functions.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2; var f = functions[mid];
            if (address < f.Start) hi = mid - 1;
            else if (address >= f.End) lo = mid + 1;
            else return f.Start;
        }
        return null;
    }
    private bool Boundary(int address)
    {
        if (Owner(address) is not { } start)
            return address == text.RawOffset || image.Bytes[address - 1] == 0xcc;
        if (address - start > 65536) return false;
        for (var n = 0; start < address && n < 8192; n++) start = Next(At(start));
        return start == address;
    }
    private static bool Imm(Instruction i, int operand, long value)
    {
        var kind = i.GetOpKind(operand);
        return kind is >= OpKind.Immediate8 and <= OpKind.Immediate32to64 &&
            unchecked((long)i.GetImmediate(operand)) == value;
    }
    private static bool Memory(Instruction i, int operand, Register @base, int size) =>
        i.GetOpKind(operand) == OpKind.Memory && i.MemoryIndex == Register.None &&
        Full(i.MemoryBase) == Full(@base) && i.MemorySize.GetSize() == size &&
        i.SegmentPrefix == Register.None;
    private static bool MoveMemory(Instruction i, Register @base, int size) =>
        i.Mnemonic == Mnemonic.Mov && i.Op0Kind == OpKind.Register && Memory(i, 1, @base, size) &&
        i.Op0Register.GetSize() == size;

    private Gate? ObjectGate(int address, Instruction cmp, Instruction branch)
    {
        if (cmp.Op0Kind != OpKind.Memory || cmp.MemorySize.GetSize() != 4 || cmp.MemoryIndex != Register.None ||
            cmp.MemoryBase.GetSize() != 8 || cmp.SegmentPrefix != Register.None) return null;
        var root = cmp.MemoryBase; var p = Next(branch);
        var pointer = NextMeaningful(ref p);
        if (!MoveMemory(pointer, root, 8)) return null;
        var location = NextMeaningful(ref p);
        if (!MoveMemory(location, pointer.Op0Register, 4)) return null;
        var flag = NextMeaningful(ref p);
        if (flag.Mnemonic != Mnemonic.Cmp || !Memory(flag, 0, root, 1) || !Imm(flag, 1, 0)) return null;
        var flagBranch = NextMeaningful(ref p);
        if (flagBranch.Mnemonic != Mnemonic.Jne) return null;
        var type = NextMeaningful(ref p);
        if (!MoveMemory(type, pointer.Op0Register, 4)) return null;
        var typeCmp = NextMeaningful(ref p);
        if (typeCmp.Mnemonic != Mnemonic.Cmp || typeCmp.Op0Register != type.Op0Register || !Imm(typeCmp, 1, 1)) return null;
        var typeBranch = NextMeaningful(ref p);
        if (typeBranch.Mnemonic != Mnemonic.Jne) return null;
        var layout = new Layout(checked((int)cmp.MemoryDisplacement64), checked((int)pointer.MemoryDisplacement64),
            checked((int)location.MemoryDisplacement64), checked((int)type.MemoryDisplacement64), checked((int)flag.MemoryDisplacement64));
        var fields = new[] { (layout.Manifest, 4), (layout.Pointer, 8), (layout.Flag, 1) };
        if (fields.Any(x => x.Item1 < 0 || x.Item1 > 65536) || layout.Location < 0 || layout.Type < 0 ||
            layout.Location > 65536 || layout.Type > 65536 || Math.Abs(layout.Location - layout.Type) < 4 ||
            fields.Any(a => fields.Any(b => a != b && a.Item1 < b.Item1 + b.Item2 && b.Item1 < a.Item1 + a.Item2))) return null;
        return new(address, cmp, branch, root, layout, Owner(address));
    }

    private Gate? IntegerGate(int address, Instruction cmp, Instruction branch)
    {
        if (cmp.Op0Kind != OpKind.Register || cmp.Op0Register != Register.ECX) return null;
        // Recognize the explicit unsigned bound before BT. Sampling alone could
        // miss periodic bit-index wraparound on arbitrary 32-bit manifest types.
        var start = address - 2;
        if (!Inside(start) || !Boundary(start)) return null;
        var zero = At(start);
        if (zero.Mnemonic != Mnemonic.Xor || zero.Op0Register != Register.EAX || zero.Op1Register != Register.EAX || Next(zero) != address) return null;
        var p = Next(branch); var bound = NextMeaningful(ref p); var ja = NextMeaningful(ref p);
        var mask = NextMeaningful(ref p); var bit = NextMeaningful(ref p); var jae = NextMeaningful(ref p);
        if (bound.Mnemonic != Mnemonic.Cmp || bound.Op0Register != Register.EDX || !Imm(bound, 1, 8) ||
            ja.Mnemonic != Mnemonic.Ja || Target(ja) != Target(branch) || mask.Mnemonic != Mnemonic.Mov ||
            mask.Op0Kind != OpKind.Register || mask.Op0Register.GetSize() != 4 || !Imm(mask, 1, 0x10a) ||
            bit.Mnemonic != Mnemonic.Bt || bit.Op0Register != mask.Op0Register || bit.Op1Register != Register.EDX ||
            jae.Mnemonic != Mnemonic.Jae || Target(jae) != Target(branch) || At(Target(branch)).Mnemonic != Mnemonic.Ret) return null;
        return new(start, cmp, branch, Register.None, null, null, true);
    }

    private List<Gate> Discover()
    {
        var result = new List<Gate>(); var cursor = text.RawOffset;
        var end = text.RawOffset + text.RawSize;
        while (cursor < end - 8)
        {
            var index = image.Bytes.AsSpan(cursor, end - cursor).IndexOf((byte)0x83);
            if (index < 0) break;
            var p = cursor + index; cursor = p + 1;
            var modrm = image.Bytes[p + 1];
            if ((modrm & 0x38) != 0x38 || (modrm & 7) == 4) continue;
            var immediate = (modrm >> 6) switch { 1 => p + 3, 2 => p + 6, 3 => p + 2, _ => -1 };
            if (immediate < 0 || image.Bytes[immediate] != 2) continue;
            if (p > text.RawOffset && image.Bytes[p - 1] is >= 0x40 and <= 0x4f) p--;
            try
            {
                var cmp = At(p);
                if (cmp.Mnemonic != Mnemonic.Cmp || !Imm(cmp, 1, 2)) continue;
                var branch = At(Next(cmp));
                if (branch.Mnemonic != Mnemonic.Jg || !Inside(Target(branch))) continue;
                Gate? gate;
                if (cmp.Op0Kind == OpKind.Memory)
                {
                    // Discard unrelated compare sites before the more expensive
                    // function-boundary and predicate-partition checks.
                    gate = ObjectGate(p, cmp, branch);
                    if (gate is null || !Boundary(p)) continue;
                }
                else gate = IntegerGate(p, cmp, branch);
                if (gate is not null) result.Add(gate);
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException) { continue; }
            if (result.Count > 16) throw new InvalidDataException("Too many semantic gate candidates.");
        }
        return result;
    }

    private static int RegIndex(Register register)
    {
        var full = Full(register);
        if (full is < Register.RAX or > Register.R15 || register is Register.AH or Register.BH or Register.CH or Register.DH)
            throw new InvalidDataException("Unsupported register in MV2 predicate.");
        return (int)full - (int)Register.RAX;
    }
    private Outcome Run(Gate gate, Input input, HashSet<int>? normals, bool forceUnaffected = false)
    {
        var registers = new ulong?[16];
        var known = new ulong[16];
        var lowBoolean = new bool[16];
        if (gate.Integer)
        {
            registers[RegIndex(Register.RCX)] = (uint)input.Manifest; registers[RegIndex(Register.RDX)] = (uint)input.Type; registers[RegIndex(Register.R8)] = (uint)input.Location;
            known[RegIndex(Register.RCX)] = known[RegIndex(Register.RDX)] = known[RegIndex(Register.R8)] = ulong.MaxValue;
        }
        else { registers[RegIndex(gate.Root)] = 0x100000; known[RegIndex(gate.Root)] = ulong.MaxValue; }
        bool zero = false, greater = false, carry = false;
        var flagsKnown = false; var pc = gate.Start;
        ulong Mask(int size) => size == 8 ? ulong.MaxValue : (1UL << (size * 8)) - 1;
        ulong ReadReg(Register r)
        {
            var index = RegIndex(r); var mask = Mask(r.GetSize());
            if ((known[index] & mask) != mask) throw new InvalidDataException("Unknown register bits in predicate.");
            return registers[index]!.Value & mask;
        }
        void WriteReg(Register r, ulong value)
        {
            var index = RegIndex(r); var size = r.GetSize(); value &= Mask(size);
            lowBoolean[index] = false;
            if (size is 8 or 4) { registers[index] = value; known[index] = ulong.MaxValue; }
            else { registers[index] = ((registers[index] ?? 0) & ~Mask(size)) | value; known[index] |= Mask(size); }
        }
        Outcome Finish(string kind, int address)
        {
            var rax = RegIndex(Register.RAX);
            byte? returnByte = (known[rax] & 0xff) == 0xff ? (byte)registers[rax]!.Value : null;
            for (var n = 0; n < registers.Length; n++) if (known[n] != ulong.MaxValue) registers[n] = null;
            return new(kind, address, registers, returnByte);
        }
        ulong Read(Instruction i, int operand)
        {
            if (i.GetOpKind(operand) == OpKind.Register) return ReadReg(i.GetOpRegister(operand));
            if (i.GetOpKind(operand) != OpKind.Memory) return i.GetImmediate(operand);
            if (gate.Layout is not { } l || i.MemoryIndex != Register.None || i.SegmentPrefix != Register.None)
                throw new InvalidDataException("Unexpected memory input to predicate.");
            var address = unchecked(ReadReg(i.MemoryBase) + i.MemoryDisplacement64); var size = i.MemorySize.GetSize();
            if (address == 0x100000UL + (ulong)l.Manifest && size == 4) return (uint)input.Manifest;
            if (address == 0x100000UL + (ulong)l.Pointer && size == 8) return 0x200000;
            if (address == 0x100000UL + (ulong)l.Flag && size == 1) return input.Flag;
            if (address == 0x200000UL + (ulong)l.Location && size == 4) return (uint)input.Location;
            if (address == 0x200000UL + (ulong)l.Type && size == 4) return (uint)input.Type;
            throw new InvalidDataException("Predicate read is outside the inferred extension fields.");
        }
        for (var steps = 0; steps < 96; steps++)
        {
            if (normals?.Contains(pc) == true) return Finish("normal", pc);
            if (Math.Abs((long)pc - gate.Start) > 4096) throw new InvalidDataException("Predicate graph exceeds its local bound.");
            var i = At(pc); var next = Next(i);
            if (forceUnaffected && pc == (int)gate.Branch.IP) { pc = Target(gate.Branch); continue; }
            switch (i.Mnemonic)
            {
                case Mnemonic.Nop: break;
                case Mnemonic.Mov:
                    if (i.Op0Kind != OpKind.Register) return Finish("action", pc);
                    if (i.Op0Register.GetSize() < 4) throw new InvalidDataException("Truncating move in predicate.");
                    var moved = Read(i, 1); WriteReg(i.Op0Register, moved);
                    lowBoolean[RegIndex(i.Op0Register)] = moved <= 1 && i.Op1Kind != OpKind.Memory;
                    break;
                case Mnemonic.Xor:
                    if (i.Op0Kind != OpKind.Register || i.Op1Kind != OpKind.Register || i.Op0Register != i.Op1Register)
                        throw new InvalidDataException("Non-zeroing XOR in predicate.");
                    WriteReg(i.Op0Register, 0); lowBoolean[RegIndex(i.Op0Register)] = true;
                    flagsKnown = true; zero = true; greater = false; carry = false; break;
                case Mnemonic.Cmp:
                    if (i.Op1Kind is < OpKind.Immediate8 or > OpKind.Immediate32to64 ||
                        unchecked((long)i.GetImmediate(1)) is not (0 or 1 or 2 or 3 or 5 or 8 or 10))
                        throw new InvalidDataException("Unrecognized comparison partition.");
                    var size = i.Op0Kind == OpKind.Memory ? i.MemorySize.GetSize() : i.Op0Register.GetSize();
                    if (size != 4 && !(size == 1 && i.Op0Kind == OpKind.Memory && Imm(i, 1, 0)))
                        throw new InvalidDataException("Unsupported/truncating comparison width.");
                    var a = Read(i, 0) & Mask(size); var b = Read(i, 1) & Mask(size);
                    zero = a == b; carry = a < b;
                    greater = size == 4 ? (int)a > (int)b : size == 1 ? (sbyte)a > (sbyte)b : (long)a > (long)b;
                    flagsKnown = true; break;
                case Mnemonic.Bt:
                    if (!gate.Integer) throw new InvalidDataException("Bit test outside the explicitly bounded integer predicate.");
                    var bits = i.Op0Register.GetSize() * 8;
                    carry = (Read(i, 0) & (1UL << (int)(Read(i, 1) % (uint)bits))) != 0;
                    flagsKnown = true; break;
                case Mnemonic.Setne:
                    if (!flagsKnown) throw new InvalidDataException("Unknown flags.");
                    WriteReg(i.Op0Register, zero ? 0UL : 1UL); lowBoolean[RegIndex(i.Op0Register)] = true; break;
                case Mnemonic.And:
                    if (i.Op0Kind != OpKind.Register || i.Op1Kind != OpKind.Register || i.Op0Register.GetSize() != 1)
                        throw new InvalidDataException("Unsupported AND in predicate.");
                    if (!lowBoolean[RegIndex(i.Op0Register)] || !lowBoolean[RegIndex(i.Op1Register)])
                        throw new InvalidDataException("AND operands are not derived booleans.");
                    var value = Read(i, 0) & Read(i, 1); WriteReg(i.Op0Register, value);
                    lowBoolean[RegIndex(i.Op0Register)] = true;
                    zero = value == 0; greater = value is > 0 and < 128; carry = false; flagsKnown = true; break;
                case Mnemonic.Jmp:
                    if (i.FlowControl != FlowControl.UnconditionalBranch) throw new InvalidDataException("Indirect predicate jump.");
                    next = Target(i); break;
                case Mnemonic.Jg: case Mnemonic.Je: case Mnemonic.Jne: case Mnemonic.Ja: case Mnemonic.Jae:
                    if (!flagsKnown) throw new InvalidDataException("Unknown branch flags.");
                    var take = i.Mnemonic switch { Mnemonic.Jg => greater, Mnemonic.Je => zero,
                        Mnemonic.Jne => !zero, Mnemonic.Ja => !carry && !zero, _ => !carry };
                    if (take) next = Target(i); break;
                case Mnemonic.Ret: return Finish("return", pc);
                default: return Finish("action", pc);
            }
            pc = next;
        }
        throw new InvalidDataException("Predicate graph does not terminate within budget.");
    }

    private bool Prove(Gate gate, HashSet<int>? normals, int? action, Register? zeroAtJoin = null)
    {
        var flags = gate.Integer ? new byte[] { 0 } : [0, 1, 255];
        foreach (var manifest in Classes)
        foreach (var type in Classes)
        foreach (var location in Classes)
        foreach (var flag in flags)
        {
            var input = new Input(manifest, type, location, flag);
            var affected = manifest <= 2 && (flag != 0 || type is 1 or 3 or 8) && location is not 5 and not 10;
            var result = Run(gate, input, normals);
            if (normals is null)
            {
                if (result.Kind != "return" || result.ReturnByte != (affected ? 1 : 0)) return false;
                var patched = Run(gate, input, null, true);
                if (patched.Kind != "return" || patched.ReturnByte != 0) return false;
            }
            else if (affected)
            { if (result.Kind != "action" || result.Address != action) return false; }
            else
            {
                if (result.Kind != "normal") return false;
                if (zeroAtJoin is { } reg && result.Address != Target(gate.Branch) && result.Registers[RegIndex(reg)] != 0) return false;
            }
        }
        return true;
    }

    private bool Epilogue(int address)
    {
        var i = At(address);
        if (i.Mnemonic != Mnemonic.Add || i.Op0Register != Register.RSP || i.GetImmediate(1) is 0 or > 4096) return false;
        for (var n = 0; n < 10; n++)
        {
            i = At(Next(i));
            if (i.Mnemonic == Mnemonic.Ret) return true;
            if (i.Mnemonic != Mnemonic.Pop || i.Op0Register is not (Register.RBX or Register.RBP or Register.RSI or Register.RDI or Register.R12 or Register.R13 or Register.R14 or Register.R15)) return false;
        }
        return false;
    }

    private bool RemovalPath(int address, out int factory, out int extensionId)
    {
        factory = extensionId = -1;
        var reason = false; var calls = new List<int>();
        for (var n = 0; n < 24; n++)
        {
            var i = At(address);
            if (i.Mnemonic == Mnemonic.Mov && i.Op0Register == Register.R8D && Imm(i, 1, 0x800000)) reason = true;
            if (i.FlowControl == FlowControl.Call) { if (!Inside(Target(i))) return false; calls.Add(Target(i)); }
            if (i.Mnemonic == Mnemonic.Jmp)
            {
                if (i.FlowControl != FlowControl.UnconditionalBranch || !Inside(Target(i)) || !reason || calls.Count != 3) return false;
                factory = calls[1]; extensionId = calls[2]; return true;
            }
            if (i.FlowControl is not FlowControl.Next and not FlowControl.Call) return false;
            address = Next(i);
        }
        return false;
    }

    private bool DisableSuffix(int address, int join)
    {
        var p = address; var test = NextMeaningful(ref p); var skip = NextMeaningful(ref p);
        var store = NextMeaningful(ref p); var set = NextMeaningful(ref p); var jump = NextMeaningful(ref p);
        return test.Mnemonic == Mnemonic.Test && test.Op0Kind == OpKind.Register && test.Op0Register == test.Op1Register &&
            skip.Mnemonic == Mnemonic.Je && Target(skip) == (int)set.IP && store.Mnemonic == Mnemonic.Mov &&
            Memory(store, 0, test.Op0Register, 4) && store.MemoryDisplacement64 == 0 && store.Op1Register == Register.EAX &&
            set.Mnemonic == Mnemonic.Mov && set.Op0Kind == OpKind.Register && set.Op0Register.GetSize() == 1 && Imm(set, 1, 1) &&
            jump.Mnemonic == Mnemonic.Jmp && Target(jump) == join;
    }

    private bool InstallPaths(Gate gate, int error, int resource)
    {
        var normal = Target(gate.Branch); var n = normal; var e = error;
        var output = NextMeaningful(ref n);
        if (output.Mnemonic != Mnemonic.Lea || output.Op0Kind != OpKind.Register || output.MemoryBase != Register.RSP) return false;
        var context = NextMeaningful(ref n); var extension = NextMeaningful(ref n); var outputArg = NextMeaningful(ref n); var policy = NextMeaningful(ref n);
        if (context.Mnemonic != Mnemonic.Mov || context.Op0Register != Register.RCX || context.Op1Kind != OpKind.Register ||
            extension.Mnemonic != Mnemonic.Mov || extension.Op0Register != Register.RDX || Full(extension.Op1Register) != Full(gate.Root) ||
            outputArg.Mnemonic != Mnemonic.Mov || outputArg.Op0Register != Register.R8 || outputArg.Op1Register != output.Op0Register ||
            policy.FlowControl != FlowControl.Call || !Inside(Target(policy))) return false;
        var normalTemp = NextMeaningful(ref n); var normalBool = NextMeaningful(ref n); var normalBuffer = NextMeaningful(ref n);
        var errorOutput = NextMeaningful(ref e); var errorArg = NextMeaningful(ref e); var errorId = NextMeaningful(ref e); var errorCall = NextMeaningful(ref e);
        var errorTemp = NextMeaningful(ref e); var errorBool = NextMeaningful(ref e); var errorBuffer = NextMeaningful(ref e); var errorJoin = NextMeaningful(ref e);
        var callback = At(n);
        return errorOutput.Mnemonic == Mnemonic.Lea && errorOutput.Op0Register == output.Op0Register &&
            errorOutput.MemoryBase == output.MemoryBase && errorOutput.MemoryDisplacement64 == output.MemoryDisplacement64 &&
            errorArg.Mnemonic == Mnemonic.Mov && errorArg.Op0Register == Register.RCX && errorArg.Op1Register == output.Op0Register &&
            errorId.Mnemonic == Mnemonic.Mov && errorId.Op0Register == Register.EDX && Imm(errorId, 1, resource) &&
            errorCall.FlowControl == FlowControl.Call && Inside(Target(errorCall)) && Target(errorCall) != Target(policy) &&
            normalTemp.Mnemonic == Mnemonic.Lea && normalTemp.Op0Register == Register.RCX && normalTemp.MemoryBase == Register.RSP &&
            errorTemp.Mnemonic == Mnemonic.Lea && errorTemp.Op0Register == Register.RCX && errorTemp.MemoryBase == Register.RSP &&
            normalBool.Mnemonic == Mnemonic.Mov && Memory(normalBool, 0, Register.RCX, 1) && normalBool.Op1Register == Register.AL &&
            errorBool.Mnemonic == Mnemonic.Mov && Memory(errorBool, 0, Register.RCX, 1) && Imm(errorBool, 1, 0) &&
            normalBool.MemoryDisplacement64 == errorBool.MemoryDisplacement64 &&
            normalBuffer.Mnemonic == Mnemonic.Lea && normalBuffer.MemoryBase == Register.RSP &&
            errorBuffer.Mnemonic == Mnemonic.Lea && errorBuffer.Op0Register == normalBuffer.Op0Register && errorBuffer.MemoryBase == Register.RSP &&
            errorJoin.Mnemonic == Mnemonic.Jmp && Target(errorJoin) == n && callback.Mnemonic == Mnemonic.Cmp &&
            Memory(callback, 0, output.Op0Register, 1) && callback.MemoryDisplacement64 == 0x17 && Imm(callback, 1, 0);
    }

    private LocatorResult Locate(ChromeResourceAnchor resource)
    {
        if (!text.IsExecutable) throw new InvalidDataException("The code section is not executable.");
        var gates = Discover();
        var objects = gates.Where(g => !g.Integer).ToList(); var integers = gates.Where(g => g.Integer).ToList();
        if (objects.Count != 6 || integers.Count != 1 || objects.Select(g => g.Layout).Distinct().Count() != 1)
            throw new InvalidDataException($"Expected six consistent object gates and one integer gate, found {objects.Count}/{integers.Count}.");
        var pure = new List<Gate>(); Gate? disable = null, enable = null, install = null, startupGate = null;
        var startupAction = -1; var registrarFactory = -1; var extensionIdGetter = -1;
        foreach (var gate in objects)
        {
            var target = Target(gate.Branch); var first = At(target);
            if (first.Mnemonic == Mnemonic.Xor && first.Op0Register == Register.EAX && first.Op1Register == Register.EAX)
            { if (!Prove(gate, null, null)) throw new InvalidDataException("Impact checker failed its predicate partitions."); pure.Add(gate); continue; }
            HashSet<int> normals = [target]; Register? zero = null;
            if (first.Mnemonic == Mnemonic.Xor && first.Op0Register == first.Op1Register && first.Op0Register.GetSize() == 4)
            {
                var jump = At(Next(first));
                if (jump.Mnemonic != Mnemonic.Jmp || !Inside(Target(jump))) throw new InvalidDataException("Invalid unaffected return join.");
                normals.Add(Target(jump)); zero = first.Op0Register;
            }
            var affected = Run(gate, new(2, 1, 0, 0), normals);
            if (affected.Kind != "action" || !Prove(gate, normals, affected.Address, zero))
                throw new InvalidDataException("Clone predicate did not converge on the affected/unaffected paths.");
            if (zero is not null && affected.Registers[RegIndex(Register.RAX)] == 0x800000 && DisableSuffix(affected.Address, normals.Single(p => p != target)))
            { if (disable is not null) throw new InvalidDataException("Ambiguous disable gate."); disable = gate; }
            else if (Epilogue(affected.Address) && RemovalPath(target, out var factory, out var id))
            {
                if (enable is not null) throw new InvalidDataException("Ambiguous re-enable gate.");
                enable = gate; registrarFactory = factory; extensionIdGetter = id;
            }
            else if (InstallPaths(gate, affected.Address, resource.ResourceId))
            { if (install is not null) throw new InvalidDataException("Ambiguous installation gate."); install = gate; }
            else
            {
                if (startupGate is not null || gate.Function is null) throw new InvalidDataException("Unrecognized/ambiguous startup predicate.");
                startupGate = gate; startupAction = affected.Address;
            }
        }
        if (pure.Count != 2 || disable is null || enable is null || install is null || startupGate is null || !Prove(integers[0], null, null))
            throw new InvalidDataException("Incomplete semantic patch family.");
        var startupRange = functions.Single(f => f.Start == startupGate.Function);
        var startup = new StartupLoopLocator(image, new(startupRange.Start, startupRange.End, startupGate.Start,
            Target(startupGate.Branch), startupAction, startupGate.Root, registrarFactory, extensionIdGetter)).Locate();
        pure.Sort((a, b) => a.Start.CompareTo(b.Start));
        var edits = new List<PatchEdit>();
        foreach (var gate in new[] { pure[0], pure[1], integers[0], disable, enable, install })
        {
            var address = (int)gate.Branch.IP;
            if (gate.Branch.Length == 2 && image.Bytes[address] == 0x7f)
                edits.Add(new(address, image.RawOffsetToRva(address), 0x7f, 0xeb));
            else if (gate.Branch.Length == 6 && image.Bytes[address] == 0x0f && image.Bytes[address + 1] == 0x8f)
            { edits.Add(new(address, image.RawOffsetToRva(address), 0x0f, 0x90)); edits.Add(new(address + 1, image.RawOffsetToRva(address + 1), 0x8f, 0xe9)); }
            else throw new InvalidDataException("Unsupported manifest-branch encoding.");
        }
        edits.AddRange(startup.Edits);
        var primary = edits[0];
        var evidence = new List<string>
        {
            $"English resource ID {resource.ResourceId} discovered from the local PAK, not a version table.",
            "Six object gates share locally inferred manifest, pointer, type, location and flag fields.",
            "Decoded supported predicate grammar checked over all comparison equivalence classes; unknown reads/instructions reject the plan.",
            "Two return-value checkers, bounded integer bitset, disable output, re-enable removal and install-policy/error callback were validated.",
            "Startup entry was decoded and correlated with selected-collection insertion, empty guard, reason data flow and untouched backedge."
        };
        evidence.AddRange(startup.Evidence);
        var patch = new PatchTarget(Rule, "Automatically discovered and validated local MV2 gate family.", pure[0].Start,
            primary.PatchRawOffset, primary.PatchRva, primary.ExpectedByte, primary.ReplacementByte, PatchState.Original, edits.Skip(1).ToArray(), evidence);
        return new(true, patch, pure.Count, pure.Count, ["Automatic local semantic discovery approved one complete patch set."]);
    }
}
