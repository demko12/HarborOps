namespace HarborOps.Modules.Identity.Application;

/// <summary>Central settings for anonymous credential submission protection.</summary>
public sealed class LoginThrottlingOptions
{
    public const string SectionName = "LoginThrottling";
    public const string IpPolicyName = "identity-login-ip";

    public int AccountFailedAttemptLimit { get; set; } = 5;
    public TimeSpan AccountFailureWindow { get; set; } = TimeSpan.FromMinutes(5);
    public int ClientAttemptLimit { get; set; } = 10;
    public TimeSpan ClientAttemptWindow { get; set; } = TimeSpan.FromMinutes(1);
}
