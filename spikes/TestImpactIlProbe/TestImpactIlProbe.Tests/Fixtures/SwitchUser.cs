namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>A dense switch compiles to the IL switch instruction, whose operand has a variable length.</summary>
internal static class SwitchUser
{
    public static int Pick(int value)
    {
        switch (value)
        {
            case 0:
                return 10;
            case 1:
                return 20;
            case 2:
                return 30;
            case 3:
                return 40;
            default:
                return SwitchTarget.Value();
        }
    }
}
