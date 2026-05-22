using Module.Communication.Models;
using Shared.Abstractions.Enum;
using Shared.Models.Communication;

namespace TestProject;

[TestFixture]
public sealed class DeviceCommunicationProfileTests
{
    [Test]
    public void ResetToCurrentTypeDefaults_WhenPlcTypeIsS7_UsesStandardS7Defaults()
    {
        DeviceCommunicationProfile profile = new()
        {
            Type = CommuniactionType.PLC,
            PLCType = PlcCommunicationTypeNames.S7,
            RemotePort = "502",
            PLCS7CpuType = string.Empty,
            PLCS7Rack = string.Empty,
            PLCS7Slot = string.Empty
        };

        profile.ResetToCurrentTypeDefaults();

        Assert.Multiple(() =>
        {
            Assert.That(profile.RemotePort, Is.EqualTo("102"));
            Assert.That(profile.PLCS7CpuType, Is.EqualTo(S7CpuTypeNames.S71200));
            Assert.That(profile.PLCS7Rack, Is.EqualTo("0"));
            Assert.That(profile.PLCS7Slot, Is.EqualTo("1"));
        });
    }

    [Test]
    public void TryBuildRuntimeConfig_WhenPlcTypeIsS7_BuildsS7RuntimeConfig()
    {
        DeviceCommunicationProfile profile = new()
        {
            LocalName = "S7 Device",
            Type = CommuniactionType.PLC,
            PLCType = PlcCommunicationTypeNames.S7,
            RemoteIPAddress = "192.168.0.10",
            RemotePort = "102",
            PLCS7CpuType = S7CpuTypeNames.S71500,
            PLCS7Rack = "0",
            PLCS7Slot = "1"
        };

        bool succeeded = profile.TryBuildRuntimeConfig(out CommuniactionConfigModel? config, out string validationMessage);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True, validationMessage);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Type, Is.EqualTo(CommuniactionType.PLC));
            Assert.That(config.PLCType, Is.EqualTo(PlcCommunicationTypeNames.S7));
            Assert.That(config.RemoteIPAddress, Is.EqualTo("192.168.0.10"));
            Assert.That(config.RemotePort, Is.EqualTo(102));
            Assert.That(config.S7CpuType, Is.EqualTo(S7CpuTypeNames.S71500));
            Assert.That(config.S7Rack, Is.EqualTo(0));
            Assert.That(config.S7Slot, Is.EqualTo(1));
        });
    }

    [Test]
    public void TryBuildRuntimeConfig_WhenS7PortIsNot102_ReturnsValidationError()
    {
        DeviceCommunicationProfile profile = new()
        {
            LocalName = "S7 Device",
            Type = CommuniactionType.PLC,
            PLCType = PlcCommunicationTypeNames.S7,
            RemoteIPAddress = "192.168.0.10",
            RemotePort = "502",
            PLCS7CpuType = S7CpuTypeNames.S71200,
            PLCS7Rack = "0",
            PLCS7Slot = "1"
        };

        bool succeeded = profile.TryBuildRuntimeConfig(out _, out string validationMessage);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(validationMessage, Is.EqualTo("PLC S7 远端端口必须为 102。"));
        });
    }
}
