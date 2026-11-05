using Iced.Intel;
using static Iced.Intel.AssemblerRegisters;

namespace Mv2Enabler.Tests;

public sealed class AutomaticGateLocatorTests
{
    [Theory]
    [InlineData("154.0.1.2", true, true)]
    [InlineData("157.0.1.2", false, true)]
    [InlineData("157.0.1.2", true, false)]
    [InlineData("158.0.1.2", false, true)]
    [InlineData("159.0.1.2", false, false)]
    [InlineData("158.0.1.2", true, false)]
    [InlineData("unknown", false, false)]
    public void AutomaticSemanticRejectionIsNotBypassedByLegacyRules(string version, bool resources, bool allowed) =>
        Assert.Equal(allowed, AnalysisService.CanUseLegacyFallback(version, resources));

    // Instructions are assembled for each fixture. No real Chrome is needed.
    private static byte[] Checker(bool renamed = false, bool changedFields = false, bool padding = false,
        bool wrongType = false, bool wrongReturn = false, bool wrongOperation = false, bool loop = false,
        bool farBranch = false, bool truncatedType = false)
    {
        var a = new Assembler(64); a.int3();
        var locations = a.CreateLabel(); var otherTypes = a.CreateLabel(); var unaffected = a.CreateLabel(); var ret = a.CreateLabel();
        var root = renamed ? rdi : rdx;
        var location = renamed ? edx : eax;
        var manifest = changedFields ? 0x60 : 0x50; var pointer = changedFields ? 0x248 : 0x228;
        var flag = changedFields ? 0x218 : 0x208; var loc = changedFields ? 0x70 : 0x50; var type = changedFields ? 0x98 : 0x88;
        a.cmp(__dword_ptr[root + manifest], 2); a.jg(unaffected);
        if (padding) a.nop();
        a.mov(rcx, __qword_ptr[root + pointer]); a.mov(location, __dword_ptr[rcx + loc]);
        a.cmp(__byte_ptr[root + flag], 0); a.jne(locations);
        a.mov(ecx, __dword_ptr[rcx + type]); a.cmp(ecx, 1); a.jne(otherTypes);
        a.Label(ref locations); a.cmp(location, 5); a.setne(cl); a.cmp(location, 10); a.setne(al);
        if (wrongOperation) a.or(al, cl); else a.and(al, cl);
        a.Label(ref ret); a.ret();
        a.Label(ref otherTypes);
        if (truncatedType) a.cmp(cl, 8); else a.cmp(ecx, 8);
        a.je(locations); a.cmp(ecx, wrongType ? 4 : 3); a.je(locations);
        if (farBranch) { a.jmp(unaffected); for (var n = 0; n < 200; n++) a.int3(); }
        a.Label(ref unaffected);
        if (wrongReturn) a.mov(eax, 1); else a.xor(eax, eax);
        if (loop) a.jmp(unaffected); else a.jmp(ret);
        using var stream = new MemoryStream(); a.Assemble(new StreamCodeWriter(stream), 0);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void DiscoversPredicateDespiteRegisterFieldAndSpacingChanges(bool registers, bool fields, bool padding)
    {
        var image = SemanticFixtures.Image(Checker(registers, fields, padding));
        Assert.True(new AutomaticGateLocator(image).ValidateChecker(0x401, false));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("return")]
    [InlineData("operation")]
    [InlineData("loop")]
    public void RejectsSemanticallyChangedChecker(string mutation)
    {
        var image = SemanticFixtures.Image(Checker(wrongType: mutation == "type", wrongReturn: mutation == "return",
            wrongOperation: mutation == "operation", loop: mutation == "loop"));
        Assert.False(new AutomaticGateLocator(image).ValidateChecker(0x401, false));
    }

    [Fact]
    public void RejectsBranchOutsideExecutableSection()
    {
        var code = Checker(); code[6] = 0x7f; // short JG displacement escapes this tiny fixture
        Assert.False(new AutomaticGateLocator(SemanticFixtures.Image(code)).ValidateChecker(0x401, false));
    }

    [Fact]
    public void RefusesPartialFamilyEvenWhenOneCheckerIsValid()
    {
        var image = SemanticFixtures.Image(Checker());
        Assert.False(AutomaticGateLocator.Locate(image, new(65000, "test")).Success);
    }

    [Fact]
    public void DiscoverySkipsInvalidCandidateAndStillFindsFollowingChecker()
    {
        // A plausible CMP followed by an overlength instruction in inline data.
        // The candidate must fail locally, not abort discovery of later code.
        var noise = new byte[] { 0xcc, 0x83, 0xf9, 2 }.Concat(Enumerable.Repeat((byte)0x66, 16));
        var image = SemanticFixtures.Image(noise.Concat(Checker()).ToArray());
        Assert.Equal((1, 0), new AutomaticGateLocator(image).DiscoverCounts());
        Assert.False(AutomaticGateLocator.Locate(image, new(65000, "test")).Success);
    }

    [Fact]
    public void DecodesLongBranchWithoutAssumingShortOffsets() => Assert.True(
        new AutomaticGateLocator(SemanticFixtures.Image(Checker(farBranch: true))).ValidateChecker(0x401, false));

    [Fact]
    public void RejectsTruncatedTypeComparisonRatherThanAssumingSampledIntegersAreProof() => Assert.False(
        new AutomaticGateLocator(SemanticFixtures.Image(Checker(truncatedType: true))).ValidateChecker(0x401, false));

    [Fact]
    public void IntegerBoundIsMandatoryBeforeBitTest()
    {
        var code = Convert.FromHexString("CC31C083F9027F1F83FA08771AB90A0100000FA3D173104183F8050F95C14183F80A0F95C020C8C3");
        Assert.True(new AutomaticGateLocator(SemanticFixtures.Image(code)).ValidateChecker(0x403, true));
        code[11] = 0x90;
        Assert.False(new AutomaticGateLocator(SemanticFixtures.Image(code)).ValidateChecker(0x403, true));
    }
}
