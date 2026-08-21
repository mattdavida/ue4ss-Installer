using Avalonia.Media.Imaging;

namespace UE4SSInstaller.Services;

public sealed class DetectedGame
{
    public required string Name { get; init; }
    public required string InstallPath { get; init; }
    public required string Win64Path { get; init; }
    public string? ExePath { get; init; }
    public Bitmap? Icon { get; init; }
    public string? ChannelLabel { get; set; }

    public string Initial
    {
        get
        {
            foreach (var c in Name)
            {
                if (char.IsLetterOrDigit(c))
                    return char.ToUpperInvariant(c).ToString();
            }

            return "?";
        }
    }

    public bool HasIcon => Icon is not null;
    public bool HasChannelLabel => !string.IsNullOrEmpty(ChannelLabel);
}
