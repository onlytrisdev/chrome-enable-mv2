using Iced.Intel;

namespace Mv2Enabler;

// Bounded CFG/data-flow contract for the selected-extension collection and its
// disabling loop. No Chrome-version table, stack offsets, or GPR signatures.
internal sealed class StartupLoopLocator
{
    internal sealed record Context(int FunctionStart, int FunctionEnd, int Gate, int Normal, int Affected,
        Register Extension, int RegistrarFactory, int ExtensionIdGetter);
    internal sealed record Proof(int Branch, IReadOnlyList<PatchEdit> Edits, IReadOnlyList<string> Evidence);
    private sealed record Value(string Kind, long Number = 0, Register Register = Register.None, Value? Parent = null);
    private readonly PeImage image;
    private readonly Context context;
    private readonly Dictionary<int, Instruction> code = [];
    private readonly Dictionary<int, int[]> edges = [];
    private readonly Dictionary<int, int[]> predecessors = [];
    private readonly Dictionary<int, HashSet<Register>> writes = [];
    private readonly InstructionInfoFactory info = new();
    private static readonly Register[] Gprs = Enumerable.Range((int)Register.RAX, 16).Select(i => (Register)i).ToArray();
    private static readonly Register[] Volatile = [Register.RAX, Register.RCX, Register.RDX, Register.R8, Register.R9, Register.R10, Register.R11];
    private static Register Full(Register r) => r.GetFullRegister();
    private static int Next(Instruction i) => checked((int)i.NextIP);
    private static int Target(Instruction i) => checked((int)i.NearBranchTarget);
    private static Value Stack(long offset) => new("stack", offset);
    private static Value Unknown(Register r = Register.None) => new("unknown", Register: Full(r));
    private static bool Immediate(Instruction i, int op, long value) =>
        i.GetOpKind(op) is >= OpKind.Immediate8 and <= OpKind.Immediate32to64 && unchecked((long)i.GetImmediate(op)) == value;
    private static bool Mem(Instruction i, int op, Register b, int size) => i.GetOpKind(op) == OpKind.Memory &&
        i.MemoryIndex == Register.None && i.SegmentPrefix == Register.None && Full(i.MemoryBase) == Full(b) && i.MemorySize.GetSize() == size;
    private static bool RegMove(Instruction i) => i.Mnemonic == Mnemonic.Mov && i.Op0Kind == OpKind.Register;
    private static bool Nonvolatile(Register r) => !Volatile.Contains(Full(r)) && Full(r) is >= Register.RAX and <= Register.R15 && Full(r) != Register.RSP;

    internal StartupLoopLocator(PeImage image, Context context)
    {
        this.image = image; this.context = context;
        var text = image.GetSection(".text");
        if (context.FunctionStart < text.RawOffset || context.FunctionEnd > (long)text.RawOffset + text.RawSize ||
            context.FunctionEnd <= context.FunctionStart || context.FunctionEnd - context.FunctionStart > 65536)
            throw new InvalidDataException("Startup function has invalid bounds.");
        var decoder = Decoder.Create(64, new ByteArrayCodeReader(image.Bytes, context.FunctionStart, context.FunctionEnd - context.FunctionStart));
        decoder.IP = (ulong)context.FunctionStart;
        while ((long)decoder.IP < context.FunctionEnd)
        {
            decoder.Decode(out var i);
            if (i.IsInvalid || i.Length == 0 || i.NextIP > (ulong)context.FunctionEnd || code.Count >= 8192)
                throw new InvalidDataException("Invalid/budget-exceeding startup function.");
            var address = (int)i.IP; code.Add(address, i);
            var changed = info.GetInfo(i).GetUsedRegisters().Where(r => r.Access is OpAccess.Write or OpAccess.CondWrite or OpAccess.ReadWrite or OpAccess.ReadCondWrite)
                .Select(r => Full(r.Register)).ToHashSet();
            if (i.FlowControl is FlowControl.Call or FlowControl.IndirectCall)
                foreach (var r in Volatile) changed.Add(r);
            writes[address] = changed;
        }
        foreach (var i in code.Values)
        {
            int[] successors = i.FlowControl switch
            {
                FlowControl.Next or FlowControl.Call or FlowControl.IndirectCall => [Next(i)],
                FlowControl.ConditionalBranch => [Next(i), Target(i)],
                FlowControl.UnconditionalBranch => [Target(i)],
                FlowControl.Return => [],
                _ => throw new InvalidDataException("Unresolved control flow in startup function.")
            };
            if (successors.Any(p => !code.ContainsKey(p))) throw new InvalidDataException("Startup CFG leaves function or lands inside an instruction.");
            edges[(int)i.IP] = successors;
        }
        foreach (var p in code.Keys) predecessors[p] = edges.Where(e => e.Value.Contains(p)).Select(e => e.Key).ToArray();
        // Compiler cold blocks can be physically adjacent after a cookie-failure
        // call. They are not a returning CFG edge when the dominating mismatch,
        // helper comparison and Windows INT 29h fast-fail path are all proven.
        foreach (var i in code.Values.Where(i => i.FlowControl == FlowControl.Call))
            if (CookieFailureSink(i)) edges[(int)i.IP] = [];
        if (!code.ContainsKey(context.Gate) || !code.ContainsKey(context.Normal) || !code.ContainsKey(context.Affected))
            throw new InvalidDataException("Startup predicate endpoints are not instruction boundaries.");
    }

