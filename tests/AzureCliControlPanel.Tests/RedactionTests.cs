using AzureCliControlPanel.Core.Cli;
using FluentAssertions;

namespace AzureCliControlPanel.Tests;

public sealed class RedactionTests
{
    [Fact]
    public void Redact_ShouldRemoveAccessTokenInJson()
    {
        var input = "{ \"access_token\": \"abc.def.ghi\", \"expires_in\": 3600 }";
        var outp = Redaction.Redact(input);

        outp.Should().NotContain("abc.def.ghi");
        outp.Should().Contain("***REDACTED***");
    }

    [Fact]
    public void Redact_ShouldRemoveBearerToken()
    {
        var input = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.aaa.bbb";
        var outp = Redaction.Redact(input);

        outp.Should().NotContain("eyJhbGci");
        outp.Should().Contain("***REDACTED***");
    }
}
