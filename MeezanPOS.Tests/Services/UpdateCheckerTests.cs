using System;
using FluentAssertions;
using MeezanPOS.Application.Services;
using Xunit;

namespace MeezanPOS.Tests.Services;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V2.0.1-beta", "2.0.1")]
    [InlineData("v3", "3.0.0")]
    public void ParseTag_ReadsReleaseTags(string tag, string expected)
        => UpdateChecker.ParseTag(tag).Should().Be(Version.Parse(expected));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void ParseTag_RejectsInvalidTags(string? tag)
        => UpdateChecker.ParseTag(tag).Should().BeNull();

    [Fact]
    public void NewerVersion_IsDetected_OnlyWhenGreater()
    {
        var current = new Version(1, 1, 0);
        new UpdateChecker.UpdateInfo(current, new Version(1, 2, 0), "v1.2.0", "").IsNewer.Should().BeTrue();
        new UpdateChecker.UpdateInfo(current, new Version(1, 1, 0), "v1.1.0", "").IsNewer.Should().BeFalse();
        new UpdateChecker.UpdateInfo(current, new Version(1, 0, 9), "v1.0.9", "").IsNewer.Should().BeFalse();
    }
}
