namespace TateScribe.Core.Images;

public sealed record NormalizedCrop(double Left, double Top, double Right, double Bottom)
{
    public static NormalizedCrop Full { get; } = new(0, 0, 1, 1);

    public void Validate()
    {
        if (Left is < 0 or >= 1 || Top is < 0 or >= 1 || Right is <= 0 or > 1 || Bottom is <= 0 or > 1 || Left >= Right || Top >= Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(Left), "Crop must be a non-empty normalized rectangle.");
        }
    }
}

public sealed record PreparedImage(string CachePath, int Width, int Height, string CacheKey);