    private Instruction External(int p)
    {
        if (!image.Sections.Any(s => s.IsExecutable && p >= s.RawOffset && (long)p + 15 <= (long)s.RawOffset + s.RawSize))
            throw new InvalidDataException("External startup helper leaves executable code.");
        var d = Decoder.Create(64, new ByteArrayCodeReader(image.Bytes, p, 15)); d.IP = (ulong)p;
        d.Decode(out var i); if (i.IsInvalid) throw new InvalidDataException("Invalid startup helper."); return i;
    }

    private bool CookieFailureSink(Instruction call)
    {
        var xor = Previous((int)call.IP);
        if (xor.Mnemonic != Mnemonic.Xor || xor.Op0Register != Register.RCX || xor.Op1Register != Register.RSP) return false;
        var load = Previous((int)xor.IP);
        if (!RegMove(load) || load.Op0Register != Register.RCX || !Mem(load, 1, Register.RSP, 8)) return false;
        var incoming = predecessors[(int)load.IP];
        if (incoming.Length != 1 || code[incoming[0]].Mnemonic != Mnemonic.Jne || Target(code[incoming[0]]) != (int)load.IP) return false;
        var compare = Previous(incoming[0]); var cookie = Previous((int)compare.IP);
        var restore = Previous((int)cookie.IP); var stack = Previous((int)restore.IP);
        if (compare.Mnemonic != Mnemonic.Cmp || compare.Op0Kind != OpKind.Register || compare.Op1Kind != OpKind.Register ||
            !RegMove(cookie) || !cookie.IsIPRelativeMemoryOperand || cookie.Op0Register.GetSize() != 8 ||
            restore.Mnemonic != Mnemonic.Xor || restore.Op1Register != Register.RSP || restore.Op0Register.GetSize() != 8 ||
            !RegMove(stack) || stack.Op0Register != restore.Op0Register || !Mem(stack, 1, Register.RSP, 8) || stack.MemoryDisplacement64 != load.MemoryDisplacement64 ||
            !((compare.Op0Register == cookie.Op0Register && compare.Op1Register == restore.Op0Register) ||
              (compare.Op1Register == cookie.Op0Register && compare.Op0Register == restore.Op0Register))) return false;
        var first = External(Target(call)); var branch = External(Next(first));
        if (first.Mnemonic != Mnemonic.Cmp || first.Op0Register != Register.RCX || !first.IsIPRelativeMemoryOperand ||
            first.IPRelativeMemoryAddress != cookie.IPRelativeMemoryAddress || branch.Mnemonic != Mnemonic.Jne) return false;
        var p = Target(branch);
        for (var n = 0; n < 8; n++)
        {
            var i = External(p);
            if (i.Mnemonic == Mnemonic.Int && Immediate(i, 0, 0x29)) return true;
            if (i.Mnemonic == Mnemonic.Jmp && i.FlowControl == FlowControl.UnconditionalBranch) p = Target(i);
            else if (i.Mnemonic == Mnemonic.Mov && i.Op0Kind == OpKind.Register && i.Op1Kind is >= OpKind.Immediate8 and <= OpKind.Immediate32to64) p = Next(i);
            else return false;
        }
        return false;
    }

