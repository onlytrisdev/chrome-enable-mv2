namespace Mv2Enabler;

internal enum PatchState
{
    Original,
    AlreadyPatched
}

internal sealed record PatchEdit(
    int PatchRawOffset,
    int PatchRva,
    byte ExpectedByte,
    byte ReplacementByte);

internal sealed record PatchTarget(
    string RuleId,
    string Description,
    int PatternRawOffset,
    int PatchRawOffset,
    int PatchRva,
    byte ExpectedByte,
    byte ReplacementByte,
    PatchState State,
    IReadOnlyList<PatchEdit> AdditionalEdits,
    IReadOnlyList<string> Evidence);

internal sealed record LocatorResult(
    bool Success,
    PatchTarget? Target,
    int PatternMatchCount,
    int SemanticMatchCount,
    IReadOnlyList<string> Diagnostics);

internal static class Mv2GateLocator
{
    private static readonly BytePattern EntryPattern = BytePattern.Parse(
        "83 7A 50 02 ?? ?? 48 8B 8A 28 02 00 00 8B 41 30 " +
        "80 BA 08 02 00 00 00 75 ?? 8B 49 68 83 F9 01 75 ?? " +
        "83 F8 05 0F 95 C1 83 F8 0A 0F 95 C0 20 C8 C3 " +
        "83 F9 08 74 ?? 83 F9 03 74 ?? 31 C0 EB ?? CC CC " +
        "83 FA 02 ?? ?? 41 83 F8 01 75 ?? 41 83 F9 05 0F 95 C1 " +
        "41 83 F9 0A 0F 95 C0 20 C8 C3 41 83 F8 08 74 ?? " +
        "41 83 F8 03 74 ?? 31 C0 EB ??");
    private static readonly BytePattern UserMayLoadPattern = BytePattern.Parse(
        "8B 41 68 83 FA 02 7F 78 8B 49 30");
    private static readonly BytePattern ReEnablePolicyPattern = BytePattern.Parse(
        "8B 41 68 83 FA 02 7F 3B 8B 49 30");
    private static readonly BytePattern StartupDisableBranchPattern = BytePattern.Parse(
        "4C 8B 74 24 40 4D 39 FE 0F 85 ?? ?? ?? ?? " +
        "48 8B 4C 24 48 E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 48 8B 56 20");
    private static readonly BytePattern StartupDisableTargetPattern = BytePattern.Parse(
        "48 89 C7 48 8D 5C 24 28 B9 04 00 00 00 E8 ?? ?? ?? ?? " +
        "48 89 44 24 28 4C 8D 40 04 4C 89 44 24 38 48 85 C0 ?? ?? ?? ?? ?? ?? " +
        "C7 00 00 00 80 00");
    private static readonly BytePattern ReturnFalseBlock = BytePattern.Parse("31 C0 EB ??");

