using System.Net;
using System.Net.Sockets;
using Pointage.Settings;

namespace Pointage.Tests;

public class NetworkPortTests
{
    [Fact]
    public void Port_DefaultIs5080() =>
        Assert.Equal(5080, new SettingsStore(TestDb.Create(out _)).Get().Port);

    [Fact]
    public void Port_SaveThenGet()
    {
        var store = new SettingsStore(TestDb.Create(out _));
        store.Save(AppSettings.Default with { Port = 8081 });
        Assert.Equal(8081, store.Get().Port);
    }

    [Theory]
    [InlineData(1023, false)]
    [InlineData(1024, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void IsValid_Bounds(int port, bool expected) => Assert.Equal(expected, NetworkPort.IsValid(port));

    [Fact]
    public void IsFree_OccupiedPort_IsFalse()
    {
        var l = new TcpListener(IPAddress.Any, 0);
        l.Start();
        try { Assert.False(NetworkPort.IsFree(((IPEndPoint)l.LocalEndpoint).Port)); }
        finally { l.Stop(); }
    }

    [Fact]
    public void SetPortCommand_Valid_SavesAndReturns0()
    {
        var db = TestDb.Create(out _);
        Assert.Equal(0, NetworkPort.SetPortCommand(db, "8081"));
        Assert.Equal(8081, new SettingsStore(db).Get().Port);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("80")]
    public void SetPortCommand_Invalid_Returns1AndKeepsPort(string arg)
    {
        var db = TestDb.Create(out _);
        Assert.Equal(1, NetworkPort.SetPortCommand(db, arg));
        Assert.Equal(5080, new SettingsStore(db).Get().Port);
    }
}
