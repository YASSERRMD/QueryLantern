namespace QueryLantern.Tests;

using Microsoft.Extensions.Configuration;
using QueryLantern.Services;
using Xunit;

public class ApprovalServiceTests
{
    [Fact]
    public void Defaults_Require_Approval_And_Do_Not_AutoReject()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new ApprovalService(config);
        Assert.True(service.RequireApproval);
        Assert.False(service.AutoRejectWrites);
    }

    [Fact]
    public void Reads_Policy_From_Configuration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Approval:RequireApproval"] = "false",
                ["Approval:AutoRejectWrites"] = "true"
            })
            .Build();
        var service = new ApprovalService(config);
        Assert.False(service.RequireApproval);
        Assert.True(service.AutoRejectWrites);
    }
}
