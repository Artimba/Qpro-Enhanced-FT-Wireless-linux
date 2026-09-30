namespace Qpro.Shared;

internal static class DeveloperCheekPuffBaseline
{
    // Derived from separate relaxed/left/right Quest Pro holds on 2026-09-30.
    // These are Balanced cheek strengths, not raw XR_FB weights. A personal
    // source profile takes precedence because puff range varies by wearer.
    private static readonly CheekPuffCalibration VirtualDesktop = new(
        LeftNeutral: 0.0f, LeftFull: 0.17802724f,
        RightNeutral: 0.0f, RightFull: 0.31023937f);
    private static readonly CheekPuffCalibration SteamLink = new(
        LeftNeutral: 0.0f, LeftFull: 0.33188242f,
        RightNeutral: 0.0f, RightFull: 0.31035322f);

    internal static CheekPuffCalibration? ForSource(string source) => source switch
    {
        CheekPuffCalibrationProfile.VirtualDesktopSource => VirtualDesktop,
        CheekPuffCalibrationProfile.SteamLinkSource => SteamLink,
        _ => null,
    };
}
