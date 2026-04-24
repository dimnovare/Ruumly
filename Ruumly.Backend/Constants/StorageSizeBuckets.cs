namespace Ruumly.Backend.Constants;

public static class StorageSizeBuckets
{
    public record Bucket(string Code, decimal? MinM2, decimal? MaxM2);

    // Boundaries: XS up to 1.5, S 1.5-3.7, M 3.7-7.4, L 7.4-15, XL 15+
    // Boundaries are inclusive on lower bound, exclusive on upper bound.
    // Aligned with industry-standard self-storage sizing (boxo.ee, Shurgard).
    public static readonly IReadOnlyList<Bucket> All = new List<Bucket>
    {
        new("XS", null,  1.5m),
        new("S",  1.5m,  3.7m),
        new("M",  3.7m,  7.4m),
        new("L",  7.4m,  15m),
        new("XL", 15m,   null),
    };

    public static Bucket? FindByCode(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(b => string.Equals(b.Code, code, StringComparison.OrdinalIgnoreCase));

    public static string? CategorizeBySize(decimal? sizeM2)
    {
        if (sizeM2 is null) return null;
        var bucket = All.FirstOrDefault(b =>
            (b.MinM2 == null || sizeM2 >= b.MinM2) &&
            (b.MaxM2 == null || sizeM2 <  b.MaxM2));
        return bucket?.Code;
    }
}
