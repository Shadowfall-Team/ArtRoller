using System.Text.Json.Serialization;

namespace ArtRoller;

/// <summary>
/// One art roll, as stored in a <c>.hsv</c> file. Every number is the editor's slider value / 100,
/// so for every adjustment 1 is unchanged; missing fields take these neutral defaults, which keeps
/// older files rendering as they did.
///
/// A record so the editor can detect unsaved changes by value. The selective sets are immutable,
/// so a <c>with</c> copy of the roll never shares a set the editor goes on to change.
/// </summary>
public record CardHsvData
{
    public const int SelectiveSetCount = 3;

    [JsonPropertyName("card_id")]       public string  CardId       { get; set; } = "";
    [JsonPropertyName("portrait_path")] public string? PortraitPath { get; set; }

    [JsonPropertyName("tint")]          public string  Tint         { get; set; } = "#ffffff";

    [JsonPropertyName("hue")]           public float   Hue          { get; set; } = 1f;
    [JsonPropertyName("saturation")]    public float   Saturation   { get; set; } = 1f;
    [JsonPropertyName("value")]         public float   Value        { get; set; } = 1f;
    [JsonPropertyName("gamma")]         public float   Gamma        { get; set; } = 1f;

    [JsonPropertyName("red")]           public float   Red          { get; set; } = 1f;
    [JsonPropertyName("red_offset")]    public float   RedOffset    { get; set; } = 1f;
    [JsonPropertyName("green")]         public float   Green        { get; set; } = 1f;
    [JsonPropertyName("green_offset")]  public float   GreenOffset  { get; set; } = 1f;
    [JsonPropertyName("blue")]          public float   Blue         { get; set; } = 1f;
    [JsonPropertyName("blue_offset")]   public float   BlueOffset   { get; set; } = 1f;

    [JsonPropertyName("contrast")]      public float   Contrast     { get; set; } = 1f;

    [JsonPropertyName("selective_1")]   public SelectiveHue Selective1 { get; set; } = new();
    [JsonPropertyName("selective_2")]   public SelectiveHue Selective2 { get; set; } = new();
    [JsonPropertyName("selective_3")]   public SelectiveHue Selective3 { get; set; } = new();

    [JsonPropertyName("flip_h")]        public bool    FlipH        { get; set; } = false;
    [JsonPropertyName("flip_v")]        public bool    FlipV        { get; set; } = false;

    public SelectiveHue GetSelective(int index) => index switch
    {
        0 => Selective1,
        1 => Selective2,
        2 => Selective3,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public void SetSelective(int index, SelectiveHue set)
    {
        switch (index)
        {
            case 0: Selective1 = set; break;
            case 1: Selective2 = set; break;
            case 2: Selective3 = set; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}

/// <summary>
/// Changes only the pixels whose hue is near <see cref="Color"/>. Does nothing while
/// <see cref="Shift"/>, <see cref="Saturation"/> and <see cref="Brightness"/> are all 1.
/// </summary>
public record SelectiveHue
{
    /// <summary>The color to target. Only its hue matters.</summary>
    [JsonPropertyName("color")]       public string Color      { get; init; } = "#ff0000";
    /// <summary>How far from the target hue still counts, 1 being a quarter of the color wheel either side.</summary>
    [JsonPropertyName("width")]       public float  Width      { get; init; } = 0.3f;
    [JsonPropertyName("shift")]       public float  Shift      { get; init; } = 1f;
    [JsonPropertyName("saturation")]  public float  Saturation { get; init; } = 1f;
    [JsonPropertyName("brightness")]  public float  Brightness { get; init; } = 1f;
    /// <summary>How much of <see cref="Width"/> fades out rather than applying fully: 0 is a hard edge, 1 fades from the target itself.</summary>
    [JsonPropertyName("softness")]    public float  Softness   { get; init; } = 0.5f;

    [JsonIgnore]
    public bool IsActive => Width > 0f && (Shift != 1f || Saturation != 1f || Brightness != 1f);
}
