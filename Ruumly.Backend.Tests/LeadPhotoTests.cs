using System.Text;
using FluentAssertions;
using Ruumly.Backend.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The photo feature's two dangerous edges.
///
/// It exists because a provider refused to quote a real move without pictures.
/// But it means an ANONYMOUS endpoint accepts multi-megabyte bodies, and what it
/// stores is later shown to a stranger who was asked to price the job. Two
/// specific failures are worth a test each:
///
///  • EXIF GPS. A phone photo of somebody's living room routinely carries the
///    coordinates of their home. Forwarding that to a stranger would be the
///    worst privacy leak in the whole concierge loop, and invisible to everyone.
///  • Key forgery. The lead's photo keys arrive from the browser and decide
///    which private-bucket object the quote page streams back. Anything other
///    than a key this feature minted must be refused, or a quote token becomes
///    a reader for signed contracts.
/// </summary>
public class LeadPhotoTests
{
    private static async Task<Stream> JpegWithGpsAsync(int width = 2400, int height = 1800)
    {
        using var image = new Image<Rgba32>(width, height);

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, new[]
        {
            new Rational(59), new Rational(26), new Rational(13),
        });
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, new[]
        {
            new Rational(24), new Rational(45), new Rational(14),
        });
        exif.SetValue(ExifTag.Make, "SecretPhone");
        image.Metadata.ExifProfile = exif;

        var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms);
        ms.Position = 0;
        return ms;
    }

    // ─── Normalisation ────────────────────────────────────────────────────────

    [Fact]
    public async Task StripsExif_SoAHomePhotoDoesNotCarryItsCoordinates()
    {
        await using var source = await JpegWithGpsAsync();

        var normalized = await LeadPhotoNormalizer.NormalizeAsync(source);

        normalized.Should().NotBeNull();
        using var stored = Image.Load(normalized!);
        stored.Metadata.ExifProfile.Should().BeNull(
            "a provider must never receive the GPS coordinates of the customer's home");
    }

    [Fact]
    public async Task BoundsTheLongEdge()
    {
        await using var source = await JpegWithGpsAsync(4032, 3024);

        var normalized = await LeadPhotoNormalizer.NormalizeAsync(source);

        using var stored = Image.Load(normalized!);
        Math.Max(stored.Width, stored.Height).Should().BeLessThanOrEqualTo(LeadPhotoNormalizer.MaxEdgePixels);
        // Aspect ratio preserved — a squashed photo is worse than no photo.
        (stored.Width > stored.Height).Should().BeTrue();
    }

    [Fact]
    public async Task ReEncodesAsJpeg_WhateverWentIn()
    {
        using var png = new Image<Rgba32>(64, 64);
        await using var source = new MemoryStream();
        await png.SaveAsPngAsync(source);
        source.Position = 0;

        var normalized = await LeadPhotoNormalizer.NormalizeAsync(source);

        Image.DetectFormat(normalized!).Should().BeOfType<JpegFormat>(
            "storing only bytes we produced is what makes the upload endpoint uninteresting to abuse");
    }

    [Theory]
    [InlineData("<?php system($_GET['c']); ?>")]
    [InlineData("#!/bin/sh\nrm -rf /")]
    [InlineData("GIF89a but not really")]
    public async Task RejectsAnythingThatIsNotADecodableImage(string payload)
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        (await LeadPhotoNormalizer.NormalizeAsync(source)).Should().BeNull();
    }

    [Fact]
    public async Task RejectsEmptyInput()
    {
        await using var empty = new MemoryStream();
        (await LeadPhotoNormalizer.NormalizeAsync(empty)).Should().BeNull();
    }

    // ─── Key validation ───────────────────────────────────────────────────────

    [Fact]
    public void AcceptsOnlyKeysThisFeatureMints()
    {
        var real = $"{LeadPhotoNormalizer.KeyPrefix}{Guid.NewGuid():N}.jpg";

        LeadPhotoNormalizer.KeepIssuedKeys([real]).Should().ContainSingle().And.Contain(real);
    }

    [Theory]
    // The attack this exists to stop: pointing a lead at a signed contract and
    // reading it back through the quote page.
    [InlineData("contracts/signed-contract-2026.pdf")]
    [InlineData("lead-photos/../contracts/secret.pdf")]
    [InlineData("lead-photos/../../etc/passwd")]
    // Right prefix, wrong shape.
    [InlineData("lead-photos/anything.jpg")]
    [InlineData("lead-photos/.jpg")]
    [InlineData("lead-photos/00112233445566778899aabbccddeeff.png")]
    [InlineData("lead-photos/00112233445566778899AABBCCDDEEFF.jpg")]
    [InlineData("lead-photos/00112233445566778899aabbccddee.jpg")]
    [InlineData("lead-photos/00112233445566778899aabbccddeeff0.jpg")]
    // No prefix at all.
    [InlineData("00112233445566778899aabbccddeeff.jpg")]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesEverythingElse(string forged)
    {
        LeadPhotoNormalizer.KeepIssuedKeys([forged]).Should().BeEmpty();
    }

    [Fact]
    public void CapsTheCount_BecauseTheBrowserIsNotTheAuthority()
    {
        var many = Enumerable.Range(0, 50)
            .Select(_ => $"{LeadPhotoNormalizer.KeyPrefix}{Guid.NewGuid():N}.jpg")
            .ToList();

        LeadPhotoNormalizer.KeepIssuedKeys(many)
            .Should().HaveCount(LeadPhotoNormalizer.MaxPhotosPerLead);
    }

    [Fact]
    public void DedupesAndSurvivesNulls()
    {
        var key = $"{LeadPhotoNormalizer.KeyPrefix}{Guid.NewGuid():N}.jpg";

        LeadPhotoNormalizer.KeepIssuedKeys([key, key, null, "  "]).Should().ContainSingle();
        LeadPhotoNormalizer.KeepIssuedKeys(null).Should().BeEmpty();
    }

    // ─── Reading them back off a lead ─────────────────────────────────────────

    [Fact]
    public void MalformedColumnNeverThrows()
    {
        // Photos are an optional extra. A bad parse must not be able to take down
        // the outreach email or the quote page that carry the request itself.
        foreach (var bad in new[] { "not json", "{\"a\":1}", "[1,2,3]", "", "   " })
            LeadPhotos.Keys(bad).Should().BeEmpty($"'{bad}' must not throw");

        LeadPhotos.Keys(null).Should().BeEmpty();
        LeadPhotos.Count(null).Should().Be(0);
    }

    [Fact]
    public void ReValidatesOnTheWayOut()
    {
        // The column is written by us today, but a key that reaches the quote page
        // decides which private object is streamed to a provider — so the check
        // belongs at the point of use too, not only at the point of storage.
        var json = """["contracts/signed.pdf","lead-photos/00112233445566778899aabbccddeeff.jpg"]""";

        LeadPhotos.Keys(json).Should().ContainSingle()
            .Which.Should().StartWith(LeadPhotoNormalizer.KeyPrefix);
    }
}
