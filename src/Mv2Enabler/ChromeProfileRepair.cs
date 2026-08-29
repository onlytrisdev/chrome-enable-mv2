using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Mv2Enabler;

internal sealed record ProfileRepairResult(
    string UserDataDirectory,
    int ProfilesScanned,
    int ProfilesChanged,
    int ExtensionsReEnabled,
    IReadOnlyList<string> ChangedProfiles,
    IReadOnlyList<string> BackupFiles);

internal static class ChromeProfileRepair
{
    internal const int UnsupportedManifestVersionReason = 1 << 23;
    private const string RegistryValidationSeed = "ChromeRegistryHashStoreValidationSeed";
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static ProfileRepairResult RepairBeforeLaunch(
        ChromeInstallation installation,
        IReadOnlyList<string> chromeArguments)
    {
        var userDataDirectory = ResolveUserDataDirectory(chromeArguments);
        if (!Directory.Exists(userDataDirectory))
        {
            return new ProfileRepairResult(userDataDirectory, 0, 0, 0, [], []);
        }

        var profiles = Directory.EnumerateDirectories(userDataDirectory)
            .Select(directory => new
            {
                Directory = directory,
                SecurePreferences = Path.Combine(directory, "Secure Preferences")
            })
            .Where(item => File.Exists(item.SecurePreferences))
            .OrderBy(item => item.Directory, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var documents = new List<ProfileDocument>();
        foreach (var profile in profiles)
        {
            var root = JsonNode.Parse(File.ReadAllText(profile.SecurePreferences))?.AsObject()
                ?? throw new InvalidDataException($"Secure Preferences is not a JSON object: {profile.SecurePreferences}");
            var affectedIds = FindAffectedExtensionIds(root);
            if (affectedIds.Count > 0)
            {
                documents.Add(new ProfileDocument(profile.Directory, profile.SecurePreferences, root, affectedIds));
            }
        }

        if (documents.Count == 0)
        {
            return new ProfileRepairResult(userDataDirectory, profiles.Length, 0, 0, [], []);
        }

        var machineId = GetMachineId();
        var resourcesPath = Path.Combine(
            Path.GetDirectoryName(installation.DllPath)
                ?? throw new InvalidDataException("The chrome.dll directory is invalid."),
            "resources.pak");
        var seed = DiscoverPreferenceHashSeed(resourcesPath, machineId, documents.Select(item => item.Root));

        var changedProfiles = new List<string>();
        var backupFiles = new List<string>();
        var changedExtensionCount = 0;
        foreach (var document in documents)
        {
            var changedIds = RepairDocument(document.Root, seed, machineId, document.AffectedIds);
            if (changedIds.Count == 0)
            {
                continue;
            }

            var backupPath = document.SecurePreferencesPath + ".mv2ctl.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(document.SecurePreferencesPath, backupPath, overwrite: false);
            }

            WriteJsonAtomically(document.SecurePreferencesPath, document.Root);
            UpdateExternalRegistryMacs(Path.GetFileName(document.ProfileDirectory), document.Root, changedIds, machineId);
            changedProfiles.Add(Path.GetFileName(document.ProfileDirectory));
            backupFiles.Add(backupPath);
            changedExtensionCount += changedIds.Count;
        }

        return new ProfileRepairResult(
            userDataDirectory,
            profiles.Length,
            changedProfiles.Count,
            changedExtensionCount,
            changedProfiles,
            backupFiles);
    }

    internal static IReadOnlyList<string> RepairDocument(
        JsonObject root,
        byte[] preferenceSeed,
        string machineId,
        IReadOnlyList<string>? affectedIds = null)
    {
        var settings = root["extensions"]?["settings"] as JsonObject
            ?? throw new InvalidDataException("Secure Preferences has no extensions.settings object.");
        var protection = root["protection"] as JsonObject
            ?? throw new InvalidDataException("Secure Preferences has no protection object.");
        var macs = protection["macs"] as JsonObject
            ?? throw new InvalidDataException("Secure Preferences has no protection.macs object.");
        var macExtensions = macs["extensions"] as JsonObject
            ?? throw new InvalidDataException("Secure Preferences has no protection.macs.extensions object.");
        var settingsMacs = macExtensions["settings"] as JsonObject
            ?? throw new InvalidDataException("Secure Preferences has no protection.macs.extensions.settings object.");

        var candidates = affectedIds ?? FindAffectedExtensionIds(root);
        var changed = new List<string>();
        foreach (var id in candidates)
        {
            if (settings[id] is not JsonObject extension || !RemoveUnsupportedReason(extension))
            {
                continue;
            }

            extension.Remove("state");
            if (extension.ContainsKey("mv2_deprecation_did_disable"))
            {
                extension["mv2_deprecation_did_disable"] = false;
            }

            settingsMacs[id] = ComputeMac(
                preferenceSeed,
                machineId,
                $"extensions.settings.{id}",
                extension);
            changed.Add(id);
        }

        if (changed.Count == 0)
        {
            return changed;
        }

        // An encrypted hash cannot be recreated outside Chrome's app-bound
        // encryption service. Removing this optional parallel store makes
        // Chrome validate the complete legacy HMAC store and regenerate the
        // encrypted hashes itself after a successful startup.
        macExtensions.Remove("settings_encrypted_hash");
        protection.Remove("super_encrypted_hash");
        protection["super_mac"] = ComputeMac(preferenceSeed, machineId, string.Empty, macs);
        return changed;
    }

