namespace SampleMobile.Web.Common;

/// <summary>
/// Policy names. Each policy pins its authentication scheme, so the dashboard cookie can never
/// authorize a mobile API call and a mobile bearer token can never open the dashboard.
/// </summary>
public static class AuthorizationPolicies {
    public const string AdminDashboard = "AdminDashboard";
    public const string MobileApi = "MobileApi";
}
