using System.Reflection;

namespace NhatVuong.Application.Tests;

/// <summary>Hexagonal dependency rules from the architecture spine, enforced by test.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Domain.Entities.Device).Assembly;
    private static readonly Assembly Application = typeof(Application.Commands.CommandService).Assembly;

    [Fact(DisplayName = "Spine: Domain depends on nothing but the base class library")]
    public void DomainHasNoDependencies()
    {
        var references = Domain.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.All(references, name => Assert.True(IsBcl(name), $"Domain references {name}"));
    }

    [Fact(DisplayName = "Spine: Application depends only on Domain (plus BCL, logging/DI abstractions and BCrypt)")]
    public void ApplicationDependsOnlyOnDomain()
    {
        var references = Application.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.Contains("NhatVuong.Domain", references);
        var disallowed = references.Where(name =>
                !IsBcl(name)
                && name != "NhatVuong.Domain"
                && name != "BCrypt-Net-Next"
                && !name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))
            .ToList();
        Assert.True(disallowed.Count == 0, "Application references: " + string.Join(", ", disallowed));
        Assert.DoesNotContain(references, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, n => n.StartsWith("MQTTnet", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "AD-1: only CommandService talks to the device port for commands")]
    public void OnlyCommandServiceSendsCommands()
    {
        var holders = Application.GetTypes()
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(Abstractions.IDevicePort))))
            .Select(t => t.Name)
            .Order()
            .ToList();
        Assert.Equal(["CommandService", "DeviceConfigService"], holders);
    }

    [Theory(DisplayName = "Wire contract: Contracts enums mirror Domain enums name-for-name and value-for-value")]
    [InlineData("UserRole")]
    [InlineData("PowerState")]
    [InlineData("AcMode")]
    [InlineData("FanSpeed")]
    [InlineData("Connectivity")]
    [InlineData("CommandAction")]
    [InlineData("CommandSource")]
    [InlineData("CommandResult")]
    [InlineData("RejectionReason")]
    [InlineData("GrantSource")]
    [InlineData("IncidentKind")]
    [InlineData("IncidentStatus")]
    [InlineData("PreCoolStatus")]
    public void EnumsMatch(string name)
    {
        var domain = Domain.GetType($"NhatVuong.Domain.{name}", throwOnError: true)!;
        var wire = typeof(Contracts.NvcJson).Assembly.GetType($"NhatVuong.Contracts.{name}", throwOnError: true)!;
        var domainPairs = Enum.GetNames(domain).Select(n => (n, Convert.ToInt32(Enum.Parse(domain, n))));
        var wirePairs = Enum.GetNames(wire).Select(n => (n, Convert.ToInt32(Enum.Parse(wire, n))));
        Assert.Equal(domainPairs, wirePairs);
    }

    [Fact(DisplayName = "NFR-07: server-generated text exists in Vietnamese and English resources with the same keys")]
    public void ResourcesComplete()
    {
        var manager = new System.Resources.ResourceManager("NhatVuong.Application.Resources.Messages", Application);
        var vi = manager.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, true)!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        var en = manager.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo("en"), true, false)!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        Assert.NotEmpty(vi);
        Assert.Equal(vi.Order(), en.Order());
        Text.Culture = System.Globalization.CultureInfo.GetCultureInfo("vi");
        Assert.Equal("Thiết bị đang mất kết nối.", Text.Get("Command_DeviceOffline"));
    }

    private static bool IsBcl(string name) =>
        name is "System.Runtime" or "netstandard" or "mscorlib" or "System.Private.CoreLib"
        || name.StartsWith("System.", StringComparison.Ordinal);
}
