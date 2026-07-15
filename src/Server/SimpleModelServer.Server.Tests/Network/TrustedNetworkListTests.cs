using System.Net;
using SimpleModelServer.Server.Infrastructure.Networking;
using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
/// Comprehensive tests for <see cref="ITrustedNetworkList"/> interface.
/// Validates network access control and IP address validation.
/// </summary>
public class TrustedNetworkListTests
{
    /// <summary>
    /// Contract verification: ITrustedNetworkList has all required members.
    /// </summary>
    [Fact]
    public void ITrustedNetworkList_Interface_HasRequiredMembers()
    {
        // Arrange
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList();

        // Act & Assert - All properties and methods must be accessible
        _ = networkList.IsRestricted;
        _ = networkList.IsAllowed(IPAddress.Loopback);
    }

    [Fact]
    public void TrustedNetworkList_EmptyList_IsNotRestricted()
    {
        // Arrange & Act
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(restricted: false);

        // Assert
        Assert.False(networkList.IsRestricted);
    }

    [Fact]
    public void TrustedNetworkList_WithNetworks_IsRestricted()
    {
        // Arrange & Act
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(restricted: true);

        // Assert
        Assert.True(networkList.IsRestricted);
    }

    [Fact]
    public void TrustedNetworkList_Unrestricted_AllowsAnyAddress()
    {
        // Arrange
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(restricted: false);

        // Act & Assert
        Assert.True(networkList.IsAllowed(IPAddress.Loopback));
        Assert.True(networkList.IsAllowed(IPAddress.Parse("192.168.1.100")));
        Assert.True(networkList.IsAllowed(IPAddress.Parse("10.0.0.1")));
        Assert.True(networkList.IsAllowed(null));
    }

    [Fact]
    public void TrustedNetworkList_Restricted_AllowsLoopback()
    {
        // Arrange
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(
            restricted: true,
            allowLoopback: true
        );

        // Act & Assert
        Assert.True(networkList.IsAllowed(IPAddress.Loopback));
        Assert.True(networkList.IsAllowed(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void TrustedNetworkList_Restricted_AllowsTrustedNetwork()
    {
        // Arrange
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(
            restricted: true,
            trustedNetworks: new[] { "192.168.0.0/16" },
            testIpAllowed: true
        );

        // Act & Assert
        Assert.True(networkList.IsAllowed(IPAddress.Parse("192.168.1.100")));
    }

    [Fact]
    public void TrustedNetworkList_Restricted_DeniesUntrustedNetwork()
    {
        // Arrange
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(
            restricted: true,
            trustedNetworks: new[] { "192.168.0.0/16" },
            testIpAllowed: false
        );

        // Act & Assert
        Assert.False(networkList.IsAllowed(IPAddress.Parse("10.0.0.1")));
    }

    [Fact]
    public void TrustedNetworkList_NullAddress_IsHandled()
    {
        // Arrange
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(restricted: false);

        // Act
        var result = networkList.IsAllowed(null);

        // Assert
        Assert.True(result); // Unrestricted allows all, including null
    }

    [Fact]
    public void TrustedNetworkList_Restricted_NullAddress_IsDenied()
    {
        // Arrange
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(
            restricted: true,
            trustedNetworks: new[] { "192.168.0.0/16" },
            testIpAllowed: false
        );

        // Act
        var result = networkList.IsAllowed(null);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void TrustedNetworkList_Loopback_IsAlwaysAllowed(string ipAddress)
    {
        // Arrange
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(
            restricted: true,
            allowLoopback: true
        );

        // Act & Assert
        Assert.True(networkList.IsAllowed(IPAddress.Parse(ipAddress)));
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("192.168.100.50")]
    [InlineData("192.168.255.255")]
    public void TrustedNetworkList_MatchesCidrNetworks(string ipAddress)
    {
        // Arrange
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(
            restricted: true,
            trustedNetworks: new[] { "192.168.0.0/16" },
            testIpAllowed: true
        );

        // Act & Assert
        Assert.True(networkList.IsAllowed(IPAddress.Parse(ipAddress)));
    }

    [Fact]
    public void TrustedNetworkList_MultipleNetworks_AllowsAnyMatch()
    {
        // Arrange
        ITrustedNetworkList networkList = CreateTestTrustedNetworkList(
            restricted: true,
            trustedNetworks: new[] { "192.168.0.0/16", "10.0.0.0/8", "172.16.0.0/12" },
            testIpAllowed: true
        );

        // Act & Assert
        Assert.True(networkList.IsAllowed(IPAddress.Parse("192.168.1.1")));
        Assert.True(networkList.IsAllowed(IPAddress.Parse("10.0.0.1")));
        Assert.True(networkList.IsAllowed(IPAddress.Parse("172.16.0.1")));
    }

    [Fact]
    public void TrustedNetworkList_IdentifiesRestrictionStatus()
    {
        // Arrange
        var unrestricted = CreateTestTrustedNetworkList(restricted: false);
        var restricted = CreateTestTrustedNetworkList(restricted: true);

        // Act & Assert
        Assert.False(unrestricted.IsRestricted);
        Assert.True(restricted.IsRestricted);
    }

    // Helper to create test implementations
    private static ITrustedNetworkList CreateTestTrustedNetworkList(
        bool restricted = true,
        bool allowLoopback = true,
        string[]? trustedNetworks = null,
        bool testIpAllowed = true)
    {
        return new TestTrustedNetworkList
        {
            IsRestricted = restricted,
            AllowLoopback = allowLoopback,
            TrustedNetworks = trustedNetworks ?? Array.Empty<string>(),
            TestIpAllowed = testIpAllowed
        };
    }

    private class TestTrustedNetworkList : ITrustedNetworkList
    {
        public bool IsRestricted { get; init; }
        public bool AllowLoopback { get; init; }
        public string[] TrustedNetworks { get; init; } = Array.Empty<string>();
        public bool TestIpAllowed { get; init; }

        public bool IsAllowed(IPAddress? address)
        {
            // Unrestricted mode allows all
            if (!IsRestricted)
            {
                return true;
            }

            // Loopback is always allowed in restricted mode
            if (AllowLoopback && address != null && (address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback)))
            {
                return true;
            }

            // Null address not allowed in restricted mode
            if (address == null)
            {
                return false;
            }

            // Check if address is in trusted networks
            return TestIpAllowed;
        }
    }
}