    private bool Reaches(int start, int goal, int? blocked = null)
    {
        var queue = new Stack<int>(); queue.Push(start); var seen = new HashSet<int>();
        while (queue.TryPop(out var p))
        { if (p == blocked || !seen.Add(p)) continue; if (p == goal) return true; foreach (var n in edges[p]) queue.Push(n); }
        return false;
    }
    private bool Dominates(int a, int b) => Reaches(context.FunctionStart, b) && !Reaches(context.FunctionStart, b, a);
    private Instruction Previous(int address) => code.Values.SingleOrDefault(i => Next(i) == address) is { Length: > 0 } i
        ? i : throw new InvalidDataException("Missing preceding instruction.");
    private Instruction Meaningful(ref int p)
    {
        for (var k = 0; k < 8; k++)
        { if (!code.TryGetValue(p, out var i)) throw new InvalidDataException("Invalid startup endpoint."); p = Next(i); if (i.Mnemonic != Mnemonic.Nop) return i; }
        throw new InvalidDataException("Excessive startup padding.");
    }
    private bool Survives(Register reg, int definition, int use)
    {
        var queue = new Stack<int>(); foreach (var p in edges[definition]) queue.Push(p);
        var seen = new HashSet<int>();
        while (queue.TryPop(out var p))
        {
            if (p == use || p == definition || !seen.Add(p) || !Reaches(p, use, definition)) continue;
            if (writes[p].Contains(Full(reg))) return false;
            foreach (var n in edges[p]) queue.Push(n);
        }
        return true;
    }
    private long? StableStack(Register reg, int use)
    {
        if (!Nonvolatile(reg)) return null;
        var candidates = code.Values.Where(i => i.Mnemonic == Mnemonic.Lea && i.Op0Kind == OpKind.Register && Full(i.Op0Register) == Full(reg) &&
            i.Op0Register.GetSize() == 8 && i.MemoryBase == Register.RSP && i.MemoryIndex == Register.None && i.SegmentPrefix == Register.None &&
            i.MemoryDisplacement64 <= 4096 && Dominates((int)i.IP, use) && Survives(reg, (int)i.IP, use)).ToArray();
        return candidates.Length == 1 ? (long)candidates[0].MemoryDisplacement64 : null;
    }
    private static Value Add(Value value, long displacement) => value.Kind is "stack" or "heap" or "node" ? value with { Number = value.Number + displacement }
        : new("address", displacement, Parent: value);
    private Value Address(Instruction i, Dictionary<Register, Value> state)
    {
        if (i.MemoryIndex != Register.None || i.SegmentPrefix != Register.None || i.MemoryBase.GetSize() != 8)
            throw new InvalidDataException("Unsupported indexed/segmented startup data flow.");
        var value = i.MemoryBase == Register.RSP ? Stack(0) : state.GetValueOrDefault(Full(i.MemoryBase), Unknown());
        return Add(value, unchecked((long)i.MemoryDisplacement64));
    }
    private Value Read(Instruction i, int operand, Dictionary<Register, Value> state)
    {
        var kind = i.GetOpKind(operand);
        if (kind == OpKind.Register) return state.GetValueOrDefault(Full(i.GetOpRegister(operand)), Unknown());
        if (kind == OpKind.Memory) return new("memory", Parent: Address(i, state));
        if (kind is >= OpKind.Immediate8 and <= OpKind.Immediate32to64) return new("constant", unchecked((long)i.GetImmediate(operand)));
        throw new InvalidDataException("Unsupported startup operand.");
    }
    private void Move(Instruction i, Dictionary<Register, Value> state)
    {
        if (i.Mnemonic == Mnemonic.Nop) return;
        if (i.Mnemonic == Mnemonic.Lea && i.Op0Kind == OpKind.Register && i.Op0Register.GetSize() == 8)
        { state[Full(i.Op0Register)] = Address(i, state); return; }
        if (RegMove(i))
        {
            var value = Read(i, 1, state);
            if (i.Op0Register.GetSize() != 8 && value.Kind != "constant") throw new InvalidDataException("Truncated pointer in startup data flow.");
            state[Full(i.Op0Register)] = value; return;
        }
        throw new InvalidDataException("Unsupported startup argument preparation.");
    }
    private static void Call(Dictionary<Register, Value> state, Value result)
    { foreach (var r in Volatile) state[r] = Unknown(); state[Register.RAX] = result; }
    private bool ExecutableTarget(Instruction i) => i.FlowControl == FlowControl.Call && image.Sections.Any(s => s.IsExecutable &&
        Target(i) >= s.RawOffset && (long)Target(i) < (long)s.RawOffset + s.RawSize);

