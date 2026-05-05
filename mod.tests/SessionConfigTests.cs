using Xunit;

namespace StsStats.Tests;

public class SessionConfigTests
{
    [Fact]
    public void NormalizeUrl_TrimsTrailingSlash()
    {
        Assert.Equal("http://x", SessionConfig.NormalizeUrl("http://x/"));
        Assert.Equal("http://x/y", SessionConfig.NormalizeUrl("http://x/y/"));
        Assert.Equal("http://x", SessionConfig.NormalizeUrl("http://x"));
    }

    [Fact]
    public void NormalizeUrl_TrimsWhitespace()
    {
        Assert.Equal("http://x", SessionConfig.NormalizeUrl("  http://x  "));
    }

    [Fact]
    public void NormalizeUrl_EmptyOrWhitespace_BecomesEmpty()
    {
        Assert.Equal("", SessionConfig.NormalizeUrl(""));
        Assert.Equal("", SessionConfig.NormalizeUrl("   "));
    }

    [Fact]
    public void OverrideBackendUrl_DisablesHttpWhenEmpty()
    {
        SessionConfig.OverrideBackendUrl("");
        Assert.False(SessionConfig.HttpEnabled);

        SessionConfig.OverrideBackendUrl("http://localhost:8080");
        Assert.True(SessionConfig.HttpEnabled);
        Assert.Equal("http://localhost:8080", SessionConfig.BackendUrl);
    }
}
