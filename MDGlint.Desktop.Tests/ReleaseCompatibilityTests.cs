using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MDGlint.Core.Models;
using MDGlint.Core.Services;
using MDGlint.Desktop.Services;

namespace MDGlint.Desktop.Tests;

/// <summary>
/// Updates only work while the installer, the release workflow and the app agree on a few names
/// and formats (docs/releasing.md, "Keeping updates working"). These tests read the real files, so
/// changing one side without the other fails here instead of breaking updates for users.
/// </summary>
public sealed class ReleaseCompatibilityTests
{
    // Released copies find their installed MDGlint by this; it can never change.
    private const string AppId = "{8650A31D-C8E8-48EC-9A9E-F38B9825ED9E}";

    private static readonly string Installer = RepositoryFile("MDGlint.Desktop", "Install", "installer.iss");
    private static readonly string ReleaseWorkflow = RepositoryFile(".github", "workflows", "release.yml");

    [Fact]
    public void Installer_KeepsTheAppId()
    {
        // Inno Setup writes a literal "{" as "{{".
        Assert.Contains($"AppId={{{AppId}", Installer.Split('\n').Select(l => l.Trim()));
    }

    [Fact]
    public void Installer_IsNamedAsTheAppLooksForIt()
    {
        Assert.Contains($"OutputBaseFilename={Path.GetFileNameWithoutExtension(UpdateSource.InstallerName)}", Lines(Installer));
    }

    [Fact]
    public void ReleaseWorkflow_UploadsTheInstallerAndItsHash()
    {
        Assert.Contains($"\"$output/{UpdateSource.InstallerName}\"", ReleaseWorkflow);
        Assert.Contains($"\"$output/{UpdateSource.HashName}\"", ReleaseWorkflow);
        Assert.Equal(UpdateSource.InstallerName + ".sha256", UpdateSource.HashName);
    }

    [Fact]
    public void ReleaseWorkflow_WritesAHashFileTheAppReads()
    {
        // The workflow line that writes the file, with a real hash in place of $hash.
        var line = Regex.Match(ReleaseWorkflow, @"""\$hash(?<rest>[^""]*)"" \| Set-Content -Path ""\$output/(?<name>[^""]+)"" -Encoding ascii");
        Assert.True(line.Success, "release.yml no longer writes the hash file as expected.");
        Assert.Equal(UpdateSource.HashName, line.Groups["name"].Value);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("setup"));
        var written = Convert.ToHexString(hash).ToLowerInvariant() + line.Groups["rest"].Value;

        Assert.Equal(hash, UpdateService.ParseHashFile(written));
        Assert.Contains("ToLowerInvariant()", ReleaseWorkflow);
    }

    [Fact]
    public void ReleaseWorkflow_TagsReleasesAsTheAppParsesThem()
    {
        Assert.Contains("tags: ['v*']", ReleaseWorkflow);
        Assert.Contains("\"v$version\"", ReleaseWorkflow);
        Assert.True(SemanticVersion.TryParse("v1.0.0", out _));
        // Pre-releases are marked as such and never offered (UpdateService.ParseRelease).
        Assert.Contains("'--prerelease'", ReleaseWorkflow);
    }

    [Fact]
    public void ReleaseWorkflow_WaitsForApprovalAndAttestsTheInstaller()
    {
        // A pushed tag builds nothing until the maintainer approves the run in the `release`
        // environment, and the installer users download gets a signed build-provenance attestation.
        Assert.Contains("environment: release", Lines(ReleaseWorkflow));
        Assert.Contains("id-token: write", ReleaseWorkflow);
        Assert.Contains("attestations: write", ReleaseWorkflow);
        Assert.Contains(Lines(ReleaseWorkflow), l => l.StartsWith("uses: actions/attest-build-provenance@", StringComparison.Ordinal));
        Assert.Contains($"subject-path: MDGlint.Desktop/Install/Output/{UpdateSource.InstallerName}", Lines(ReleaseWorkflow));
    }

    [Fact]
    public void Installer_ReadsTheUpdateArgumentTheAppPasses()
    {
        Assert.Contains("/UPDATE=1", UpdateLauncher.InstallerArguments);
        Assert.Contains("ExpandConstant('{param:UPDATE|0}') = '1'", Installer);
        Assert.Contains("Check: IsAppUpdate", Installer);
    }

    [Fact]
    public void Installer_WritesTheSetupChoiceWhereTheAppReadsIt()
    {
        Assert.Equal(@"Software\MDGlint", UpdateLauncher.SetupKey);
        var choices = Lines(Installer).Where(l => l.Contains("ValueName: \"CheckForUpdates\"")).ToList();

        Assert.Equal(2, choices.Count);
        Assert.All(choices, l => Assert.Contains("Root: HKA; Subkey: \"Software\\{#MyAppName}\"; ValueType: dword;", l));
        Assert.Contains(choices, l => l.Contains("ValueData: \"1\"") && l.EndsWith("Tasks: check_updates"));
        Assert.Contains(choices, l => l.Contains("ValueData: \"0\"") && l.EndsWith("Tasks: not check_updates"));
        Assert.Contains("#define MyAppName \"MDGlint\"", Installer);
    }

    [Fact]
    public void Installer_RefusesWindowsTheAppCannotRunOn()
    {
        // .NET 10 and WebView2 need Windows 10 version 1607 (build 14393) or later.
        Assert.Contains("MinVersion=10.0.14393", Lines(Installer));
        Assert.Single(Lines(Installer), l => l.StartsWith("MinVersion="));
    }

    [Fact]
    public void Installer_KeepsTheTaskNamesEarlierVersionsUsed()
    {
        // Setup remembers ticked tasks by name between versions.
        Assert.Contains(Lines(Installer), l => l.StartsWith("Name: \"associate_md\";"));
        Assert.Contains(Lines(Installer), l => l.StartsWith("Name: \"check_updates\";"));
    }

    private static IEnumerable<string> Lines(string text) => text.Split('\n').Select(l => l.Trim());

    internal static string RepositoryFile(params string[] parts)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "MDGlint.slnx")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);
        return File.ReadAllText(Path.Combine([folder.FullName, .. parts])).Replace("\r\n", "\n");
    }
}
