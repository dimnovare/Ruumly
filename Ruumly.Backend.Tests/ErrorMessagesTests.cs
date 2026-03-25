using FluentAssertions;
using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Tests;

public class ErrorMessagesTests
{
    [Theory]
    [InlineData("et", "Vale e-post")]
    [InlineData("en", "Invalid email")]
    [InlineData("ru", "Неверный email")]
    public void InvalidCredentials_ReturnsCorrectLanguage(
        string lang, string expectedStart)
    {
        var msg = ErrorMessages.Get(
            "INVALID_CREDENTIALS", lang);
        msg.Should().StartWith(expectedStart);
    }

    [Fact]
    public void UnknownKey_ReturnsFallback()
    {
        var result = ErrorMessages.Get(
            "UNKNOWN_KEY_XYZ", "et");
        result.Should().Be("UNKNOWN_KEY_XYZ");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("zh")]
    public void UnknownLang_FallsBackToEt(string? lang)
    {
        var et = ErrorMessages.Get(
            "INVALID_CREDENTIALS", "et");
        var fallback = ErrorMessages.Get(
            "INVALID_CREDENTIALS", lang);
        fallback.Should().Be(et);
    }
}
