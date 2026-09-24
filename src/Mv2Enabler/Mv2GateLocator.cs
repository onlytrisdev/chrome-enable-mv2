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
    private static readonly BytePattern SplitEntryPattern = BytePattern.Parse(
        "83 7A 50 02 ?? ?? 48 8B 8A 28 02 00 00 8B 41 30 " +
        "80 BA 08 02 00 00 00 75 ?? 8B 49 68 83 F9 01 75 ?? " +
        "83 F8 05 0F 95 C1 83 F8 0A 0F 95 C0 20 C8 C3 " +
        "83 F9 08 74 ?? 83 F9 03 74 ?? 31 C0 EB ?? CC CC");
    private static readonly BytePattern SplitIntegerEntryPattern = BytePattern.Parse(
        "31 C0 83 F9 02 ?? ?? 83 FA 08 77 ?? B9 0A 01 00 00 " +
        "0F A3 D1 73 ?? 41 83 F8 05 0F 95 C1 41 83 F8 0A 0F 95 C0 20 C8 C3 CC CC");
    private static readonly BytePattern MustRemainDisabledV5Pattern = BytePattern.Parse(
        "83 7F 50 02 0F 8F ?? ?? ?? ?? 48 8B 8F 28 02 00 00 8B 41 30 " +
        "80 BF 08 02 00 00 00 75 ?? 8B 49 68 83 F9 01 75 ?? 31 FF " +
        "83 F8 05 74 ?? 83 F8 0A 75 ??");
    private static readonly BytePattern ReEnableV5Pattern = BytePattern.Parse(
        "83 7E 50 02 ?? ?? 48 8B 8E 28 02 00 00 8B 41 30 " +
        "80 BE 08 02 00 00 00 75 ?? 8B 49 68 83 F9 01 75 ?? " +
        "83 F8 0A 74 ?? 83 F8 05 74 ?? 48 83 C4 20 5B 5F 5E C3");
    private static readonly BytePattern UserMayInstallV5Pattern = BytePattern.Parse(
        "83 7F 50 02 ?? ?? 48 8B 8F 28 02 00 00 8B 41 30 " +
        "80 BF 08 02 00 00 00 75 ?? 8B 49 68 83 F9 01 0F 85 ?? ?? ?? ?? " +
        "83 F8 05 74 ?? 83 F8 0A 74 ?? 4C 8D B4 24 80 00 00 00 " +
        "4C 89 F1 BA B3 1F 00 00");
    private static readonly BytePattern UserMayInstallV6Pattern = BytePattern.Parse(
        "83 7F 50 02 ?? ?? 48 8B 8F 28 02 00 00 8B 41 30 " +
        "80 BF 08 02 00 00 00 75 ?? 8B 49 68 83 F9 01 0F 85 ?? ?? ?? ?? " +
        "83 F8 05 74 ?? 83 F8 0A 74 ?? 4C 8D B4 24 80 00 00 00 " +
        "4C 89 F1 BA 95 1F 00 00");
    private static readonly BytePattern SplitEntryV7Pattern = BytePattern.Parse(
        "83 7A 50 02 ?? ?? 48 8B 8A 28 02 00 00 8B 41 50 " +
        "80 BA 08 02 00 00 00 75 ?? 8B 89 88 00 00 00 83 F9 01 75 ?? " +
        "83 F8 05 0F 95 C1 83 F8 0A 0F 95 C0 20 C8 C3 " +
        "83 F9 08 74 ?? 83 F9 03 74 ?? 31 C0 EB ?? CC CC");
    private static readonly BytePattern MustRemainDisabledV7Pattern = BytePattern.Parse(
        "83 7F 50 02 0F 8F ?? ?? ?? ?? 48 8B 8F 28 02 00 00 8B 41 50 " +
        "80 BF 08 02 00 00 00 75 ?? 8B 89 88 00 00 00 83 F9 01 75 ?? 31 FF " +
        "83 F8 05 74 ?? 83 F8 0A 75 ??");
    private static readonly BytePattern ReEnableV7Pattern = BytePattern.Parse(
        "83 7E 50 02 ?? ?? 48 8B 8E 28 02 00 00 8B 41 50 " +
        "80 BE 08 02 00 00 00 75 ?? 8B 89 88 00 00 00 83 F9 01 75 ?? " +
        "83 F8 0A 74 ?? 83 F8 05 74 ?? 48 83 C4 20 5B 5F 5E C3");
    private static readonly BytePattern UserMayInstallV7Pattern = BytePattern.Parse(
        "83 7F 50 02 ?? ?? 48 8B 8F 28 02 00 00 8B 41 50 " +
        "80 BF 08 02 00 00 00 75 ?? 8B 89 88 00 00 00 83 F9 01 0F 85 ?? ?? ?? ?? " +
        "83 F8 05 74 ?? 83 F8 0A 74 ?? 4C 8D B4 24 80 00 00 00 " +
        "4C 89 F1 BA B3 1F 00 00");
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
    private static readonly BytePattern StartupDisableBranchV5Pattern = BytePattern.Parse(
        "4C 8B 74 24 40 4D 39 FE 0F 85 ?? ?? ?? ?? " +
        "48 8B 4C 24 48 E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 48 8B 56 18");
    private static readonly BytePattern StartupDisableBranchV6Pattern = BytePattern.Parse(
        "4C 8B 7C 24 48 4D 39 E7 0F 85 ?? ?? ?? ?? " +
        "48 8B 4C 24 50 E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 48 8B 56 18");
    private static readonly BytePattern StartupDisableTargetPattern = BytePattern.Parse(
        "48 89 C7 48 8D 5C 24 28 B9 04 00 00 00 E8 ?? ?? ?? ?? " +
        "48 89 44 24 28 4C 8D 40 04 4C 89 44 24 38 48 85 C0 ?? ?? ?? ?? ?? ?? " +
        "C7 00 00 00 80 00");
    private static readonly BytePattern StartupDisableTargetV5Pattern = BytePattern.Parse(
        "48 89 C7 48 8D 5C 24 28 B9 04 00 00 00 E8 ?? ?? ?? ?? " +
        "48 89 44 24 28 4C 8D 40 04 4C 89 44 24 38 C7 00 00 00 80 00");
    private static readonly BytePattern StartupDisableTargetV6Pattern = BytePattern.Parse(
        "48 89 C3 41 BD 01 00 00 00 4C 8D 74 24 40 B9 04 00 00 00 E8 ?? ?? ?? ?? " +
        "48 89 44 24 28 4C 89 6C 24 38 C7 00 00 00 80 00");
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
        if (relativeMatches.Count == 0)
        {
            return LocateSplit(image, text, sectionBytes);
        }

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
                StartupDisableTargetPattern,
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

    private static LocatorResult LocateSplit(PeImage image, PeSection text, ReadOnlySpan<byte> sectionBytes)
    {
        var diagnostics = new List<string>();
        var splitEntryV7Matches = SplitEntryV7Pattern.FindAll(sectionBytes);
        var isV7 = splitEntryV7Matches.Count == 2;
        var relativeMatches = isV7 ? splitEntryV7Matches : SplitEntryPattern.FindAll(sectionBytes);
        var valid = new List<(int RawOffset, IReadOnlyList<string> Evidence)>();

        foreach (var relative in relativeMatches)
        {
            var rawOffset = text.RawOffset + relative;
            if (TryValidateSplit(image.Bytes, text, rawOffset, out var evidence, out var reason))
            {
                valid.Add((rawOffset, evidence));
            }
            else
            {
                diagnostics.Add($"Rejected split candidate 0x{rawOffset:X}: {reason}");
            }
        }

        if (relativeMatches.Count != 2 || valid.Count != 2)
        {
            diagnostics.Add(
                "Fail-closed: expected exactly two semantic copies of the split MV2 impact checker; " +
                $"found {relativeMatches.Count} pattern matches and {valid.Count} valid copies.");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        valid.Sort((left, right) => left.RawOffset.CompareTo(right.RawOffset));
        var primary = valid[0];
        var secondary = valid[1];
        var patchRawOffset = primary.RawOffset + 4;
        var secondaryPatchRawOffset = secondary.RawOffset + 4;
        var current = image.Bytes[patchRawOffset];
        var secondaryCurrent = image.Bytes[secondaryPatchRawOffset];

        var integerMatches = SplitIntegerEntryPattern.FindAll(sectionBytes);
        if (integerMatches.Count != 1)
        {
            diagnostics.Add(
                "Fail-closed: expected exactly one split integer-argument MV2 impact checker; " +
                $"found {integerMatches.Count}.");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var integerRawOffset = text.RawOffset + integerMatches[0];
        if (!TryValidateSplitInteger(
                image.Bytes,
                text,
                integerRawOffset,
                out var integerEvidence,
                out var integerReason))
        {
            diagnostics.Add($"Rejected integer-argument checker: {integerReason}");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var integerPatchRawOffset = integerRawOffset + 5;
        var integerCurrent = image.Bytes[integerPatchRawOffset];

        var userMayInstallV7Matches = isV7 ? UserMayInstallV7Pattern.FindAll(sectionBytes) : [];
        var userMayInstallV6Matches = !isV7 ? UserMayInstallV6Pattern.FindAll(sectionBytes) : [];
        var isV6 = !isV7 && userMayInstallV6Matches.Count == 1;

        var mustRemainDisabledMatches = isV7
            ? MustRemainDisabledV7Pattern.FindAll(sectionBytes)
            : MustRemainDisabledV5Pattern.FindAll(sectionBytes);
        var reEnableMatches = isV7
            ? ReEnableV7Pattern.FindAll(sectionBytes)
            : ReEnableV5Pattern.FindAll(sectionBytes);
        var userMayInstallMatches = isV7
            ? userMayInstallV7Matches
            : isV6
                ? userMayInstallV6Matches
                : UserMayInstallV5Pattern.FindAll(sectionBytes);
        var chromeVersionLabel = isV7 ? "Chrome 154" : isV6 ? "Chrome 153" : "Chrome 152";

        if (mustRemainDisabledMatches.Count != 1 ||
            reEnableMatches.Count != 1 ||
            userMayInstallMatches.Count != 1)
        {
            diagnostics.Add(
                $"Fail-closed: expected one {chromeVersionLabel} MustRemainDisabled clone, one re-enable clone, and one UserMayInstall clone; " +
                $"found {mustRemainDisabledMatches.Count}, {reEnableMatches.Count}, and {userMayInstallMatches.Count}.");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var mustRemainDisabledRawOffset = text.RawOffset + mustRemainDisabledMatches[0];
        var reEnableRawOffset = text.RawOffset + reEnableMatches[0];
        var userMayInstallRawOffset = text.RawOffset + userMayInstallMatches[0];
        if (!TryValidateNearUnaffectedBranch(
                image.Bytes,
                text,
                mustRemainDisabledRawOffset + 4,
                out var mustRemainEvidence,
                out var mustRemainReason))
        {
            diagnostics.Add($"Rejected MustRemainDisabled clone: {mustRemainReason}");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        if (!TryValidateReEnableBranch(
                image.Bytes,
                text,
                reEnableRawOffset + 4,
                out var reEnableEvidence,
                out var reEnableReason))
        {
            diagnostics.Add($"Rejected re-enable clone: {reEnableReason}");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var expectedResourceId = isV6 ? 8085 : 8115;
        if (!TryValidateUserMayInstallBranch(
                image.Bytes,
                text,
                userMayInstallRawOffset + 4,
                expectedResourceId,
                out var userMayInstallEvidence,
                out var userMayInstallReason))
        {
            diagnostics.Add($"Rejected UserMayInstall clone: {userMayInstallReason}");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var startupDisableBranchPattern = (isV6 || isV7) ? StartupDisableBranchV6Pattern : StartupDisableBranchV5Pattern;
        var startupDisableTargetPattern = (isV6 || isV7) ? StartupDisableTargetV6Pattern : StartupDisableTargetV5Pattern;
        var startupDisableBranchMatches = startupDisableBranchPattern.FindAll(sectionBytes);
        if (startupDisableBranchMatches.Count != 1)
        {
            diagnostics.Add(
                $"Fail-closed: expected exactly one startup-disable branch for the {chromeVersionLabel} checker pair; " +
                $"found {startupDisableBranchMatches.Count}.");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var startupDisableBranchRawOffset = text.RawOffset + startupDisableBranchMatches[0] + 8;
        if (!TryValidateStartupDisableBranch(
                image.Bytes,
                text,
                startupDisableBranchRawOffset,
                startupDisableTargetPattern,
                out var startupDisableEvidence,
                out var startupDisableReason))
        {
            diagnostics.Add($"Rejected startup-disable branch: {startupDisableReason}");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var additionalEdits = new List<PatchEdit>
        {
            new(
                secondaryPatchRawOffset,
                image.RawOffsetToRva(secondaryPatchRawOffset),
                secondaryCurrent,
                0xeb),
            new(
                integerPatchRawOffset,
                image.RawOffsetToRva(integerPatchRawOffset),
                integerCurrent,
                0xeb),
            new(
                mustRemainDisabledRawOffset + 4,
                image.RawOffsetToRva(mustRemainDisabledRawOffset + 4),
                image.Bytes[mustRemainDisabledRawOffset + 4],
                0x90),
            new(
                mustRemainDisabledRawOffset + 5,
                image.RawOffsetToRva(mustRemainDisabledRawOffset + 5),
                image.Bytes[mustRemainDisabledRawOffset + 5],
                0xe9),
            new(
                reEnableRawOffset + 4,
                image.RawOffsetToRva(reEnableRawOffset + 4),
                image.Bytes[reEnableRawOffset + 4],
                0xeb),
            new(
                userMayInstallRawOffset + 4,
                image.RawOffsetToRva(userMayInstallRawOffset + 4),
                image.Bytes[userMayInstallRawOffset + 4],
                0xeb)
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
            diagnostics.Add($"Fail-closed: {chromeVersionLabel} MV2 checker copies have inconsistent or unexpected patch state.");
            return new LocatorResult(false, null, relativeMatches.Count, valid.Count, diagnostics);
        }

        var ruleId = isV7
            ? "chromium.mv2-impact-checker.split-extension-copies.return-unaffected.v7"
            : isV6
                ? "chromium.mv2-impact-checker.split-extension-copies.return-unaffected.v6"
                : "chromium.mv2-impact-checker.split-extension-copies.return-unaffected.v5";
        var description = isV7
            ? "Force both Chrome 154 MV2 impact-checker copies to take the unaffected path and neutralize the verified startup disable branch."
            : isV6
                ? "Force both Chrome 153 MV2 impact-checker copies to take the unaffected path and neutralize the verified startup disable branch."
                : "Force both Chrome 152 MV2 impact-checker copies to take the unaffected path and neutralize the verified startup disable branch.";

        var target = new PatchTarget(
            ruleId,
            description,
            primary.RawOffset,
            patchRawOffset,
            image.RawOffsetToRva(patchRawOffset),
            current,
            0xeb,
            allPatched ? PatchState.AlreadyPatched : PatchState.Original,
            additionalEdits,
            [
                .. primary.Evidence,
                $"second byte-identical checker copy validated at raw offset 0x{secondary.RawOffset:X}",
                .. integerEvidence,
                .. mustRemainEvidence,
                .. reEnableEvidence,
                .. userMayInstallEvidence,
                .. startupDisableEvidence
            ]);

        diagnostics.Add($"Exactly two {chromeVersionLabel} checker copies passed all semantic checks and were grouped into one atomic patch set.");
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

    private static bool TryValidateSplit(
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

        items.Add("checker reads Extension.manifest_version and compares it with 2");
        items.Add("checker accepts only manifest types Extension (1), LoginScreenExtension (8), and UserScript (3)");
        items.Add("checker reads Extension type/location fields and excludes component locations 5 and 10");
        items.Add($"branch target 0x{branchTarget:X} is XOR EAX,EAX followed by the shared return");
        return true;
    }

    private static bool TryValidateSplitInteger(
        byte[] bytes,
        PeSection section,
        int rawOffset,
        out IReadOnlyList<string> evidence,
        out string reason)
    {
        var items = new List<string>();
        evidence = items;
        reason = string.Empty;

        var branchRawOffset = rawOffset + 5;
        var branchOpcode = bytes[branchRawOffset];
        if (branchOpcode is not 0x7f and not 0xeb)
        {
            reason = $"branch opcode is 0x{branchOpcode:X2}, expected JG (7F) or patched JMP (EB)";
            return false;
        }

        var displacement = unchecked((sbyte)bytes[branchRawOffset + 1]);
        var branchTarget = branchRawOffset + 2 + displacement;
        if (branchTarget < section.RawOffset || branchTarget >= section.RawOffset + section.RawSize)
        {
            reason = "manifest-version branch leaves the executable section";
            return false;
        }

        if (bytes[branchTarget] != 0xc3)
        {
            reason = "manifest-version branch does not reach the shared RET with EAX initialized to false";
            return false;
        }

        items.Add("integer-argument checker initializes EAX to false and compares manifest version with 2");
        items.Add("integer-argument checker accepts manifest types 1, 3, and 8 through bit mask 0x10A");
        items.Add("integer-argument checker excludes component locations 5 and 10 before the shared return");
        items.Add($"integer checker return-false target validated at raw offset 0x{branchTarget:X}");
        return true;
    }

    private static bool TryValidateNearUnaffectedBranch(
        byte[] bytes,
        PeSection section,
        int branchRawOffset,
        out IReadOnlyList<string> evidence,
        out string reason)
    {
        var items = new List<string>();
        evidence = items;
        reason = string.Empty;
        if (bytes[branchRawOffset] != 0x0f || bytes[branchRawOffset + 1] != 0x8f)
        {
            reason = "expected a near JG opcode";
            return false;
        }

        var displacement = BitConverter.ToInt32(bytes, branchRawOffset + 2);
        var branchTarget = checked(branchRawOffset + 6 + displacement);
        if (branchTarget < section.RawOffset || branchTarget + 4 > section.RawOffset + section.RawSize)
        {
            reason = "near manifest-version branch leaves the executable section";
            return false;
        }

        if (bytes[branchTarget] != 0x31 || bytes[branchTarget + 2] != 0xeb)
        {
            reason = "near manifest-version branch does not reach the clone's false-return path";
            return false;
        }

        var disableReasonFound = false;
        for (var offset = branchRawOffset; offset <= branchRawOffset + 128 - 5; offset++)
        {
            if (bytes[offset] == 0xb8 &&
                bytes[offset + 1] == 0x00 &&
                bytes[offset + 2] == 0x00 &&
                bytes[offset + 3] == 0x80 &&
                bytes[offset + 4] == 0x00)
            {
                disableReasonFound = true;
                break;
            }
        }

        if (!disableReasonFound)
        {
            reason = "clone does not construct disable reason 0x800000 near the affected path";
            return false;
        }

        items.Add("MustRemainDisabled clone constructs disable reason 0x800000 only on the affected path");
        items.Add($"near manifest-version branch reaches its false-return path at raw offset 0x{branchTarget:X}");
        return true;
    }

    private static bool TryValidateReEnableBranch(
        byte[] bytes,
        PeSection section,
        int branchRawOffset,
        out IReadOnlyList<string> evidence,
        out string reason)
    {
        var items = new List<string>();
        evidence = items;
        reason = string.Empty;
        if (bytes[branchRawOffset] is not 0x7f and not 0xeb)
        {
            reason = "expected a short JG or patched JMP opcode";
            return false;
        }

        var branchTarget = branchRawOffset + 2 + unchecked((sbyte)bytes[branchRawOffset + 1]);
        if (branchTarget < section.RawOffset || branchTarget + 4 > section.RawOffset + section.RawSize)
        {
            reason = "re-enable branch leaves the executable section";
            return false;
        }

        if (bytes[branchTarget] != 0x48 || bytes[branchTarget + 1] != 0x8b)
        {
            reason = "re-enable branch does not reach the verified disable-reason removal path";
            return false;
        }

        items.Add("re-enable clone first verifies disable reason 0x800000, then repeats the MV2 type/location checks");
        items.Add($"re-enable manifest-version branch reaches disable-reason removal at raw offset 0x{branchTarget:X}");
        return true;
    }

    private static bool TryValidateUserMayInstallBranch(
        byte[] bytes,
        PeSection section,
        int branchRawOffset,
        int expectedResourceId,
        out IReadOnlyList<string> evidence,
        out string reason)
    {
        var items = new List<string>();
        evidence = items;
        reason = string.Empty;
        if (bytes[branchRawOffset] is not 0x7f and not 0xeb)
        {
            reason = "expected a short JG or patched JMP opcode";
            return false;
        }

        var branchTarget = branchRawOffset + 2 + unchecked((sbyte)bytes[branchRawOffset + 1]);
        if (branchTarget < section.RawOffset || branchTarget + 4 > section.RawOffset + section.RawSize)
        {
            reason = "UserMayInstall branch leaves the executable section";
            return false;
        }

        if (bytes[branchTarget] != 0x4c || bytes[branchTarget + 1] != 0x8d)
        {
            reason = "UserMayInstall branch does not reach the verified normal policy path";
            return false;
        }

        items.Add("UserMayInstall clone reads manifest version, type, and location before its MV2 rejection path");
        items.Add($"affected path loads localized resource {expectedResourceId} for the unsupported-manifest installation error");
        items.Add($"manifest-version branch reaches the normal UserMayLoad policy path at raw offset 0x{branchTarget:X}");
        return true;
    }

    private static bool TryValidateStartupDisableBranch(
        byte[] bytes,
        PeSection section,
        int branchRawOffset,
        BytePattern targetPattern,
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
            branchTarget + targetPattern.Length > section.RawOffset + section.RawSize)
        {
            reason = "branch target leaves the executable section";
            return false;
        }

        if (!targetPattern.MatchesAt(bytes, branchTarget))
        {
            reason = "branch target is not the verified loop that constructs disable reason 0x800000";
            return false;
        }

        items.Add("startup manager branch targets a loop that constructs disable reason 0x800000");
        items.Add($"neutralizing the JNE displacement at raw offset 0x{branchRawOffset:X} preserves the cleanup fallthrough");
        return true;
    }
}
