shader_type canvas_item;
// ─────────────────────────────────────────────
//  Color Adjustment Shader  –  Godot 4.x
//  Works on Sprite2D, TextureRect, etc.
// ─────────────────────────────────────────────

// ── RGB Channels ──────────────────────────────
uniform float red   : hint_range(0.0, 2.0) = 1.0;  // 1.0 = unchanged
uniform float green : hint_range(0.0, 2.0) = 1.0;
uniform float blue  : hint_range(0.0, 2.0) = 1.0;

// ── RGB Offset (additive shift, –1 … +1) ──────
uniform float red_offset   : hint_range(-1.0, 1.0) = 0.0;
uniform float green_offset : hint_range(-1.0, 1.0) = 0.0;
uniform float blue_offset  : hint_range(-1.0, 1.0) = 0.0;

// ── Alpha ─────────────────────────────────────
uniform float alpha : hint_range(0.0, 1.0) = 1.0;

// ── HSV ───────────────────────────────────────
uniform float hue_shift  : hint_range(0.0, 2.0) = 1.0;  // 1.0 = no shift
uniform float saturation : hint_range(0.0, 5.0) = 1.0;  // 1.0 = unchanged
uniform float value      : hint_range(0.0, 2.0) = 1.0;  // 1.0 = unchanged

// ── Extra ─────────────────────────────────────
uniform float contrast : hint_range(0.0, 3.0) = 1.0;
uniform float gamma    : hint_range(0.1, 3.0) = 1.0;
uniform vec4  tint     : source_color = vec4(1.0);

// ── Selective hue: three independent sets ─────
// Hues and widths are in turns of the color wheel. A set with width 0 is off.
uniform bool  selective_active = false;
uniform float selective_target_hue[3];
uniform float selective_width[3];
uniform float selective_shift[3];
uniform float selective_saturation[3];
uniform float selective_brightness[3];
uniform float selective_softness[3];

vec3 rgb_to_hsv(vec3 c) {
    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

vec3 hsv_to_rgb(vec3 c) {
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

// ─────────────────────────────────────────────
//  YIQ matrices
// ─────────────────────────────────────────────
const mat3 RGB_TO_YIQ = mat3(
    vec3(0.2989,  0.5959,  0.2115),
    vec3(0.5870, -0.2774, -0.5229),
    vec3(0.1140, -0.3216,  0.3114));

// ─────────────────────────────────────────────
//  Vertex – capture modulate so COLOR works
// ─────────────────────────────────────────────
varying vec4 modulate_color;

void vertex() {
    modulate_color = COLOR;
}

// ─────────────────────────────────────────────
//  Fragment
// ─────────────────────────────────────────────
void fragment() {
    vec4 col = texture(TEXTURE, UV);

    // 1. RGB channel multiply + additive offset
    col.rgb *= vec3(red, green, blue);
    col.rgb += vec3(red_offset, green_offset, blue_offset);

    // 2. Convert to YIQ
    col.rgb = RGB_TO_YIQ * col.rgb;

    // 3. Hue shift (1.0 = no shift, maps to 0 radians)
    float hue = mix(0.0, 6.283185, 1.0 - hue_shift);
    float sin_hue = sin(hue);
    float cos_hue = cos(hue);
    mat3 hue_mat = mat3(
        vec3(1.0,     0.0,      0.0),
        vec3(0.0, cos_hue, -sin_hue),
        vec3(0.0, sin_hue,  cos_hue));
    col.rgb *= hue_mat;

    // 4. Saturation
    col.rgb = mat3(
        vec3(1.0,        0.0,        0.0),
        vec3(0.0, saturation,        0.0),
        vec3(0.0,        0.0, saturation)) * col.rgb;

    // 5. Value (brightness)
    col.rgb = mix(vec3(0.0), col.rgb, value);

    // 6. Convert back to RGB
    col.rgb = inverse(RGB_TO_YIQ) * col.rgb;

    // 7. Contrast (pivot at 0.5)
    col.rgb = (col.rgb - 0.5) * contrast + 0.5;

    // 8. Gamma correction
    col.rgb = pow(max(col.rgb, vec3(0.0)), vec3(gamma));

    // 9. Tint multiply
    col *= tint;

    // 9b. Selective hue, last and in order, so each set works on the image as it then looks: set 1
    //     on the finished Standard adjustments, set 2 on set 1's result, set 3 on set 2's. That is
    //     what makes the color picker's eyedropper, which reads the screen, pick what the set will
    //     actually match. Clamped first because the screen can only show 0..1.
    if (selective_active) {
        col.rgb = clamp(col.rgb, 0.0, 1.0);

        for (int i = 0; i < 3; i++) {
            if (selective_width[i] <= 0.0) continue;

            vec3 hsv = rgb_to_hsv(col.rgb);
            float dist = abs(fract(hsv.x - selective_target_hue[i] + 0.5) - 0.5);
            // Softness is the share of the width that fades; the rest applies at full strength.
            // smoothstep needs its edges apart, hence the floor under the fade.
            float fade_start = min(selective_width[i] * (1.0 - selective_softness[i]), selective_width[i] - 0.0001);
            float weight = 1.0 - smoothstep(fade_start, selective_width[i], dist);
            if (weight <= 0.0) continue;

            hsv.x = fract(hsv.x + selective_shift[i] * weight);
            hsv.y = clamp(hsv.y * mix(1.0, selective_saturation[i], weight), 0.0, 1.0);
            hsv.z = clamp(hsv.z * mix(1.0, selective_brightness[i], weight), 0.0, 1.0);
            col.rgb = hsv_to_rgb(hsv);
        }
    }

    // 10. Alpha
    col.a *= alpha;

    COLOR = clamp(col, 0.0, 1.0) * modulate_color;
}