    internal static string CanonicalizeForChrome(JsonNode node)
    {
        var cleaned = CloneWithoutEmptyContainers(node)
            ?? throw new InvalidDataException("Cannot canonicalize a null JSON value.");
        return cleaned.ToJsonString(CompactJsonOptions)
            .Replace("<", "\\u003C", StringComparison.Ordinal);
    }

    internal static string ComputeMac(byte[] seed, string machineId, string path, JsonNode value)
    {
        var message = Encoding.UTF8.GetBytes(machineId + path + CanonicalizeForChrome(value));
        using var hmac = new HMACSHA256(seed);
        return Convert.ToHexString(hmac.ComputeHash(message));
    }

    private static IReadOnlyList<string> FindAffectedExtensionIds(JsonObject root)
    {
        if (root["extensions"]?["settings"] is not JsonObject settings)
        {
            return [];
        }

        return settings
            .Where(pair => pair.Value is JsonObject extension && HasUnsupportedReason(extension))
            .Select(pair => pair.Key)
            .ToArray();
    }

    private static bool HasUnsupportedReason(JsonObject extension)
    {
        var reasons = extension["disable_reasons"];
        if (reasons is JsonArray array)
        {
            return array.Any(item => item is not null &&
                item.GetValue<int>() == UnsupportedManifestVersionReason);
        }

        return reasons is JsonValue value &&
            (value.GetValue<int>() & UnsupportedManifestVersionReason) != 0;
    }

    private static bool RemoveUnsupportedReason(JsonObject extension)
    {
        var reasons = extension["disable_reasons"];
        if (reasons is JsonArray array)
        {
            var removed = false;
            for (var index = array.Count - 1; index >= 0; index--)
            {
                if (array[index] is not null &&
                    array[index]!.GetValue<int>() == UnsupportedManifestVersionReason)
                {
                    array.RemoveAt(index);
                    removed = true;
                }
            }

            if (removed && array.Count == 0)
            {
                extension.Remove("disable_reasons");
            }

            return removed;
        }

        if (reasons is not JsonValue value)
        {
            return false;
        }

        var oldReasons = value.GetValue<int>();
        var newReasons = oldReasons & ~UnsupportedManifestVersionReason;
        if (oldReasons == newReasons)
        {
            return false;
        }

        if (newReasons == 0)
        {
            extension.Remove("disable_reasons");
        }
        else
        {
            extension["disable_reasons"] = newReasons;
        }

        return true;
    }