    private long InsertCollection()
    {
        var load = Previous(context.Gate);
        if (!RegMove(load) || Full(load.Op0Register) != Full(context.Extension) || load.Op0Register.GetSize() != 8 ||
            load.Op1Kind != OpKind.Memory || load.MemoryIndex != Register.None || load.SegmentPrefix != Register.None)
            throw new InvalidDataException("Startup extension is not loaded from an iterator member.");
        var state = Gprs.ToDictionary(r => r, Unknown);
        foreach (var r in Gprs) if (StableStack(r, context.Gate) is { } offset) state[r] = Stack(offset);
        var node = new Value("node"); state[Full(load.MemoryBase)] = node;
        var extensionAddress = Add(node, unchecked((long)load.MemoryDisplacement64));
        var p = context.Affected; long? collection = null;
        for (var n = 0; n < 24; n++)
        {
            var i = Meaningful(ref p);
            if (i.FlowControl == FlowControl.Call)
            {
                if (collection is not null || !ExecutableTarget(i) || state[Register.RDX].Kind != "stack" ||
                    state[Register.R8] != extensionAddress || state[Register.RCX].Kind != "stack")
                    throw new InvalidDataException($"Affected path does not insert this extension into the selected collection at 0x{i.IP:X}: RCX={state[Register.RCX]}, RDX={state[Register.RDX]}, R8={state[Register.R8]}, expected={extensionAddress}.");
                collection = state[Register.RDX].Number; Call(state, Unknown()); continue;
            }
            if (i.Mnemonic == Mnemonic.Jmp && i.FlowControl == FlowControl.UnconditionalBranch)
            {
                if (collection is null || Target(i) != context.Normal) throw new InvalidDataException("Selection does not rejoin the predicate's normal iterator path.");
                return collection.Value;
            }
            Move(i, state);
        }
        throw new InvalidDataException("Unbounded selected-collection insertion.");
    }

    internal Proof Locate()
    {
        var collection = InsertCollection(); var proofs = new List<Proof>();
        foreach (var guard in code.Values.Where(i => i.Mnemonic == Mnemonic.Jne && i.FlowControl == FlowControl.ConditionalBranch && Target(i) > Next(i)))
        {
            // Unrelated branches are leads. Once selected, the complete startup
            // proof below is mandatory; ambiguity is never resolved by score.
            if (TryGuard(guard, collection) is { } proof) proofs.Add(proof);
        }
        if (proofs.Count != 1) throw new InvalidDataException($"Expected one proven startup-loop entry; found {proofs.Count}.");
        return proofs[0];
    }

