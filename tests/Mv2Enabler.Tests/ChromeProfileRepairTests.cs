using System.Text.Json.Nodes;

namespace Mv2Enabler.Tests;

public sealed class ChromeProfileRepairTests
{
    [Fact]
    public void CanonicalizationMatchesChromiumEmptyContainerAndLessThanRules()
    {
        var value = JsonNode.Parse(
            """{"empty":{},"list":[],"host":"<all_urls>","nested":{"drop":{},"keep":2},"arr":[{},[],3]}""")!;

        var canonical = ChromeProfileRepair.CanonicalizeForChrome(value);
        var seed = Enumerable.Range(0, 32).Select(value => checked((byte)value)).ToArray();
        var mac = ChromeProfileRepair.ComputeMac(
            seed,
            "S-1-5-21-1-2-3",
            "extensions.settings.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            value);

        Assert.Equal(
            """{"host":"\u003Call_urls>","nested":{"keep":2},"arr":[3]}""",
            canonical);
        Assert.Equal("5FA83B1CA2127430CEC796DC12EF9D6042FDAEE46EB7212B4FF3F979A8EC8A83", mac);
    }

    [Fact]
    public void RepairRemovesOnlyMv2ReasonAndRestampsProtectionStore()
    {
        const string id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var root = JsonNode.Parse(
            $$"""
            {
              "extensions": {
                "settings": {
                  "{{id}}": {
                    "disable_reasons": [8388608, 2],
                    "mv2_deprecation_did_disable": true,
                    "state": 0,
                    "path": "C:\\extension"
                  }
                }
              },
              "protection": {
                "macs": {
                  "extensions": {
                    "settings": { "{{id}}": "OLD" },
                    "settings_encrypted_hash": { "{{id}}": "OLD-ENCRYPTED" }
                  }
                },
                "super_mac": "OLD-SUPER",
                "super_encrypted_hash": "OLD-SUPER-ENCRYPTED"
              }
            }
            """)!.AsObject();
        var seed = Enumerable.Range(0, 32).Select(value => checked((byte)value)).ToArray();

        var changed = ChromeProfileRepair.RepairDocument(root, seed, "S-1-5-21-1-2-3");

        Assert.Equal([id], changed);
        var extension = root["extensions"]!["settings"]![id]!.AsObject();
        Assert.Equal(2, extension["disable_reasons"]![0]!.GetValue<int>());
        Assert.False(extension["mv2_deprecation_did_disable"]!.GetValue<bool>());
        Assert.False(extension.ContainsKey("state"));
        Assert.Null(root["protection"]!["macs"]!["extensions"]!["settings_encrypted_hash"]);
        Assert.Null(root["protection"]!["super_encrypted_hash"]);
        Assert.Matches("^[0-9A-F]{64}$", root["protection"]!["macs"]!["extensions"]!["settings"]![id]!.GetValue<string>());
        Assert.Matches("^[0-9A-F]{64}$", root["protection"]!["super_mac"]!.GetValue<string>());
    }
}
