using System.Runtime.CompilerServices;

namespace LocalDebug.Demo;

public static class CounterMath
{
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int ComputeNext(int input)
    {
        int multiplier = 2;
        int adjustment = 7;
        int result = Multiply(input, multiplier) + adjustment;
        return result; // BREAKPOINT_HERE: inspect input, multiplier, adjustment, result.
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static int Multiply(int value, int multiplier)
    {
        return value * multiplier;
    }
}
