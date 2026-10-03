using Godot;

namespace ArtRoller;

public static class CardShaderHelper
{
    private static readonly string ShaderPath = $"res://{MainFile.ModId}/shaders/color_adjust.gdshader";

    private static readonly CardHsvData Neutral = new();

    public static ShaderMaterial? CreateMaterial(CardHsvData? data)
    {
        // Missing when ArtRoller.pck is: leave cards unrecolored rather than throw inside NCard.Reload,
        // which breaks the game's own card screens.
        if (GD.Load<Shader>(ShaderPath) is not { } source) return null;

        var shader = (Shader)source.Duplicate();
        var mat = new ShaderMaterial();
        mat.Shader = shader;
        SetParameters(mat, data ?? Neutral);
        return mat;
    }

    /// <summary>
    /// Renders <paramref name="portrait"/> with <paramref name="data"/>, flips included. Null resets
    /// it to neutral, which callers need because portrait nodes are reused between cards.
    /// </summary>
    public static void ApplyToPortrait(TextureRect portrait, CardHsvData? data)
    {
        data ??= Neutral;

        if (portrait.Material is ShaderMaterial existing)
            SetParameters(existing, data);
        else
            portrait.Material = CreateMaterial(data);

        portrait.FlipH = data.FlipH;
        portrait.FlipV = data.FlipV;
    }

    private static void SetParameters(ShaderMaterial mat, CardHsvData data)
    {
        mat.SetShaderParameter("hue_shift",    data.Hue);
        mat.SetShaderParameter("saturation",   data.Saturation);
        mat.SetShaderParameter("value",        data.Value);
        mat.SetShaderParameter("gamma",        Mathf.Max(data.Gamma, 0.1f));
        mat.SetShaderParameter("red",          data.Red);
        mat.SetShaderParameter("green",        data.Green);
        mat.SetShaderParameter("blue",         data.Blue);
        // Stored with 1 as unchanged like every other field; the shader wants 0 as unchanged.
        mat.SetShaderParameter("red_offset",   data.RedOffset   - 1f);
        mat.SetShaderParameter("green_offset", data.GreenOffset - 1f);
        mat.SetShaderParameter("blue_offset",  data.BlueOffset  - 1f);
        mat.SetShaderParameter("contrast",     data.Contrast);
        mat.SetShaderParameter("tint",         ParseColor(data.Tint, Colors.White));

        SetSelectiveParameters(mat, data);
    }

    private static void SetSelectiveParameters(ShaderMaterial mat, CardHsvData data)
    {
        const int n = CardHsvData.SelectiveSetCount;
        var target = new float[n];
        var width = new float[n];
        var shift = new float[n];
        var saturation = new float[n];
        var brightness = new float[n];
        var softness = new float[n];
        bool anyActive = false;

        for (int i = 0; i < n; i++)
        {
            var set = data.GetSelective(i);
            saturation[i] = 1f;
            brightness[i] = 1f;

            // An inactive set is sent as width 0, which the shader skips outright.
            if (!set.IsActive) continue;
            anyActive = true;
            
            target[i]     = ParseColor(set.Color, Colors.Red).H;
            width[i]      = set.Width * 0.25f;
            shift[i]      = (set.Shift - 1f) * 0.5f;
            saturation[i] = Mathf.Max(set.Saturation, 0f);
            brightness[i] = Mathf.Max(set.Brightness, 0f);
            softness[i]   = Mathf.Clamp(set.Softness, 0f, 1f);
        }

        // Skips the per-pixel color conversion entirely on the many cards with no selective sets.
        mat.SetShaderParameter("selective_active",     anyActive);
        mat.SetShaderParameter("selective_target_hue", target);
        mat.SetShaderParameter("selective_width",      width);
        mat.SetShaderParameter("selective_shift",      shift);
        mat.SetShaderParameter("selective_saturation", saturation);
        mat.SetShaderParameter("selective_brightness", brightness);
        mat.SetShaderParameter("selective_softness",   softness);
    }

    public static Color ParseColor(string? html, Color fallback) =>
        !string.IsNullOrWhiteSpace(html) && Color.HtmlIsValid(html) ? Color.FromHtml(html) : fallback;
}
