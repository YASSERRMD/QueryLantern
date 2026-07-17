namespace QueryLantern.Services;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Policy for the human in the loop gate. By default every staged write requires explicit approval.
/// An operator can harden the app to auto-reject any write, or (future) auto-approve reads only.
/// </summary>
public sealed class ApprovalService
{
    public bool RequireApproval { get; }
    public bool AutoRejectWrites { get; }

    public ApprovalService(IConfiguration config)
    {
        RequireApproval = config["Approval:RequireApproval"] != "false";
        AutoRejectWrites = config["Approval:AutoRejectWrites"] == "true";
    }
}
