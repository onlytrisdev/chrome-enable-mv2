using System.Runtime.InteropServices;

namespace Mv2Enabler.Tests;

public sealed class NativeLayoutTests
{
    [Fact]
    public void Win64InputStructureHasExpectedSize()
    {
        Assert.Equal(40, Marshal.SizeOf<NativeMethods.Input>());
    }
}
