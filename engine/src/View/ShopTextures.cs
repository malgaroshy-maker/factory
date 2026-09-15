using Godot;

namespace FactoryForge.View;

/// <summary>
/// The floor and wall surfaces, baked in code (EN-01).
///
/// This project ships no image assets and should not start: a texture on disk
/// is a licence to check, a file the exporter has to be told about, and a thing
/// that can go missing from a release without the build noticing. Every surface
/// here is generated at startup from a seeded hash, which costs a few
/// milliseconds once and cannot be lost.
///
/// What it is for is less obvious than it looks. The floor was a dark plane
/// with a faint speckle on it, and the scene had no walls at all — so a factory
/// simulator opened on a machine standing in a grey void, which reads as a
/// product render rather than as a place. Scale is the specific casualty: with
/// nothing around it, a conveyor could be two metres long or twenty and nothing
/// on screen says which. A concrete floor with two-metre bays and a wall of
/// known-height cladding behind it answers that before anybody has to think
/// about it.
///
/// Every tile is **seamless**: the noise lattice wraps at the tile period, so a
/// 40 m floor built from twenty repeats has no visible grid of joins other than
/// the ones that are painted on deliberately.
/// </summary>
public static class ShopTextures
{
    /// <summary>One concrete bay, in metres. Real slabs are poured in bays of
    /// two to three metres with a control joint between them, which is why
    /// this number is the one thing on the floor a person can measure a
    /// conveyor against.</summary>
    public const float FloorTileMetres = 2.0f;

    /// <summary>One wall panel, in metres. Standard profiled cladding.</summary>
    public const float WallTileMetres = 1.0f;

    private const int TileSize = 256;

    private static ImageTexture? _floorAlbedo;
    private static ImageTexture? _floorNormal;
    private static ImageTexture? _floorRough;
    private static ImageTexture? _wallAlbedo;
    private static ImageTexture? _wallNormal;