    private Proof? TryGuard(Instruction guard, long collection)
    {
        var cmp = Previous((int)guard.IP);
        if (cmp.Mnemonic != Mnemonic.Cmp || cmp.Op0Kind != OpKind.Register || cmp.Op1Kind != OpKind.Register ||
            cmp.Op0Register.GetSize() != 8 || cmp.Op1Register.GetSize() != 8) return null;
        var load = Previous((int)cmp.IP);
        if (!RegMove(load) || !Mem(load, 1, Register.RSP, 8) || load.Op0Register.GetSize() != 8 ||
            (long)load.MemoryDisplacement64 != collection) return null;
        var iterator = Full(load.Op0Register);
        var bound = Full(cmp.Op0Register) == iterator ? Full(cmp.Op1Register) : Full(cmp.Op1Register) == iterator ? Full(cmp.Op0Register) : Register.None;
        if (bound == Register.None || bound == iterator || StableStack(bound, (int)cmp.IP) != collection + 8 ||
            !Reaches(context.Normal, (int)guard.IP) || Reaches(Next(guard), Target(guard))) return null;
        var cleanup = Next(guard); var c = cleanup; var cleanupArg = Meaningful(ref c); var cleanupCall = Meaningful(ref c);
        if (!RegMove(cleanupArg) || cleanupArg.Op0Register != Register.RCX || !Mem(cleanupArg, 1, Register.RSP, 8) ||
            (long)cleanupArg.MemoryDisplacement64 != collection + 8 || !ExecutableTarget(cleanupCall)) return null;
        var factory = code.Values.Where(i => i.FlowControl == FlowControl.Call && Target(i) == context.RegistrarFactory &&
            Dominates((int)i.IP, (int)guard.IP) && Survives(Register.RAX, (int)i.IP, (int)guard.IP)).ToArray();
        if (factory.Length != 1) return null;

        var state = Gprs.ToDictionary(r => r, Unknown);
        foreach (var r in Gprs) if (StableStack(r, (int)guard.IP) is { } off) state[r] = Stack(off);
        state[iterator] = new("node"); state[bound] = Stack(collection + 8); state[Register.RAX] = new("registrar");
        var p = Target(guard); int? reasonStore = null, disable = null;
        Value? heap = null, reasonContainer = null; var sawId = false;
        var stored = new Dictionary<long, Value>();
        try
        {
            for (var n = 0; n < 48; n++)
            {
                var i = Meaningful(ref p);
                if (i.FlowControl == FlowControl.Call)
                {
                    if (!ExecutableTarget(i)) return null;
                    if (heap is null)
                    {
                        if (state[Register.RCX] != new Value("constant", 4)) return null;
                        heap = new("heap"); Call(state, heap);
                    }
                    else if (reasonContainer is null)
                    {
                        if (reasonStore is null || state[Register.RCX].Kind != "stack" || state[Register.RDX] != heap || state[Register.R8] != Add(heap, 4)) return null;
                        reasonContainer = state[Register.RCX]; var slot = reasonContainer.Number;
                        if (stored.Count != 3 || stored.GetValueOrDefault(slot) != heap ||
                            stored.GetValueOrDefault(slot + 8) != new Value("constant", 1) ||
                            stored.GetValueOrDefault(slot + 16) != new Value("constant", 1) ||
                            (slot < collection + 24 && collection < slot + 24)) return null;
                        Call(state, Unknown());
                    }
                    else if (!sawId)
                    {
                        var value = state[Register.RCX];
                        if (Target(i) != context.ExtensionIdGetter || value.Kind != "memory" || value.Parent?.Kind != "node") return null;
                        sawId = true; Call(state, new("extension-id"));
                    }
                    else
                    {
                        if (state[Register.RCX] != new Value("registrar") || state[Register.RDX] != new Value("extension-id") || state[Register.R8] != reasonContainer) return null;
                        disable = (int)i.IP; break;
                    }
                }
                else
                {
                    if (i.Mnemonic == Mnemonic.Mov && i.Op0Kind == OpKind.Memory)
                    {
                        if (heap is null || reasonContainer is not null) return null;
                        var destination = Address(i, state);
                        if (i.MemorySize.GetSize() == 4)
                        {
                            if (destination != heap || !Immediate(i, 1, 0x800000) || reasonStore is not null) return null;
                            reasonStore = (int)i.IP;
                        }
                        else if (i.MemorySize.GetSize() == 8 && destination.Kind == "stack" &&
                            destination.Number is >= 0 and <= 4096 && !stored.ContainsKey(destination.Number) &&
                            !stored.Keys.Any(k => Math.Abs(k - destination.Number) < 8))
                            stored[destination.Number] = Read(i, 1, state);
                        else return null;
                        continue;
                    }
                    Move(i, state);
                }
            }
        }
        catch (InvalidDataException) { return null; }
        if (disable is null || reasonStore is null || !Dominates((int)guard.IP, reasonStore.Value) ||
            Reaches(cleanup, reasonStore.Value)) return null;
        var backedges = code.Values.Where(i => i.Mnemonic == Mnemonic.Jne && Target(i) < (int)i.IP &&
            Target(i) >= Target(guard) && Target(i) <= reasonStore && Dominates(disable.Value, (int)i.IP) &&
            Reaches(Next(i), cleanup)).ToArray();
        var validBackedges = backedges.Where(i => ValidBackedge(i, guard, bound, collection)).ToArray();
        if (validBackedges.Length != 1) return null;
        var branchRaw = (int)guard.IP;
        int offset, length;
        if (guard.Length == 6 && image.Bytes[branchRaw] == 0x0f && image.Bytes[branchRaw + 1] == 0x85) { offset = 2; length = 4; }
        else if (guard.Length == 2 && image.Bytes[branchRaw] == 0x75) { offset = 1; length = 1; }
        else return null;
        var edits = Enumerable.Range(offset, length).Where(k => image.Bytes[branchRaw + k] != 0)
            .Select(k => new PatchEdit(branchRaw + k, image.RawOffsetToRva(branchRaw + k), image.Bytes[branchRaw + k], 0)).ToArray();
        return new(branchRaw, edits,
        [ $"Startup selection and empty/nonempty guard refer to the same stack collection (offset 0x{collection:X}).",
          $"Forward entry 0x{guard.IP:X}, reason store 0x{reasonStore:X}, registrar/extension-id arguments and cleanup validated by decoded data flow.",
          $"Backward iterator branch 0x{validBackedges[0].IP:X} is not patched; only the proven entry displacement at 0x{guard.IP:X} is neutralized." ]);
    }

