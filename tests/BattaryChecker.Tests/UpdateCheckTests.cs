using System.Text;
using System.Text.Json.Nodes;

namespace BattaryChecker.Tests;

public sealed class UpdateCheckTests
{
    const string Hash = "c81b562c2c4ab6a5d3a23c4e57b0c08ea668e735277b24da811bd340c5f74e54";
    const string Download = "https://github.com/Raptar212/Battery-Checker/releases/download/";

    /// <summary>The shape of GitHub's "latest release" answer, cut to the fields the check reads.</summary>
    static JsonObject Release(string tag = "v1.2.0") => new()
    {
        ["tag_name"] = tag,
        ["draft"] = false,
        ["prerelease"] = false,
        ["assets"] = new JsonArray(
            new JsonObject { ["name"] = "notes.txt", ["browser_download_url"] = Download + tag + "/notes.txt" },
            new JsonObject
            {
                ["name"] = $"BatteryChecker-Setup-{tag[1..]}.exe",
                ["browser_download_url"] = $"{Download}{tag}/BatteryChecker-Setup-{tag[1..]}.exe",
                ["digest"] = "sha256:" + Hash.ToUpperInvariant(),
            }),
    };

    static JsonObject Installer(JsonObject release) => (JsonObject)release["assets"]![1]!;

    [Fact]
    public void Parse_TakesVersionInstallerAndHash()
    {
        var r = UpdateCheck.Parse(Release().ToJsonString())!;
        Assert.Equal(new Version(1, 2, 0), r.Version);
        Assert.Equal(Download + "v1.2.0/BatteryChecker-Setup-1.2.0.exe", r.Installer.ToString());
        Assert.Equal(Hash, r.Sha256);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    public void Parse_SkipsDraftsAndPreReleases(string flag)
    {
        var json = Release();
        json[flag] = true;
        Assert.Null(UpdateCheck.Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData("1.2.0")]
    [InlineData("v1.2")]
    [InlineData("v1.2.0.1")]
    [InlineData("v1.2.0-beta")]
    [InlineData("v+1.2.0")]
    public void Parse_SkipsOtherTags(string tag) => Assert.Null(UpdateCheck.Parse(Release(tag).ToJsonString()));

    [Fact]
    public void Parse_SkipsReleaseWithoutInstaller()
    {
        var json = Release();
        Installer(json)["name"] = "BattaryChecker.exe";
        Assert.Null(UpdateCheck.Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData("https://example.com/BatteryChecker-Setup-1.2.0.exe")]
    [InlineData("http://github.com/Raptar212/Battery-Checker/releases/download/v1.2.0/BatteryChecker-Setup-1.2.0.exe")]
    [InlineData("https://github.com/someone/Battery-Checker/releases/download/v1.2.0/BatteryChecker-Setup-1.2.0.exe")]
    [InlineData("https://github.com/Raptar212/Battary-Checker/releases/download/v1.2.0/BatteryChecker-Setup-1.2.0.exe")]
    public void Parse_RefusesInstallerFromElsewhere(string url)
    {
        var json = Release();
        Installer(json)["browser_download_url"] = url;
        Assert.Null(UpdateCheck.Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("md5:0123456789abcdef0123456789abcdef")]
    [InlineData("sha256:1234")]
    [InlineData("sha256:zz1b562c2c4ab6a5d3a23c4e57b0c08ea668e735277b24da811bd340c5f74e54")]
    public void Parse_RefusesInstallerWithoutHash(string? digest)
    {
        var json = Release();
        Installer(json)["digest"] = digest;
        Assert.Null(UpdateCheck.Parse(json.ToJsonString()));
    }

    [Fact]
    public void TagVersion_KnownEvenWithoutInstaller()
    {
        var json = Release();
        Installer(json)["digest"] = null;
        Assert.Null(UpdateCheck.Parse(json.ToJsonString()));
        Assert.Equal(new Version(1, 2, 0), UpdateCheck.TagVersion(json.ToJsonString()));

        json["prerelease"] = true;
        Assert.Null(UpdateCheck.TagVersion(json.ToJsonString()));
        Assert.Null(UpdateCheck.TagVersion("not json"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"message\":\"Not Found\"}")]
    [InlineData("{\"tag_name\":\"v1.2.0\",\"assets\":{}}")]
    public void Parse_GarbageIsNull(string json) => Assert.Null(UpdateCheck.Parse(json));

    [Theory]
    [InlineData("1.2.0", "1.1.0.0", true)]
    [InlineData("1.1.1", "1.1.0.0", true)]
    [InlineData("2.0.0", "1.10.5.0", true)]
    [InlineData("1.1.0", "1.1.0.0", false)] // the assembly's fourth number is not a newer version
    [InlineData("1.0.9", "1.1.0.0", false)]
    [InlineData("1.10.0", "1.9.0.0", true)] // numbers, not text
    public void IsNewer_ComparesThreeNumbers(string latest, string current, bool newer) =>
        Assert.Equal(newer, UpdateCheck.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Fact]
    public void HashMatches_ChecksSha256()
    {
        using var file = new MemoryStream(Encoding.ASCII.GetBytes("abc"));
        const string abc = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        Assert.True(UpdateCheck.HashMatches(file, abc));
        file.Position = 0;
        Assert.False(UpdateCheck.HashMatches(file, Hash));
    }

    [Fact]
    public void Config_ChecksUpdatesByDefault_KeepsUserChoice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bc-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{}"); // a file from a version before the setting
            Assert.True(AppConfig.Load(path).CheckUpdates);

            File.WriteAllText(path, "{\"CheckUpdates\":false}");
            Assert.False(AppConfig.Load(path).CheckUpdates);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
