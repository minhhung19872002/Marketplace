namespace ShopHub.Application.SystemConfig;

/// <summary>
/// SITE.MODE (G4-D): "demo" — a showcase install (a notice on the buyer site, the simulated payment gateway usable, Flash
/// Sale slots filled with sample items); "live" — running for real (no simulated gateway, slots only opened for shops to
/// register). Anything that is not exactly "demo" counts as live, so a typo never turns a real site into a demo.
/// </summary>
public static class SiteMode
{
    public const string Demo = "demo";
    public const string Live = "live";

    public static bool IsValid(string value) => value.Trim() is Demo or Live;

    public static async Task<bool> IsDemoAsync(Abstractions.ISystemParameters parameters, CancellationToken ct) =>
        string.Equals((await parameters.GetStringAsync(ParameterKeys.SiteMode, ct)).Trim(), Demo, StringComparison.Ordinal);
}