    private bool ValidBackedge(Instruction back, Instruction guard, Register bound, long collection)
    {
        // Track aliases of the end sentinel through every path of the disabling
        // loop. A may-be-sentinel is not enough: merges intersect knowledge.
        var states = new Dictionary<int, HashSet<Register>>();
        var initial = new HashSet<Register> { bound };
        var pending = new Queue<int>(); var entry = Target(guard); states[entry] = initial; pending.Enqueue(entry);
        var updates = 0;
        while (pending.TryDequeue(out var p) && updates++ < 8192)
        {
            if (p == Next(guard)) continue;
            var before = states[p]; var after = before.ToHashSet(); var i = code[p];
            foreach (var r in writes[p]) after.Remove(r);
            if (RegMove(i) && i.Op0Register.GetSize() == 8 && i.Op1Kind == OpKind.Register && before.Contains(Full(i.Op1Register))) after.Add(Full(i.Op0Register));
            if (i.Mnemonic == Mnemonic.Lea && i.Op0Register.GetSize() == 8 && i.MemoryBase == Register.RSP && i.MemoryIndex == Register.None && (long)i.MemoryDisplacement64 == collection + 8) after.Add(Full(i.Op0Register));
            foreach (var next in edges[p])
            {
                if (next == Next(guard)) continue;
                if (!states.TryGetValue(next, out var prior)) { states[next] = after.ToHashSet(); pending.Enqueue(next); }
                else { var merged = prior.Intersect(after).ToHashSet(); if (!merged.SetEquals(prior)) { states[next] = merged; pending.Enqueue(next); } }
            }
        }
        if (updates >= 8192) return false;
        var previous = Previous((int)back.IP);
        for (var n = 0; n < 4 && previous.Mnemonic is Mnemonic.Mov or Mnemonic.Nop; n++) previous = Previous((int)previous.IP);
        if (previous.Mnemonic != Mnemonic.Cmp || previous.Op0Kind != OpKind.Register || previous.Op1Kind != OpKind.Register ||
            previous.Op0Register.GetSize() != 8 || previous.Op1Register.GetSize() != 8 || !states.TryGetValue((int)previous.IP, out var known)) return false;
        if (!(known.Contains(Full(previous.Op0Register)) ^ known.Contains(Full(previous.Op1Register)))) return false;
        var pFalse = Next(back); var jump = Meaningful(ref pFalse);
        return jump.Mnemonic == Mnemonic.Jmp && Target(jump) == Next(guard);
    }
}
