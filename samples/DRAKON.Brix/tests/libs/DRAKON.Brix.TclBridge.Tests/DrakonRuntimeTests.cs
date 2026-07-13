using DRAKON.Brix.Drakon;

using SilverAssertions;
using Xunit;

namespace DRAKON.Brix.TclBridge.Tests;

/// <summary>
/// Placeholder smoke coverage confirming the extracted TclBridge library is
/// referenced and loads. The real coverage — opening every example .drn file
/// through the exact DRAKON open path — will be added here later.
/// </summary>
public class DrakonRuntimeTests
{
    [Fact]
    public void Construct_and_dispose_without_starting_is_safe()
    {
        //Arrange
        var runtime = new DrakonRuntime();

        //Act
        runtime.Dispose();

        //Assert
        runtime.Should().NotBeNull();
    }
}
