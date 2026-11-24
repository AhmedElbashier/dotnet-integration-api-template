using System.Text;
using IntegrationApi.Webhooks;

namespace IntegrationApi.Tests;

public class WebhookSignatureVerifierTests
{
    private const string Secret = "test-secret";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private readonly WebhookSignatureVerifier _sut = new(Secret, TimeSpan.FromMinutes(5), new FakeClock(Now));
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"id\":\"evt_1\"}");

    [Fact]
    public void Valid_signature_is_accepted()
    {
        var ts = Now.ToUnixTimeSeconds().ToString();
        var sig = WebhookSignatureVerifier.ComputeSignature(Secret, ts, Body);
        Assert.Equal(SignatureResult.Valid, _sut.Verify(sig, ts, Body));
    }

    [Fact]
    public void Tampered_body_is_rejected()
    {
        var ts = Now.ToUnixTimeSeconds().ToString();
        var sig = WebhookSignatureVerifier.ComputeSignature(Secret, ts, Body);
        var tampered = Encoding.UTF8.GetBytes("{\"id\":\"evt_2\"}");
        Assert.Equal(SignatureResult.Invalid, _sut.Verify(sig, ts, tampered));
    }

    [Fact]
    public void Wrong_secret_is_rejected()
    {
        var ts = Now.ToUnixTimeSeconds().ToString();
        var sig = WebhookSignatureVerifier.ComputeSignature("other-secret", ts, Body);
        Assert.Equal(SignatureResult.Invalid, _sut.Verify(sig, ts, Body));
    }

    [Fact]
    public void Old_timestamp_is_rejected_as_replay()
    {
        var ts = Now.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var sig = WebhookSignatureVerifier.ComputeSignature(Secret, ts, Body);
        Assert.Equal(SignatureResult.Expired, _sut.Verify(sig, ts, Body));
    }

    [Theory]
    [InlineData(null, "1700000000", SignatureResult.MissingHeaders)]
    [InlineData("sha256=abc", null, SignatureResult.MissingHeaders)]
    [InlineData("sha256=abc", "not-a-number", SignatureResult.BadTimestamp)]
    public void Malformed_headers_are_rejected(string? sig, string? ts, SignatureResult expected)
        => Assert.Equal(expected, _sut.Verify(sig, ts, Body));
}