    private static JsonNode? CloneWithoutEmptyContainers(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject sourceObject:
                {
                    var result = new JsonObject();
                    foreach (var pair in sourceObject)
                    {
                        var child = CloneWithoutEmptyContainers(pair.Value);
                        if (IsEmptyContainer(child))
                        {
                            continue;
                        }

                        result.Add(pair.Key, child);
                    }

                    return result;
                }
            case JsonArray sourceArray:
                {
                    var result = new JsonArray();
                    foreach (var item in sourceArray)
                    {
                        var child = CloneWithoutEmptyContainers(item);
                        if (IsEmptyContainer(child))
                        {
                            continue;
                        }

                        result.Add(child);
                    }

                    return result;
                }
            default:
                return node.DeepClone();
        }
    }

    private static bool IsEmptyContainer(JsonNode? node) =>
        node is JsonObject objectValue && objectValue.Count == 0 ||
        node is JsonArray arrayValue && arrayValue.Count == 0;

    private static byte[] DiscoverPreferenceHashSeed(
        string resourcesPath,
        string machineId,
        IEnumerable<JsonObject> roots)
    {
        if (!File.Exists(resourcesPath))
        {
            throw new FileNotFoundException("Chrome resources.pak was not found; profile MACs cannot be repaired safely.", resourcesPath);
        }

        var samples = new List<(string Path, JsonNode Value, string ExpectedMac)>();
        foreach (var root in roots)
        {
            if (root["extensions"]?["settings"] is not JsonObject settings ||
                root["protection"]?["macs"]?["extensions"]?["settings"] is not JsonObject settingsMacs)
            {
                continue;
            }

            foreach (var pair in settings)
            {
                if (pair.Value is not null &&
                    settingsMacs[pair.Key] is JsonValue macValue &&
                    macValue.TryGetValue<string>(out var expectedMac))
                {
                    samples.Add(($"extensions.settings.{pair.Key}", pair.Value, expectedMac));
                }
            }
        }

        if (samples.Count == 0)
        {
            throw new InvalidDataException("No protected extension entry was available to verify Chrome's preference hash seed.");
        }

        var pack = File.ReadAllBytes(resourcesPath);
        if (pack.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(pack) != 5)
        {
            throw new InvalidDataException("Unsupported Chrome resources.pak format.");
        }

        var resourceCount = BinaryPrimitives.ReadUInt16LittleEndian(pack.AsSpan(8, 2));
        var candidates = new List<byte[]>();
        for (var index = 0; index < resourceCount; index++)
        {
            var entryOffset = checked(12 + index * 6);
            var nextEntryOffset = checked(entryOffset + 6);
            if (nextEntryOffset + 6 > pack.Length)
            {
                throw new InvalidDataException("resources.pak has a truncated index.");
            }

            var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(pack.AsSpan(entryOffset + 2, 4)));
            var end = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(pack.AsSpan(nextEntryOffset + 2, 4)));
            var length = end - start;
            if (length is <= 0 or > 512 || start < 0 || end > pack.Length)
            {
                continue;
            }

            candidates.Add(pack.AsSpan(start, length).ToArray());
        }

        foreach (var candidate in candidates)
        {
            foreach (var sample in samples.Take(32))
            {
                if (string.Equals(
                        ComputeMac(candidate, machineId, sample.Path, sample.Value),
                        sample.ExpectedMac,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidDataException("Chrome's preference hash seed could not be verified against the current profile; no profile file was changed.");
    }

    private static void UpdateExternalRegistryMacs(
        string profileName,
        JsonObject root,
        IReadOnlyList<string> changedIds,
        string machineId)
    {
        if (root["extensions"]?["settings"] is not JsonObject settings)
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(
            $@"Software\Google\Chrome\PreferenceMACs\{profileName}\extensions.settings",
            writable: true);
        var registrySeed = Encoding.UTF8.GetBytes(RegistryValidationSeed);
        foreach (var id in changedIds)
        {
            if (settings[id] is JsonNode extension)
            {
                key.SetValue(
                    id,
                    ComputeMac(registrySeed, machineId, $"extensions.settings.{id}", extension),
                    RegistryValueKind.String);
            }
        }
    }

    private static void WriteJsonAtomically(string path, JsonObject root)
    {
        var temporaryPath = path + $".mv2ctl.{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, root.ToJsonString(CompactJsonOptions), new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ResolveUserDataDirectory(IReadOnlyList<string> chromeArguments)
    {
        for (var index = 0; index < chromeArguments.Count; index++)
        {
            const string prefix = "--user-data-dir=";
            if (chromeArguments[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(chromeArguments[index][prefix.Length..]);
            }

            if (string.Equals(chromeArguments[index], "--user-data-dir", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < chromeArguments.Count)
            {
                return Path.GetFullPath(chromeArguments[index + 1]);
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google",
            "Chrome",
            "User Data");
    }

    private static string GetMachineId()
    {
        var computerName = new StringBuilder(256);
        var computerNameLength = checked((uint)computerName.Capacity);
        if (!GetComputerNameW(computerName, ref computerNameLength))
        {
            throw new InvalidOperationException("Windows did not return the computer name needed for Chrome profile validation.");
        }

        uint sidLength = 68;
        var sid = new byte[sidLength];
        uint domainLength = 128;
        var domain = new StringBuilder(checked((int)domainLength));
        if (!LookupAccountNameW(null, computerName.ToString(), sid, ref sidLength, domain, ref domainLength, out _))
        {
            var error = Marshal.GetLastWin32Error();
            const int errorInsufficientBuffer = 122;
            if (error != errorInsufficientBuffer)
            {
                throw new System.ComponentModel.Win32Exception(error, "LookupAccountNameW failed while obtaining Chrome's machine ID");
            }

            sid = new byte[sidLength];
            domain = new StringBuilder(checked((int)domainLength));
            if (!LookupAccountNameW(null, computerName.ToString(), sid, ref sidLength, domain, ref domainLength, out _))
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "LookupAccountNameW failed while obtaining Chrome's machine ID");
            }
        }

        if (!ConvertSidToStringSidA(sid, out var sidString))
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "ConvertSidToStringSidA failed while obtaining Chrome's machine ID");
        }

        try
        {
            return Marshal.PtrToStringAnsi(sidString)
                ?? throw new InvalidOperationException("Windows returned an empty Chrome machine ID.");
        }
        finally
        {
            _ = LocalFree(sidString);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComputerNameW(StringBuilder buffer, ref uint size);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupAccountNameW(
        string? systemName,
        string accountName,
        byte[] sid,
        ref uint sidSize,
        StringBuilder referencedDomainName,
        ref uint referencedDomainNameSize,
        out int sidNameUse);

    [DllImport("advapi32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertSidToStringSidA(byte[] sid, out IntPtr stringSid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private sealed record ProfileDocument(
        string ProfileDirectory,
        string SecurePreferencesPath,
        JsonObject Root,
        IReadOnlyList<string> AffectedIds);
}
