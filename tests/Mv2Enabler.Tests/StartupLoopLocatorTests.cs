using Iced.Intel;
using static Iced.Intel.AssemblerRegisters;

namespace Mv2Enabler.Tests;

public sealed class StartupLoopLocatorTests
{
    private sealed record Fixture(PeImage Image, StartupLoopLocator.Context Context, int Entry, int Backedge, int Reason);

    private static Fixture Create(bool renamed = false, int frameShift = 0, string mutation = "", bool farEntry = false)
    {
        var a = new Assembler(64);
        var gate = a.CreateLabel(); var normal = a.CreateLabel(); var affected = a.CreateLabel();
        var cleanup = a.CreateLabel(); var entry = a.CreateLabel(); var perItem = a.CreateLabel();
        var guard = a.CreateLabel(); var back = a.CreateLabel(); var reason = a.CreateLabel(); var end = a.CreateLabel();
        var factory = a.CreateLabel(); var alloc = a.CreateLabel(); var id = a.CreateLabel();
        var insert = a.CreateLabel(); var ctor = a.CreateLabel(); var disable = a.CreateLabel(); var destroy = a.CreateLabel();
        var bound = renamed ? r12 : r14;
        var collection = renamed ? r15 : rbx;
        var iterator = renamed ? r15 : rbp;
        var registrar = renamed ? rbx : r12;
        var count = renamed ? r13d : esi;
        var count64 = renamed ? r13 : rsi;
        var extension = renamed ? rcx : r8;
        var root = 0x48 + frameShift; var buffer = 0x20 + frameShift;
        a.lea(bound, __[rsp + root + 8]); a.lea(collection, __[rsp + root]); a.lea(rdi, __[rsp + buffer]);
        a.mov(extension, __qword_ptr[r13 + 0x38]);
        a.Label(ref gate); a.cmp(__dword_ptr[extension + 0x50], 2); a.jg(normal); a.jmp(affected);
        a.Label(ref normal); a.call(factory);
        a.mov(iterator, __qword_ptr[rsp + root]); a.cmp(iterator, bound);
        a.Label(ref guard); if (mutation == "inverted-entry") a.je(entry); else a.jne(entry);
        a.Label(ref cleanup); a.mov(rcx, __qword_ptr[rsp + root + (mutation == "wrong-cleanup" ? 16 : 8)]); a.call(destroy); a.ret();
        a.Label(ref affected);
        a.lea(r8, __[r13 + (mutation == "wrong-extension" ? 0x40 : 0x38)]);
        a.lea(rcx, __[rsp + 0x10 + frameShift]);
        if (mutation == "wrong-collection") a.lea(rdx, __[rsp + root + 16]); else a.mov(rdx, collection);
        a.call(insert); a.jmp(normal);
        if (farEntry) for (var n = 0; n < 150; n++) a.nop();
        a.Label(ref entry); a.mov(registrar, rax); a.mov(count, mutation == "wrong-count" ? 2 : 1);
        a.Label(ref perItem); a.mov(ecx, 4); a.call(alloc);
        a.mov(__qword_ptr[rsp + buffer], rax); a.mov(__qword_ptr[rsp + buffer + 16], count64);
        a.Label(ref reason); a.mov(__dword_ptr[rax + (mutation == "wrong-destination" ? 4 : 0)], mutation == "wrong-reason" ? 0x400000 : 0x800000);
        a.mov(__qword_ptr[rsp + buffer + 8], count64);
        a.lea(r8, __[rax + (mutation == "wrong-span" ? 8 : 4)]); a.mov(rcx, rdi); a.mov(rdx, rax); a.call(ctor);
        a.mov(rcx, __qword_ptr[iterator + 0x20]); a.call(mutation == "wrong-id-getter" ? alloc : id);
        a.mov(rcx, mutation == "wrong-registrar" ? rdi : registrar); a.mov(rdx, rax); a.mov(r8, rdi); a.call(disable);
        a.mov(iterator, __qword_ptr[iterator + 8]); a.cmp(iterator, bound);
        a.Label(ref back); if (mutation == "missing-backedge") a.je(perItem); else a.jne(perItem);
        a.jmp(mutation == "wrong-exit" ? normal : cleanup);
        a.Label(ref end); a.nop();
        a.Label(ref factory); a.ret(); a.Label(ref alloc); a.ret();
        a.Label(ref id); a.ret(); a.Label(ref insert); a.ret();
        a.Label(ref ctor); a.ret(); a.Label(ref disable); a.ret(); a.Label(ref destroy); a.ret();
        using var stream = new MemoryStream();
        var result = a.Assemble(new StreamCodeWriter(stream), 0x400, BlockEncoderOptions.ReturnNewInstructionOffsets);
        int Position(Label label) => checked((int)result.GetLabelRIP(label));
        var image = SemanticFixtures.Image(stream.ToArray());
        return new(image, new(0x400, Position(end), Position(gate), Position(normal), Position(affected),
            extension == rcx ? Register.RCX : Register.R8, Position(factory), Position(id)), Position(guard), Position(back), Position(reason));
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(false, 16, true)]
    [InlineData(true, 0, true)]
    [InlineData(true, 32, false)]
    public void IdentifiesEntryNotBackedgeAcrossRegisterAndFrameChanges(bool renamed, int shift, bool farEntry)
    {
        var f = Create(renamed, shift, farEntry: farEntry); var proof = new StartupLoopLocator(f.Image, f.Context).Locate();
        Assert.Equal(f.Entry, proof.Branch); Assert.NotEqual(f.Backedge, proof.Branch);
        Assert.Equal(farEntry ? 0x0f : 0x75, f.Image.Bytes[f.Entry]);
        Assert.NotEmpty(proof.Edits); Assert.All(proof.Edits, e => Assert.Equal(0, e.ReplacementByte));
        Assert.All(proof.Edits, e => Assert.InRange(e.PatchRawOffset, f.Entry + 1, f.Entry + 5));
    }

    [Theory]
    [InlineData("inverted-entry")]
    [InlineData("wrong-cleanup")]
    [InlineData("wrong-extension")]
    [InlineData("wrong-collection")]
    [InlineData("wrong-reason")]
    [InlineData("wrong-destination")]
    [InlineData("wrong-span")]
    [InlineData("wrong-id-getter")]
    [InlineData("wrong-registrar")]
    [InlineData("missing-backedge")]
    [InlineData("wrong-exit")]
    [InlineData("wrong-count")]
    public void RejectsReasonOnlyMatchesWithWrongControlOrDataFlow(string mutation)
    {
        var f = Create(mutation: mutation);
        Assert.Throws<InvalidDataException>(() => new StartupLoopLocator(f.Image, f.Context).Locate());
    }
}