    /// <summary>
    /// Poured concrete with a control joint around each bay.
    ///
    /// Built once and shared by every caller: the deterministic scene and the
    /// physics scene both stage themselves through
    /// <see cref="StudioEnvironment"/>, and two independently generated floors
    /// would make a screenshot of one no longer comparable with the other —
    /// which is the whole reason that staging is shared in the first place.
    /// </summary>
    public static StandardMaterial3D FloorMaterial(float groundExtent)
    {
        BuildFloorTextures();

        float repeats = groundExtent / FloorTileMetres;
        return new StandardMaterial3D
        {
            // Dark enough to be a working floor. The first pass sat at 0.52
            // and read as polished tile under the sky ambient this project
            // uses -- a shop floor is grey, not white, and a bright one also
            // pulls the eye off the machine standing on it.
            AlbedoColor = new Color(0.34f, 0.34f, 0.35f),
            AlbedoTexture = _floorAlbedo,
            NormalEnabled = true,
            NormalTexture = _floorNormal,
            // Shallow. A floor is not corrugated iron, and a normal map strong
            // enough to see from standing height turns a shop floor into
            // gravel under a low sun.
            NormalScale = 0.55f,
            RoughnessTexture = _floorRough,
            Roughness = 1.0f,
            Metallic = 0.0f,
            Uv1Scale = new Vector3(repeats, repeats, 1.0f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    /// <summary>Profiled steel cladding, the sort every shop is lined with.</summary>
    public static StandardMaterial3D WallMaterial(float width, float height, Color tint)
    {
        BuildWallTextures();

        return new StandardMaterial3D
        {
            AlbedoColor = tint,
            AlbedoTexture = _wallAlbedo,
            NormalEnabled = true,
            NormalTexture = _wallNormal,
            NormalScale = 1.0f,
            Roughness = 0.75f,
            Metallic = 0.15f,
            Uv1Scale = new Vector3(width / WallTileMetres, height / WallTileMetres, 1.0f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            // The walls are one-sided and face inward, so the camera can orbit
            // outside the shop and still see the line. Culling the back face is
            // what makes that work; without it the view from outside is a blank
            // grey box.
            CullMode = BaseMaterial3D.CullModeEnum.Back,
        };
    }

    // ---------------------------------------------------------------- floor

    private static void BuildFloorTextures()
    {
        if (_floorAlbedo is not null) return;

        var height = new float[TileSize * TileSize];
        var albedo = new byte[TileSize * TileSize * 3];
        var rough = new byte[TileSize * TileSize * 3];

        for (int y = 0; y < TileSize; y++)
        {
            for (int x = 0; x < TileSize; x++)
            {
                float u = x / (float)TileSize;
                float v = y / (float)TileSize;

                // Aggregate: the coarse mottling of a power-floated slab.
                float grain = Fbm(u, v, period: 8, octaves: 4, seed: 11);
                // Fine speckle, so the surface still reads as concrete close up
                // rather than as a cloud.
                float speckle = Fbm(u, v, period: 64, octaves: 2, seed: 29);

                float shade = 0.80f + grain * 0.26f + (speckle - 0.5f) * 0.12f;
                float h = grain * 0.6f + speckle * 0.4f;

                // The control joint, cut around the edge of the bay. Sawn
                // joints are narrow, dark and slightly dirty, and they are the
                // single feature that makes a grey plane read as a floor
                // somebody poured.
                float edge = Mathf.Min(Mathf.Min(u, 1.0f - u), Mathf.Min(v, 1.0f - v));
                float joint = 1.0f - Mathf.SmoothStep(0.0f, 0.014f, edge);
                // Softer than the first pass's 0.34: a sawn joint is a darker
                // line, not a black one, and at full contrast twenty bays read
                // as a tiled floor rather than a poured one.
                shade = Mathf.Lerp(shade, 0.55f, joint);
                h = Mathf.Lerp(h, 0.0f, joint);

                // Worn patches: smoother where traffic polishes it, rougher in
                // between. Concrete does this and it is most of why a real
                // floor has highlights at all.
                float wear = Fbm(u, v, period: 4, octaves: 3, seed: 77);
                float roughness = Mathf.Clamp(0.94f - wear * 0.30f + joint * 0.10f, 0.35f, 1.0f);

                int i = y * TileSize + x;
                height[i] = h;
                WriteGrey(albedo, i, shade);
                WriteGrey(rough, i, roughness);
            }
        }

        _floorAlbedo = MakeTexture(albedo);
        _floorRough = MakeTexture(rough);
        _floorNormal = MakeNormal(height, strength: 2.2f);
    }

    // ----------------------------------------------------------------- wall

    private static void BuildWallTextures()
    {
        if (_wallAlbedo is not null) return;

        var height = new float[TileSize * TileSize];
        var albedo = new byte[TileSize * TileSize * 3];

        for (int y = 0; y < TileSize; y++)
        {
            for (int x = 0; x < TileSize; x++)
            {
                float u = x / (float)TileSize;
                float v = y / (float)TileSize;

                // Four ribs per metre panel. Trapezoidal rather than a sine:
                // profiled cladding has flats between its ribs, and a pure
                // sine reads as corrugated cardboard.
                float rib = Mathf.Abs(((u * 4.0f) % 1.0f) - 0.5f) * 2.0f;
                float profile = Mathf.SmoothStep(0.25f, 0.75f, rib);

                float grime = Fbm(u, v, period: 6, octaves: 3, seed: 53);
                // Streaking, which runs down a wall and not across it — so the
                // noise is stretched in v and left alone in u.
                float streak = Fbm(u * 3.0f % 1.0f, v * 0.25f, period: 8, octaves: 2, seed: 91);

                float shade = 0.62f + profile * 0.26f
                              + (grime - 0.5f) * 0.10f
                              - Mathf.Max(0.0f, streak - 0.55f) * 0.22f;

                // The fixing line where each panel is screwed to its rail.
                float seam = 1.0f - Mathf.SmoothStep(0.0f, 0.012f, Mathf.Min(v, 1.0f - v));
                shade = Mathf.Lerp(shade, 0.40f, seam * 0.8f);

                int i = y * TileSize + x;
                height[i] = profile * 0.85f + grime * 0.15f;
                WriteGrey(albedo, i, Mathf.Clamp(shade, 0.0f, 1.0f));
            }
        }

        _wallAlbedo = MakeTexture(albedo);
        _wallNormal = MakeNormal(height, strength: 4.0f);
    }

    // ---------------------------------------------------------------- noise

    /// <summary>
    /// Seamless value noise. <paramref name="period"/> is the lattice size in
    /// cells across the whole tile, and every lattice lookup wraps modulo it —
    /// which is the entire trick, and the reason a twenty-times-repeated floor
    /// shows no grid of seams.
    /// </summary>
    private static float Fbm(float u, float v, int period, int octaves, int seed)
    {
        float total = 0.0f;
        float amplitude = 1.0f;
        float sum = 0.0f;
        int cells = period;

        for (int o = 0; o < octaves; o++)
        {
            total += Value(u, v, cells, seed + o * 131) * amplitude;
            sum += amplitude;
            amplitude *= 0.5f;
            cells *= 2;
        }

        return total / sum;
    }

    private static float Value(float u, float v, int cells, int seed)
    {
        float fx = u * cells;
        float fy = v * cells;
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        float tx = Smooth(fx - x0);
        float ty = Smooth(fy - y0);

        float a = Hash(x0, y0, cells, seed);
        float b = Hash(x0 + 1, y0, cells, seed);
        float c = Hash(x0, y0 + 1, cells, seed);
        float d = Hash(x0 + 1, y0 + 1, cells, seed);

        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    private static float Smooth(float t) => t * t * (3.0f - 2.0f * t);

    private static float Hash(int x, int y, int period, int seed)
    {
        // Wrapped, so the lattice meets itself at the tile edge.
        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;

        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    // -------------------------------------------------------------- packing

    private static void WriteGrey(byte[] rgb, int index, float value)
    {
        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255.0f), 0, 255);
        rgb[index * 3 + 0] = b;
        rgb[index * 3 + 1] = b;
        rgb[index * 3 + 2] = b;
    }

    private static ImageTexture MakeTexture(byte[] rgb)
    {
        var image = Image.CreateFromData(TileSize, TileSize, false, Image.Format.Rgb8, rgb);
        // Mipmaps are not optional on a floor seen at a grazing angle across
        // forty metres: without them the far half of it boils.
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Tangent-space normal map from a height field, by central
    /// differences. Wrapped at the edges for the same reason the noise is.
    /// </summary>
    private static ImageTexture MakeNormal(float[] height, float strength)
    {
        var rgb = new byte[TileSize * TileSize * 3];

        for (int y = 0; y < TileSize; y++)
        {
            for (int x = 0; x < TileSize; x++)
            {
                float left = height[y * TileSize + Wrap(x - 1)];
                float right = height[y * TileSize + Wrap(x + 1)];
                float down = height[Wrap(y - 1) * TileSize + x];
                float up = height[Wrap(y + 1) * TileSize + x];

                var n = new Vector3((left - right) * strength, (down - up) * strength, 1.0f).Normalized();

                int i = (y * TileSize + x) * 3;
                rgb[i + 0] = (byte)Mathf.RoundToInt((n.X * 0.5f + 0.5f) * 255.0f);
                rgb[i + 1] = (byte)Mathf.RoundToInt((n.Y * 0.5f + 0.5f) * 255.0f);
                rgb[i + 2] = (byte)Mathf.RoundToInt((n.Z * 0.5f + 0.5f) * 255.0f);
            }
        }

        var image = Image.CreateFromData(TileSize, TileSize, false, Image.Format.Rgb8, rgb);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static int Wrap(int i) => ((i % TileSize) + TileSize) % TileSize;
}
