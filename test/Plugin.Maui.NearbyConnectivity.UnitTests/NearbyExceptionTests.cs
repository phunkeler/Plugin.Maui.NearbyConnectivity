namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Every <see cref="NearbyException"/> subclass carries at least the two-constructor contract
/// (message, message + inner), so the message-only case is asserted from one set of rows across
/// every subclass: a caller catching <see cref="NearbyException"/> must be able to handle any of
/// them without knowing which operation failed. Not every subclass has a real cause to chain — the
/// two timeout types (<see cref="NearbyConnectException"/>,
/// <see cref="NearbyTransferTimeoutException"/>) fire on an elapsed deadline, not a caught
/// exception, so they carry no <c>(message, inner)</c> overload and are excluded from
/// <c>MessageAndInner_PreservesBoth</c>.
/// </summary>
[Trait("Category", "Discovery")]
public class NearbyExceptionTests
{
    public sealed class Construction : NearbyExceptionTests
    {
        [Theory]
        [InlineData(typeof(NearbyException), TestDisplayName = "Base")]
        [InlineData(typeof(NearbyTransferException), TestDisplayName = "Transfer")]
        [InlineData(typeof(NearbyTransferTimeoutException), TestDisplayName = "TransferTimeout")]
        public void MessageOnly_PreservesMessageAndDerivesFromNearbyException(Type exceptionType)
        {
            // Arrange
            var message = "failed";

            // Act
            var exception = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(exceptionType, message));

            // Assert
            Assert.Equal(message, exception.Message);
            Assert.IsAssignableFrom<NearbyException>(exception);
        }

        // NearbyConnectException is deliberately absent from the theory above: a handshake failure
        // is never constructible without saying why, which is the whole point of collapsing three
        // exception types into one carrying a reason.
        [Fact]
        public void ConnectException_CarriesItsReason()
        {
            // Arrange
            var message = "failed";

            // Act
            var exception = new NearbyConnectException(NearbyConnectFailure.Failed, message);

            // Assert
            Assert.Equal(message, exception.Message);
            Assert.Equal(NearbyConnectFailure.Failed, exception.Reason);
            Assert.IsAssignableFrom<NearbyException>(exception);
        }

        [Theory]
        [InlineData(typeof(NearbyException), TestDisplayName = "Base")]
        [InlineData(typeof(NearbyTransferException), TestDisplayName = "Transfer")]
        public void MessageAndInner_PreservesBoth(Type exceptionType)
        {
            // Arrange
            var message = "failed";
            var inner = new InvalidOperationException("root cause");

            // Act
            var exception = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(exceptionType, message, inner));

            // Assert
            Assert.Equal(message, exception.Message);
            Assert.Same(inner, exception.InnerException);
        }
    }
}
