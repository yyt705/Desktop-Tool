using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;

namespace DeskFences
{
    public class FenceTheme
    {
        public string Id;
        public string Name;
        public Color BodyGlaze;       // Ultra-sheer liquid glass glaze on body
        public Color HeaderGlaze;     // Slightly brighter sheer glaze on header
        public Color TopCaustic;      // Curved glass top light reflection
        public Color BottomBounce;    // Soft bottom inner rim bounce light
        public Color RimTopLeft;      // Bright liquid-glass specular rim (top-left)
        public Color RimMid;          // Subtle mid-rim refraction
        public Color RimBottomRight;  // Secondary specular reflection (bottom-right)
        public Color DividerColor;    // Whisper-thin glass divider
        public Color TextColor;
        public bool TextShadow;       // Crisp subtle shadow for light text on clear glass
        public Color AccentColor;
        public bool IsLightMenu;

        public static readonly KeyValuePair<string, string>[] PresetList = new[]
        {
            new KeyValuePair<string, string>("liquid_clear",    "液态透明玻璃（默认）"),
            new KeyValuePair<string, string>("liquid_ice",      "液态冰晶（浅色壁纸适配）"),
            new KeyValuePair<string, string>("liquid_obsidian", "液态深曜玻璃"),
            new KeyValuePair<string, string>("liquid_aqua",     "液态晴蓝玻璃"),
            new KeyValuePair<string, string>("liquid_champagne","液态晨曦玻璃"),
        };

        public static FenceTheme Resolve(string themeId)
        {
            Color sysAccent = NativeMethods.GetSystemAccentColor();
            switch (themeId)
            {
                case "liquid_ice":
                    return new FenceTheme
                    {
                        Id = "liquid_ice",
                        Name = "液态冰晶",
                        BodyGlaze = Color.FromArgb(64, 245, 250, 255),
                        HeaderGlaze = Color.FromArgb(178, 246, 250, 255),
                        TopCaustic = Color.FromArgb(115, 255, 255, 255),
                        BottomBounce = Color.FromArgb(48, 255, 255, 255),
                        RimTopLeft = Color.FromArgb(235, 255, 255, 255),
                        RimMid = Color.FromArgb(120, 225, 238, 255),
                        RimBottomRight = Color.FromArgb(180, 255, 255, 255),
                        DividerColor = Color.FromArgb(95, 255, 255, 255),
                        TextColor = Color.FromArgb(20, 26, 36),
                        TextShadow = false,
                        AccentColor = sysAccent,
                        IsLightMenu = true
                    };
                case "liquid_obsidian":
                    return new FenceTheme
                    {
                        Id = "liquid_obsidian",
                        Name = "液态深曜玻璃",
                        BodyGlaze = Color.FromArgb(72, 16, 20, 30),
                        HeaderGlaze = Color.FromArgb(168, 22, 26, 38),
                        TopCaustic = Color.FromArgb(70, 255, 255, 255),
                        BottomBounce = Color.FromArgb(34, 210, 228, 255),
                        RimTopLeft = Color.FromArgb(200, 255, 255, 255),
                        RimMid = Color.FromArgb(72, 200, 220, 255),
                        RimBottomRight = Color.FromArgb(140, 220, 236, 255),
                        DividerColor = Color.FromArgb(62, 255, 255, 255),
                        TextColor = Color.FromArgb(248, 250, 255),
                        TextShadow = true,
                        AccentColor = sysAccent,
                        IsLightMenu = false
                    };
                case "liquid_aqua":
                    return new FenceTheme
                    {
                        Id = "liquid_aqua",
                        Name = "液态晴蓝玻璃",
                        BodyGlaze = Color.FromArgb(56, 128, 198, 255),
                        HeaderGlaze = Color.FromArgb(164, 34, 88, 148),
                        TopCaustic = Color.FromArgb(85, 235, 248, 255),
                        BottomBounce = Color.FromArgb(40, 180, 230, 255),
                        RimTopLeft = Color.FromArgb(225, 235, 248, 255),
                        RimMid = Color.FromArgb(92, 165, 220, 255),
                        RimBottomRight = Color.FromArgb(160, 210, 240, 255),
                        DividerColor = Color.FromArgb(72, 220, 242, 255),
                        TextColor = Color.FromArgb(250, 253, 255),
                        TextShadow = true,
                        AccentColor = Color.FromArgb(96, 205, 255),
                        IsLightMenu = false
                    };
                case "liquid_champagne":
                    return new FenceTheme
                    {
                        Id = "liquid_champagne",
                        Name = "液态晨曦玻璃",
                        BodyGlaze = Color.FromArgb(56, 255, 224, 190),
                        HeaderGlaze = Color.FromArgb(162, 118, 86, 58),
                        TopCaustic = Color.FromArgb(88, 255, 248, 238),
                        BottomBounce = Color.FromArgb(38, 255, 232, 205),
                        RimTopLeft = Color.FromArgb(225, 255, 248, 238),
                        RimMid = Color.FromArgb(90, 255, 226, 194),
                        RimBottomRight = Color.FromArgb(160, 255, 240, 220),
                        DividerColor = Color.FromArgb(72, 255, 242, 226),
                        TextColor = Color.FromArgb(255, 252, 246),
                        TextShadow = true,
                        AccentColor = Color.FromArgb(245, 185, 72),
                        IsLightMenu = false
                    };
                case "liquid_clear":
                default:
                    // Sheer frosted Liquid Glass body (~28%) + clearly legible frosted glass title bar (~65%)
                    return new FenceTheme
                    {
                        Id = "liquid_clear",
                        Name = "液态透明玻璃",
                        BodyGlaze = Color.FromArgb(52, 246, 249, 255),
                        HeaderGlaze = Color.FromArgb(168, 244, 248, 254),
                        TopCaustic = Color.FromArgb(98, 255, 255, 255),
                        BottomBounce = Color.FromArgb(42, 255, 255, 255),
                        RimTopLeft = Color.FromArgb(228, 255, 255, 255),
                        RimMid = Color.FromArgb(88, 255, 255, 255),
                        RimBottomRight = Color.FromArgb(162, 255, 255, 255),
                        DividerColor = Color.FromArgb(86, 255, 255, 255),
                        TextColor = Color.FromArgb(22, 28, 38),
                        TextShadow = false,
                        AccentColor = sysAccent,
                        IsLightMenu = true
                    };
            }
        }
    }

