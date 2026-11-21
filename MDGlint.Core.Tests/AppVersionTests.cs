using System.Reflection;
using System.Xml.Linq;
using MDGlint.Core.Models;

namespace MDGlint.Core.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.2.3+3f2a1b9c0d", "1.2.3")]
    [InlineData("1.0.0-beta.1+abc", "1.0.0-beta.1")]
    [InlineData(" 2.0.0 ", "2.0.0")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void ToDisplay_DropsBuildMetadata(string? informationalVersion, string expected)
    {
        Assert.Equal(expected, AppVersion.ToDisplay(informationalVersion));
    }

    [Fact]
    public void BuiltAssemblies_CarryTheVersionFromDirectoryBuildProps()
    {
        var props = FindRepositoryFile("Directory.Build.props");
        var expected = XDocument.Load(props).Descendants("Version").Single().Value;

        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.Equal(expected, AppVersion.ToDisplay(informational));
    }

    private static string FindRepositoryFile(string name)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{name} not found above the test output folder.");
    }
}
