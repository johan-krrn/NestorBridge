using NestorBridge.Configuration;

namespace NestorBridge.Tests;

public class BridgeOptionsTests
{
  [Fact]
  public void Validate_MissingBoxId_Throws()
  {
    var opts = new BridgeOptions
    {
      MqttHost = "broker.example.com",
      BoxId = "",
      MqttClientId = "test"
    };

    Assert.Throws<InvalidOperationException>(() => opts.Validate());
  }

  [Fact]
  public void Validate_NoTransportConfigured_Throws()
  {
    var opts = new BridgeOptions
    {
      MqttHost = "",
      BoxId = "box-1",
      MqttClientId = "",
      SignalrHubUrl = ""
    };

    Assert.Throws<InvalidOperationException>(() => opts.Validate());
  }

  [Fact]
  public void Validate_MqttEnabled_MissingClientId_Throws()
  {
    var opts = new BridgeOptions
    {
      MqttHost = "broker.example.com",
      BoxId = "box-1",
      MqttClientId = ""
    };

    Assert.Throws<InvalidOperationException>(() => opts.Validate());
  }

  [Fact]
  public void Validate_MqttOnly_AllSet_DoesNotThrow()
  {
    var opts = new BridgeOptions
    {
      MqttHost = "broker.example.com",
      BoxId = "box-1",
      MqttClientId = "box-1"
    };

    opts.Validate(); // Should not throw
  }

  [Fact]
  public void Validate_SignalROnly_DoesNotThrow()
  {
    var opts = new BridgeOptions
    {
      MqttHost = "",
      BoxId = "box-1",
      MqttClientId = "",
      SignalrHubUrl = "https://example.com/hub/devices",
      SignalrApiKey = "my-key"
    };

    opts.Validate(); // Should not throw
  }

  [Fact]
  public void Validate_BothTransports_DoesNotThrow()
  {
    var opts = new BridgeOptions
    {
      MqttHost = "broker.example.com",
      BoxId = "box-1",
      MqttClientId = "box-1",
      SignalrHubUrl = "https://example.com/hub/devices",
      SignalrApiKey = "my-key"
    };

    opts.Validate(); // Should not throw
  }

  [Fact]
  public void IsMqttEnabled_ReturnsTrueWhenHostSet()
  {
    var opts = new BridgeOptions { MqttHost = "broker.example.com" };
    Assert.True(opts.IsMqttEnabled);
  }

  [Fact]
  public void IsMqttEnabled_ReturnsFalseWhenHostEmpty()
  {
    var opts = new BridgeOptions { MqttHost = "" };
    Assert.False(opts.IsMqttEnabled);
  }

  [Fact]
  public void IsSignalREnabled_ReturnsTrueWhenUrlSet()
  {
    var opts = new BridgeOptions { SignalrHubUrl = "https://example.com/hub" };
    Assert.True(opts.IsSignalREnabled);
  }

  [Fact]
  public void IsSignalREnabled_ReturnsFalseWhenUrlEmpty()
  {
    var opts = new BridgeOptions { SignalrHubUrl = "" };
    Assert.False(opts.IsSignalREnabled);
  }
}