    public class FenceConfig
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public int OuterCol { get; set; }
        public int OuterRow { get; set; }
        public int OuterCols { get; set; }
        public int OuterRows { get; set; }
        public string ThemeId { get; set; }
        public double BackdropOpacity { get; set; } // 0.0 (off) or 0.5..1.5 (liquid glass glaze intensity)
        public double HeaderOpacity { get; set; }
        public List<string> MemberIconNames { get; set; }
        public List<string> MemberIconIds { get; set; }
        public Dictionary<string, string> MemberParsingNames { get; set; }

        public FenceConfig()
        {
            Id = Guid.NewGuid().ToString("N");
            Title = "新分区";
            OuterCol = 1;
            OuterRow = 0;
            OuterCols = 5;
            OuterRows = 4;
            ThemeId = "liquid_clear";
            BackdropOpacity = 1.0;
            HeaderOpacity = 1.0;
            MemberIconNames = new List<string>();
            MemberIconIds = new List<string>();
            MemberParsingNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsInOuterRect(int col, int row)
        {
            return col >= OuterCol && col < OuterCol + OuterCols &&
                   row >= OuterRow && row < OuterRow + OuterRows;
        }

        public bool IsInInnerRect(int col, int row)
        {
            return col >= OuterCol + 1 && col < OuterCol + OuterCols - 1 &&
                   row >= OuterRow + 1 && row < OuterRow + OuterRows - 1;
        }

        public bool IsOnBorderRing(int col, int row)
        {
            return IsInOuterRect(col, row) && !IsInInnerRect(col, row);
        }

        /// <summary>
        /// Grid-level overlap test between two zones.
        /// Adjacent zones may share at most their 1-cell reserved border ring (keeping a clean visual gutter),
        /// while overlapping by 2+ cells on both axes (meaning one zone's inner area touches another's outer area) is forbidden.
        /// </summary>
        public static bool GridRectsOverlap(
            int colA, int rowA, int colsA, int rowsA,
            int colB, int rowB, int colsB, int rowsB)
        {
            bool overlapX = (colA + 1 < colB + colsB) && (colB + 1 < colA + colsA);
            bool overlapY = (rowA + 1 < rowB + rowsB) && (rowB + 1 < rowA + rowsA);
            return overlapX && overlapY;
        }

        public bool OverlapsGrid(FenceConfig other)
        {
            if (other == null) return false;
            return GridRectsOverlap(OuterCol, OuterRow, OuterCols, OuterRows, other.OuterCol, other.OuterRow, other.OuterCols, other.OuterRows);
        }
    }

    public class AppConfig
    {
        private bool _loadedLegacyMembers;
        public List<FenceConfig> Fences { get; set; }
        public bool Locked { get; set; }

        public AppConfig()
        {
            Fences = new List<FenceConfig>();
            Locked = false;
        }

        public static string GetConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fences_config.json");
        }

        public static AppConfig Load() { return Load(GetConfigPath()); }

        internal static AppConfig Load(string path)
        {
            string json;
            try { json = File.ReadAllText(path, System.Text.Encoding.UTF8); }
            catch (FileNotFoundException) { return null; }
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var cfg = ser.Deserialize<AppConfig>(json);
                if (cfg == null) throw new InvalidDataException("配置文件为空。");
                if (cfg.Fences == null) cfg.Fences = new List<FenceConfig>();
                var claimedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in cfg.Fences)
                {
                    if (f.OuterCols < 3) f.OuterCols = 5;
                    if (f.OuterRows < 3) f.OuterRows = 4;
                    if (f.MemberIconNames == null) f.MemberIconNames = new List<string>();
                    if (f.MemberIconIds == null) f.MemberIconIds = new List<string>();
                    f.MemberParsingNames = f.MemberParsingNames == null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(f.MemberParsingNames, StringComparer.OrdinalIgnoreCase);
                    f.MemberIconIds.RemoveAll(id => string.IsNullOrWhiteSpace(id) || !claimedIds.Add(id));
                    f.MemberIconNames.RemoveAll(string.IsNullOrWhiteSpace);
                    if (f.MemberIconNames.Count > 0) cfg._loadedLegacyMembers = true;
                    if (string.IsNullOrEmpty(f.ThemeId) || !f.ThemeId.StartsWith("liquid_"))
                        f.ThemeId = "liquid_clear";
                    if (f.BackdropOpacity < 0.0 || f.BackdropOpacity > 1.6) f.BackdropOpacity = 1.0;
                    f.HeaderOpacity = 1.0;
                }
                return cfg;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("无法读取分区配置：" + path, ex);
            }
        }

        public void Save() { Save(GetConfigPath()); }

        internal void Save(string path)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string json = ser.Serialize(this);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path))
                {
                    string backup = Path.ChangeExtension(path, "legacy.json");
                    File.Replace(temp, path, _loadedLegacyMembers && !File.Exists(backup) ? backup : null);
                    _loadedLegacyMembers = false;
                }
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
