namespace CrmApi.Models;

// The real-estate optional module (workspace-customization skill §4) — a
// self-contained table of structured, genuinely-distinct listing data (not
// an easy escape hatch from the FieldDefinition custom-field tier, which
// already covers freeform key/value fields like bedrooms/square footage on
// the real-estate starter template). One Listing extends one Deal 1:1; a
// workspace only sees this data when "listings" is in
// WorkspaceSettings.EnabledModules — see ListingsController.
public class Listing
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string DealId { get; set; }
    public string? ListingAgentName { get; set; }
    public string? ListingUrl { get; set; }
    public DateTime? OpenHouseAt { get; set; }
    public decimal? CommissionPercent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public Deal? Deal { get; set; }
}