    public static LocatorResult Locate(PeImage image)
    {
        var diagnostics = new List<string>();
        var text = image.GetSection(".text");
        if (!text.IsExecutable)
        {
            return new LocatorResult(false, null, 0, 0, ["The .text section is not executable."]);
        }

        var sectionBytes = image.Bytes.AsSpan(text.RawOffset, text.RawSize);
        var relativeMatches = EntryPattern.FindAll(sectionBytes);
        var valid = new List<(int RawOffset, IReadOnlyList<string> Evidence)>();

        foreach (var relative in relativeMatches)
        {
            var rawOffset = text.RawOffset + relative;
            if (TryValidate(image.Bytes, text, rawOffset, out var evidence, out var reason))
            {
                valid.Add((rawOffset, evidence));
            }
            else
            {
                diagnostics.Add($"Rejected candidate 0x{rawOffset:X}: {reason}");
            }
        }

        if (valid.Count != 1)
        {
            diagnostics.Add($"Fail-closed: expected exactly one semantic MV2 impact-checker target, found {valid.Count}.");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var selected = valid[0];
        var patchRawOffset = selected.RawOffset + 4;
        var current = image.Bytes[patchRawOffset];
        var secondPatchRawOffset = selected.RawOffset + 0x43;
        var secondCurrent = image.Bytes[secondPatchRawOffset];
        var userMayLoadMatches = UserMayLoadPattern.FindAll(sectionBytes);
        var reEnablePolicyMatches = ReEnablePolicyPattern.FindAll(sectionBytes);
        var startupDisableBranchMatches = StartupDisableBranchPattern.FindAll(sectionBytes);
        if (userMayLoadMatches.Count != 1 ||
            reEnablePolicyMatches.Count != 1 ||
            startupDisableBranchMatches.Count != 1)
        {
            diagnostics.Add(
                "Fail-closed: expected one UserMayLoad clone, one re-enable clone, and one startup-disable branch; " +
                $"found {userMayLoadMatches.Count}, {reEnablePolicyMatches.Count}, and {startupDisableBranchMatches.Count}.");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var userMayLoadRawOffset = text.RawOffset + userMayLoadMatches[0];
        var reEnablePolicyRawOffset = text.RawOffset + reEnablePolicyMatches[0];
        var startupDisableBranchRawOffset = text.RawOffset + startupDisableBranchMatches[0] + 8;
        if (!TryValidateStartupDisableBranch(
                image.Bytes,
                text,
                startupDisableBranchRawOffset,
                out var startupDisableEvidence,
                out var startupDisableReason))
        {
            diagnostics.Add($"Rejected startup-disable branch: {startupDisableReason}");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var additionalEdits = new List<PatchEdit>
        {
            new(secondPatchRawOffset, image.RawOffsetToRva(secondPatchRawOffset), secondCurrent, 0xeb),
            new(userMayLoadRawOffset + 5, image.RawOffsetToRva(userMayLoadRawOffset + 5), image.Bytes[userMayLoadRawOffset + 5], 0x00),
            new(userMayLoadRawOffset + 6, image.RawOffsetToRva(userMayLoadRawOffset + 6), image.Bytes[userMayLoadRawOffset + 6], 0x7d),
            new(reEnablePolicyRawOffset + 5, image.RawOffsetToRva(reEnablePolicyRawOffset + 5), image.Bytes[reEnablePolicyRawOffset + 5], 0x00),
            new(reEnablePolicyRawOffset + 6, image.RawOffsetToRva(reEnablePolicyRawOffset + 6), image.Bytes[reEnablePolicyRawOffset + 6], 0x7d)
        };
        for (var index = 0; index < sizeof(int); index++)
        {
            var displacementRawOffset = startupDisableBranchRawOffset + 2 + index;
            if (image.Bytes[displacementRawOffset] != 0)
            {
                additionalEdits.Add(new PatchEdit(
                    displacementRawOffset,
                    image.RawOffsetToRva(displacementRawOffset),
                    image.Bytes[displacementRawOffset],
                    0x00));
            }
        }

        var allOriginal = current == 0x7f && additionalEdits.All(edit => edit.ExpectedByte != edit.ReplacementByte);
        var allPatched = current == 0xeb && additionalEdits.All(edit => edit.ExpectedByte == edit.ReplacementByte);
        if (!allOriginal && !allPatched)
        {
            diagnostics.Add("Fail-closed: MV2 gate clones have inconsistent or unexpected patch state.");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var state = allPatched ? PatchState.AlreadyPatched : PatchState.Original;
        var target = new PatchTarget(
            "chromium.mv2-impact-checker.extension-overload.return-unaffected.v4",
            "Force the MV2 impact checkers to take the unaffected path and neutralize the verified startup disable branch.",
            selected.RawOffset,
            patchRawOffset,
            image.RawOffsetToRva(patchRawOffset),
            current,
            0xeb,
            state,
            additionalEdits,
            [.. selected.Evidence, .. startupDisableEvidence]);

        diagnostics.Add("Exactly one target passed all semantic checks.");
        return new LocatorResult(true, target, relativeMatches.Count, valid.Count, diagnostics);
    }

    private static bool TryValidate(
        byte[] bytes,
        PeSection section,
        int rawOffset,
        out IReadOnlyList<string> evidence,
        out string reason)
    {
        var items = new List<string>();
        evidence = items;
        reason = string.Empty;

        var branchOpcode = bytes[rawOffset + 4];
        if (branchOpcode is not 0x7f and not 0xeb)
        {
            reason = $"branch opcode is 0x{branchOpcode:X2}, expected JG (7F) or patched JMP (EB)";
            return false;
        }

        var displacement = unchecked((sbyte)bytes[rawOffset + 5]);
        var branchTarget = rawOffset + 6 + displacement;
        if (branchTarget < section.RawOffset || branchTarget + ReturnFalseBlock.Length > section.RawOffset + section.RawSize)
        {
            reason = "short branch leaves the executable section";
            return false;
        }

        if (!ReturnFalseBlock.MatchesAt(bytes, branchTarget))
        {
            reason = "branch target is not the expected XOR EAX,EAX return-false block";
            return false;
        }

        var secondBranch = rawOffset + 0x43;
        if (bytes[secondBranch] is not 0x7f and not 0xeb)
        {
            reason = "integer-overload branch opcode is not JG or patched JMP";
            return false;
        }

        var secondTarget = secondBranch + 2 + unchecked((sbyte)bytes[secondBranch + 1]);
        if (!ReturnFalseBlock.MatchesAt(bytes, secondTarget))
        {
            reason = "integer-overload branch does not reach its XOR EAX,EAX return-false block";
            return false;
        }

        items.Add("entry reads Extension.manifest_version and compares it with 2");
        items.Add("function accepts only manifest types Extension (1), LoginScreenExtension (8), and UserScript (3)");
        items.Add("function reads Extension type/location fields and excludes component locations 5 and 10");
        items.Add($"branch target 0x{branchTarget:X} is XOR EAX,EAX followed by the shared return");
        items.Add($"adjacent integer-argument overload has the same checks and return-false target at 0x{secondTarget:X}");
        return true;
    }

    private static bool TryValidateStartupDisableBranch(
        byte[] bytes,
        PeSection section,
        int branchRawOffset,
        out IReadOnlyList<string> evidence,
        out string reason)
    {
        var items = new List<string>();
        evidence = items;
        reason = string.Empty;

        if (bytes[branchRawOffset] != 0x0f || bytes[branchRawOffset + 1] != 0x85)
        {
            reason = "expected a near JNE opcode";
            return false;
        }

        var displacement = BitConverter.ToInt32(bytes, branchRawOffset + 2);
        if (displacement <= 0)
        {
            reason = $"expected a forward branch, got displacement {displacement}";
            return false;
        }

        var branchTarget = checked(branchRawOffset + 6 + displacement);
        if (branchTarget < section.RawOffset ||
            branchTarget + StartupDisableTargetPattern.Length > section.RawOffset + section.RawSize)
        {
            reason = "branch target leaves the executable section";
            return false;
        }

        if (!StartupDisableTargetPattern.MatchesAt(bytes, branchTarget))
        {
            reason = "branch target is not the verified loop that constructs disable reason 0x800000";
            return false;
        }

        items.Add("startup manager branch targets a loop that constructs disable reason 0x800000");
        items.Add($"neutralizing the JNE displacement at raw offset 0x{branchRawOffset:X} preserves the cleanup fallthrough");
        return true;
    }
}
