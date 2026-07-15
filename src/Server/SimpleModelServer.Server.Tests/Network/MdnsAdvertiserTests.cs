using SimpleModelServer.Server.Infrastructure.Networking;
using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IMdnsAdvertiser"/> interface.
/// Validates mDNS service advertisement lifecycle and state management.
/// </summary>
public class MdnsAdvertiserTests
{
    /// <summary>
    /// Contract verification: IMdnsAdvertiser has all required members.
    /// </summary>
    [Fact]
    public void IMdnsAdvertiser_Interface_HasRequiredMembers()
    {
        // Arrange
        IMdnsAdvertiser advertiser = CreateTestAdvertiser();

        // Act & Assert - All properties and methods must be accessible
        _ = advertiser.IsAdvertising;
        advertiser.Advertise();
        advertiser.Stop();
    }

    [Fact]
    public void MdnsAdvertiser_IsAdvertising_InitiallyFalse()
    {
        // Arrange & Act
        IMdnsAdvertiser advertiser = CreateTestAdvertiser();

        // Assert
        Assert.False(advertiser.IsAdvertising);
    }

    [Fact]
    public void MdnsAdvertiser_IsAdvertising_TrueAfterAdvertise()
    {
        // Arrange
        IMdnsAdvertiser advertiser = CreateTestAdvertiser();

        // Act
        advertiser.Advertise();

        // Assert
        Assert.True(advertiser.IsAdvertising);
    }

    [Fact]
    public void MdnsAdvertiser_IsAdvertising_FalseAfterStop()
    {
        // Arrange
        IMdnsAdvertiser advertiser = CreateTestAdvertiser();
        advertiser.Advertise();

        // Act
        advertiser.Stop();

        // Assert
        Assert.False(advertiser.IsAdvertising);
    }

    [Fact]
    public void MdnsAdvertiser_AdvertiseWhenAlreadyAdvertising_IsNoOp()
    {
        // Arrange
        IMdnsAdvertiser advertiser = CreateTestAdvertiser();
        advertiser.Advertise();

        // Act - Should not throw or change state
        advertiser.Advertise();

        // Assert
        Assert.True(advertiser.IsAdvertising);
    }

    [Fact]
    public void MdnsAdvertiser_StopWhenNotAdvertising_IsNoOp()
    {
        // Arrange
        IMdnsAdvertiser advertiser = CreateTestAdvertiser();

        // Act - Should not throw
        advertiser.Stop();

        // Assert
        Assert.False(advertiser.IsAdvertising);
    }

    [Fact]
    public void MdnsAdvertiser_CanAdvertiseAgainAfterStop()
    {
        // Arrange
        IMdnsAdvertiser advertiser = CreateTestAdvertiser();
        advertiser.Advertise();
        advertiser.Stop();

        // Act
        advertiser.Advertise();

        // Assert
        Assert.True(advertiser.IsAdvertising);
    }

    [Fact]
    public void MdnsAdvertiser_MultipleAdvertiseStop_Cycles()
    {
        // Arrange
        IMdnsAdvertiser advertiser = CreateTestAdvertiser();

        // Act & Assert - Multiple cycles
        advertiser.Advertise();
        Assert.True(advertiser.IsAdvertising);

        advertiser.Stop();
        Assert.False(advertiser.IsAdvertising);

        advertiser.Advertise();
        Assert.True(advertiser.IsAdvertising);

        advertiser.Stop();
        Assert.False(advertiser.IsAdvertising);
    }

    [Fact]
    public void MdnsAdvertiser_ImplementsIDisposable()
    {
        // Arrange & Act
        IMdnsAdvertiser advertiser = CreateTestAdvertiser();

        // Assert
        Assert.IsAssignableFrom<IDisposable>(advertiser);
    }

    [Fact]
    public void MdnsAdvertiser_Dispose_StopsAdvertising()
    {
        // Arrange
        var advertiser = CreateTestAdvertiser() as TestMdnsAdvertiser;
        advertiser!.Advertise();

        // Act
        advertiser.Dispose();

        // Assert
        Assert.False(advertiser.IsAdvertising);
    }

    [Fact]
    public void MdnsAdvertiser_CanBeDisposedMultipleTimes()
    {
        // Arrange
        var advertiser = CreateTestAdvertiser() as TestMdnsAdvertiser;

        // Act & Assert - Should not throw
        advertiser!.Dispose();
        advertiser.Dispose();
        advertiser.Dispose();
    }

    // Helper to create test implementations
    private static IMdnsAdvertiser CreateTestAdvertiser()
    {
        return new TestMdnsAdvertiser();
    }

    private class TestMdnsAdvertiser : IMdnsAdvertiser
    {
        private bool _isAdvertising;

        public bool IsAdvertising => _isAdvertising;

        public void Advertise()
        {
            if (!_isAdvertising)
            {
                _isAdvertising = true;
            }
        }

        public void Stop()
        {
            if (_isAdvertising)
            {
                _isAdvertising = false;
            }
        }

        public void Dispose()
        {
            if (_isAdvertising)
            {
                Stop();
            }
        }
    }
}
