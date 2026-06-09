using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sort.EditorTools
{
    /// <summary>
    /// Dev tool: turn the Designer's level JSON into a Sort# <see cref="LevelData"/> asset that a Unity
    /// dev can then review and tweak. Open via <c>Sort ▸ Import Level JSON</c>, or right-click one or
    /// more .json assets in the Project window → <c>Sort ▸ Import JSON → LevelData</c>.
    ///
    /// JSON schema understood (see Assets/ImportLevels/Level_9.json):
    ///   name        → level number (trailing integer of the name) + output filename "Level{N}.asset"
    ///   config.moveLimit → LevelData.moveLimit   (nTubes/tH are implied by the stacks; nBuf is unsupported)
    ///   colors[]    → hex→name map (the tile colors reference these hexes; the NAME becomes the
    ///                 Sort# color identity, which must exist in the prefab's ColorPalette)
    ///   extraTile   → LevelData.startingHeldPiece  ("__RAINBOW__" → a Rainbow held piece)
    ///   stacks[][]  → one ColumnConfig per stack; each tile → a PieceConfig ("__RAINBOW__" → Rainbow)
    ///   mechs / stackProps.frozenLayers / tile.mech → NOT auto-applied (different model) — logged so
    ///                 the dev wires Break Wall / Lock Color / Only Stack Sort by hand.
    ///
    /// Colors are imported by NAME verbatim (data-driven color identity). After each import the tool
    /// checks the names against the chosen prefab's palette and lists any that are missing, so the dev
    /// knows exactly what to add/rename. Project has no Newtonsoft.Json, so a small self-contained
    /// parser (MiniJson) handles the nested stacks array.
    /// </summary>
    public class LevelJsonImporterWindow : EditorWindow
    {
        const string PrefPrefab   = "Sort.JsonImport.PrefabGuid";
        const string PrefStyle    = "Sort.JsonImport.Style";
        const string PrefOutput   = "Sort.JsonImport.OutputFolder";
        const string PrefTopDown  = "Sort.JsonImport.StacksTopToBottom";
        const string PrefOverwrite= "Sort.JsonImport.Overwrite";
        const string PrefPreserve = "Sort.JsonImport.PreserveDevFields";
        const string PrefAutoLink = "Sort.JsonImport.AutoLinkNext";
        const string DefaultOutputFolder = "Assets/Levels/Sort/LevelData";

        GameObject defaultPiecePrefab;
        PaletteStyle defaultPaletteStyle = PaletteStyle.Pastel;
        string outputFolder = DefaultOutputFolder;
        bool stacksTopToBottom = true;
        bool overwriteExisting = true;
        bool preserveDevFields = true;
        bool autoLinkNextLevel = false;

        Vector2 logScroll;
        readonly StringBuilder log = new StringBuilder();
        readonly List<LevelData> importedThisRun = new List<LevelData>();

        [MenuItem("Sort/Import Level JSON")]
        public static void Open() => GetWindow<LevelJsonImporterWindow>("Level JSON Import").minSize = new Vector2(420, 420);

        void OnEnable()
        {
            string guid = EditorPrefs.GetString(PrefPrefab, string.Empty);
            if (!string.IsNullOrEmpty(guid))
                defaultPiecePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            defaultPaletteStyle = (PaletteStyle)EditorPrefs.GetInt(PrefStyle, 0);
            outputFolder       = EditorPrefs.GetString(PrefOutput, DefaultOutputFolder);
            stacksTopToBottom  = EditorPrefs.GetBool(PrefTopDown, true);
            overwriteExisting  = EditorPrefs.GetBool(PrefOverwrite, true);
            preserveDevFields  = EditorPrefs.GetBool(PrefPreserve, true);
            autoLinkNextLevel  = EditorPrefs.GetBool(PrefAutoLink, false);
        }

        void SaveSettings()
        {
            EditorPrefs.SetString(PrefPrefab, defaultPiecePrefab != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(defaultPiecePrefab)) : string.Empty);
            EditorPrefs.SetInt(PrefStyle, (int)defaultPaletteStyle);
            EditorPrefs.SetString(PrefOutput, outputFolder);
            EditorPrefs.SetBool(PrefTopDown, stacksTopToBottom);
            EditorPrefs.SetBool(PrefOverwrite, overwriteExisting);
            EditorPrefs.SetBool(PrefPreserve, preserveDevFields);
            EditorPrefs.SetBool(PrefAutoLink, autoLinkNextLevel);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            defaultPiecePrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Default piece prefab", "Assigned to NEW levels (and used to validate color names). Must be registered in PrefabRegistry."),
                defaultPiecePrefab, typeof(GameObject), false);
            defaultPaletteStyle = (PaletteStyle)EditorGUILayout.EnumPopup(
                new GUIContent("Default palette style", "Pastel/Plain for NEW levels."), defaultPaletteStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Output folder", "Where Level{N}.asset files are written."));
                EditorGUILayout.SelectableLabel(outputFolder, EditorStyles.textField, GUILayout.Height(18));
                if (GUILayout.Button("…", GUILayout.Width(28))) PickOutputFolder();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            stacksTopToBottom = EditorGUILayout.ToggleLeft(
                new GUIContent("Stacks are top→bottom", "ON: JSON stack[0] = TOP piece (matches ColumnConfig). OFF: stack[0] = BOTTOM (the tool reverses)."),
                stacksTopToBottom);
            overwriteExisting = EditorGUILayout.ToggleLeft(
                new GUIContent("Overwrite existing LevelN.asset", "If a Level{N} asset already exists, update it; otherwise skip it."),
                overwriteExisting);
            using (new EditorGUI.DisabledScope(!overwriteExisting))
                preserveDevFields = EditorGUILayout.ToggleLeft(
                    new GUIContent("    Preserve dev fields on overwrite", "Keep prefab / palette style / sprites / nextLevel / rewards already set by the dev; only refresh columns, moveLimit, held piece, level number."),
                    preserveDevFields);
            autoLinkNextLevel = EditorGUILayout.ToggleLeft(
                new GUIContent("Auto-link nextLevel by number", "After import, set each imported level's nextLevel to the level numbered +1 (if it exists). Touches those neighbour assets."),
                autoLinkNextLevel);

            if (EditorGUI.EndChangeCheck()) SaveSettings();

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import single JSON…", GUILayout.Height(26))) ImportSingleFile();
                if (GUILayout.Button("Import all JSON in folder…", GUILayout.Height(26))) ImportFolder();
            }
            EditorGUILayout.HelpBox("Tip: drop the Designer's .json files under Assets/ImportLevels, select them in the " +
                                    "Project window, then right-click → Sort ▸ Import JSON → LevelData (handles many files at once).",
                                    MessageType.Info);

            // Drag-and-drop target for .json files (from disk) or TextAssets (from the project).
            var drop = GUILayoutUtility.GetRect(0, 46, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Drag .json file(s) here to import");
            HandleDragAndDrop(drop);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(120));
            EditorGUILayout.TextArea(log.Length == 0 ? "(import results appear here)" : log.ToString(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Clear log")) { log.Clear(); Repaint(); }
        }

        // ---- entry points -----------------------------------------------------

        void ImportSingleFile()
        {
            string picked = EditorUtility.OpenFilePanel("Pick a level JSON", Application.dataPath, "json");
            if (string.IsNullOrEmpty(picked)) return;
            BeginRun();
            ImportFromDisk(picked);
            EndRun();
        }

        void ImportFolder()
        {
            string dir = EditorUtility.OpenFolderPanel("Pick a folder of level JSON", Application.dataPath, "");
            if (string.IsNullOrEmpty(dir)) return;
            var files = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) { Log($"No .json files in {dir}"); return; }
            BeginRun();
            foreach (var f in files) ImportFromDisk(f);
            EndRun();
        }

        /// <summary>Called by the Project-window context menu with the selected objects.</summary>
        public void ImportTextAssets(UnityEngine.Object[] objs)
        {
            BeginRun();
            foreach (var o in objs)
            {
                if (o is TextAsset ta && AssetDatabase.GetAssetPath(ta).EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    ImportOne(ta.text, ta.name);
            }
            EndRun();
        }

        void HandleDragAndDrop(Rect area)
        {
            var e = Event.current;
            if (!area.Contains(e.mousePosition)) return;
            if (e.type == EventType.DragUpdated) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.Use(); }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                BeginRun();
                foreach (var o in DragAndDrop.objectReferences)
                    if (o is TextAsset ta && AssetDatabase.GetAssetPath(ta).EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        ImportOne(ta.text, ta.name);
                foreach (var p in DragAndDrop.paths)
                    if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        ImportFromDisk(p);
                EndRun();
                e.Use();
            }
        }

        void ImportFromDisk(string absOrRelPath)
        {
            try
            {
                string text = File.ReadAllText(absOrRelPath);
                ImportOne(text, Path.GetFileNameWithoutExtension(absOrRelPath));
            }
            catch (Exception ex) { Log($"✗ {Path.GetFileName(absOrRelPath)}: read error — {ex.Message}"); }
        }

        // ---- run lifecycle ----------------------------------------------------

        void BeginRun()
        {
            importedThisRun.Clear();
            log.Clear();
            EnsureFolder(outputFolder);
        }

        void EndRun()
        {
            if (autoLinkNextLevel && importedThisRun.Count > 0) AutoLinkNextLevels();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log($"— done: {importedThisRun.Count} level(s) written to {outputFolder} —");
            Repaint();
        }

        // ---- the actual import ------------------------------------------------

        void ImportOne(string jsonText, string sourceLabel)
        {
            object root;
            try { root = MiniJson.Parse(jsonText); }
            catch (Exception ex) { Log($"✗ {sourceLabel}: JSON parse error — {ex.Message}"); return; }

            if (!(root is Dictionary<string, object> obj)) { Log($"✗ {sourceLabel}: top-level JSON is not an object."); return; }

            string lvlName = GetString(obj, "name", sourceLabel);
            int levelNumber = ParseTrailingInt(lvlName, ParseTrailingInt(sourceLabel, 0));
            if (levelNumber <= 0) Log($"⚠ {sourceLabel}: couldn't read a level number from '{lvlName}' — using {levelNumber}; rename if wrong.");

            var config = Get(obj, "config") as Dictionary<string, object>;
            int moveLimit = (int)GetDouble(config, "moveLimit", 20);
            int nBuf = (int)GetDouble(config, "nBuf", 0);

            var hexToName = BuildHexMap(obj, sourceLabel);

            var extra = Get(obj, "extraTile") as Dictionary<string, object>;
            PieceConfig held = extra != null ? ToPiece(extra, hexToName, sourceLabel) : new PieceConfig { color = "" };

            var stacks = Get(obj, "stacks") as List<object>;
            if (stacks == null || stacks.Count == 0) { Log($"✗ {sourceLabel}: no 'stacks' array — nothing to build."); return; }

            var columns = new ColumnConfig[stacks.Count];
            for (int ci = 0; ci < stacks.Count; ci++)
            {
                var tiles = new List<PieceConfig>();
                if (stacks[ci] is List<object> stack)
                    foreach (var t in stack)
                        if (t is Dictionary<string, object> td) tiles.Add(ToPiece(td, hexToName, sourceLabel));
                if (!stacksTopToBottom) tiles.Reverse();
                columns[ci] = new ColumnConfig { pieces = tiles.ToArray() };
            }

            // Features Sort# can't auto-derive — surface them so the dev configures by hand.
            if (Get(obj, "mechs") is List<object> mechs && mechs.Count > 0)
                Log($"⚠ {sourceLabel}: {mechs.Count} level 'mech(s)' present — not auto-applied; set special columns manually.");
            if (Get(obj, "stackProps") is List<object> props)
                for (int i = 0; i < props.Count; i++)
                    if (props[i] is Dictionary<string, object> sp && (int)GetDouble(sp, "frozenLayers", 0) > 0)
                        Log($"⚠ {sourceLabel}: stack {i} frozenLayers>0 — Sort# Break Wall / Lock Color use a different model; configure manually.");
            if (nBuf > 0) Log($"⚠ {sourceLabel}: nBuf={nBuf} (buffer tubes) — Sort# has no buffer tubes; ignored.");

            // Write / update the asset.
            string path = $"{outputFolder.TrimEnd('/')}/Level{levelNumber}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            bool isNew = existing == null;
            if (!isNew && !overwriteExisting) { Log($"• {sourceLabel}: '{path}' exists and Overwrite is OFF — skipped."); return; }

            LevelData lvl = isNew ? CreateInstance<LevelData>() : existing;
            lvl.levelNumber = levelNumber;
            lvl.moveLimit = moveLimit;
            lvl.startingHeldPiece = held;
            lvl.columns = columns;
            if (isNew || !preserveDevFields)
            {
                if (defaultPiecePrefab != null) lvl.piecePrefab = defaultPiecePrefab;
                lvl.paletteStyle = defaultPaletteStyle;
            }

            if (isNew) AssetDatabase.CreateAsset(lvl, path);
            else EditorUtility.SetDirty(lvl);
            importedThisRun.Add(lvl);

            Log($"{(isNew ? "✓ created" : "✓ updated")} {path}  (cols={columns.Length}, moveLimit={moveLimit}, held={(held.isRainbow ? "Rainbow" : (string.IsNullOrEmpty(held.color) ? "<unset>" : held.color))})");

            var vr = lvl.Validate();
            if (!vr.IsValid) foreach (var err in vr.errors) Log($"    ⚠ validate: {err}");
            CheckPaletteNames(lvl, sourceLabel);
        }

        PieceConfig ToPiece(Dictionary<string, object> tile, Dictionary<string, string> hexToName, string src)
        {
            var pc = new PieceConfig();
            string raw = GetString(tile, "color", string.Empty);
            if (raw == "__RAINBOW__")
            {
                pc.isRainbow = true;
                pc.color = string.Empty;
            }
            else
            {
                string key = NormalizeHex(raw);
                pc.color = hexToName.TryGetValue(key, out var nm) ? nm : raw; // raw fallback fails palette check → dev fixes
            }

            if (Get(tile, "mech") is string ms && !string.IsNullOrEmpty(ms))
            {
                string m = ms.ToLowerInvariant();
                if (m.Contains("quest") || m.Contains("hidden") || m == "?") pc.isQuestionmark = true;
                else Log($"⚠ {src}: unhandled tile mech '{ms}' — left as a plain piece.");
            }
            return pc;
        }

        Dictionary<string, string> BuildHexMap(Dictionary<string, object> obj, string src)
        {
            var map = new Dictionary<string, string>();
            if (!(Get(obj, "colors") is List<object> colors)) return map;
            foreach (var c in colors)
            {
                if (!(c is Dictionary<string, object> cd)) continue;
                string hex = NormalizeHex(GetString(cd, "hex", string.Empty));
                string nm = GetString(cd, "name", string.Empty);
                if (string.IsNullOrEmpty(hex) || string.IsNullOrEmpty(nm)) continue;
                if (!map.ContainsKey(hex)) map[hex] = nm;
                else if (map[hex] != nm) Log($"⚠ {src}: hex {hex} maps to both '{map[hex]}' and '{nm}' — keeping '{map[hex]}'.");
            }
            return map;
        }

        void CheckPaletteNames(LevelData lvl, string src)
        {
            if (lvl.piecePrefab == null)
            {
                Log($"⚠ {src}: no piecePrefab assigned — set a 'Default piece prefab' so colors can resolve & render.");
                return;
            }
            var names = ResolvePaletteNames(lvl.piecePrefab);
            if (names == null || names.Count == 0)
            {
                Log($"⚠ {src}: prefab '{lvl.piecePrefab.name}' has no palette names in PrefabRegistry — colors won't render.");
                return;
            }
            var missing = new HashSet<string>();
            void Check(PieceConfig p)
            {
                if (p == null || p.isRainbow || string.IsNullOrEmpty(p.color)) return;
                if (!names.Contains(p.color)) missing.Add(p.color);
            }
            Check(lvl.startingHeldPiece);
            if (lvl.columns != null)
                foreach (var col in lvl.columns)
                    if (col?.pieces != null)
                        foreach (var p in col.pieces) Check(p);

            if (missing.Count > 0)
                Log($"    ⚠ {missing.Count} color name(s) NOT in '{lvl.piecePrefab.name}' palette: [{string.Join(", ", missing)}] — add them to the ColorPalette (name+texture) or remap.");
        }

        void AutoLinkNextLevels()
        {
            var byNumber = new Dictionary<int, LevelData>();
            foreach (var guid in AssetDatabase.FindAssets("t:LevelData"))
            {
                var l = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (l != null) byNumber[l.levelNumber] = l;
            }
            foreach (var l in importedThisRun)
                if (byNumber.TryGetValue(l.levelNumber + 1, out var nxt) && l.nextLevel != nxt)
                {
                    l.nextLevel = nxt;
                    EditorUtility.SetDirty(l);
                    Log($"    ↳ linked Level{l.levelNumber}.nextLevel → Level{nxt.levelNumber}");
                }
        }

        // ---- helpers ----------------------------------------------------------

        void PickOutputFolder()
        {
            string dir = EditorUtility.OpenFolderPanel("Output folder for LevelData assets", Application.dataPath, "");
            if (string.IsNullOrEmpty(dir)) return;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (dir.StartsWith(projectRoot)) dir = dir.Substring(projectRoot.Length + 1).Replace('\\', '/');
            if (!dir.StartsWith("Assets")) { Log("Output folder must be inside the project's Assets/ folder."); return; }
            outputFolder = dir;
            SaveSettings();
        }

        static HashSet<string> ResolvePaletteNames(GameObject prefab)
        {
            PrefabRegistry registry = null;
            var rGuids = AssetDatabase.FindAssets("t:" + nameof(PrefabRegistry));
            if (rGuids.Length > 0) registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(AssetDatabase.GUIDToAssetPath(rGuids[0]));
            if (registry == null || !registry.TryGetEntry(prefab, out var entry)) return null;

            var names = new HashSet<string>();
            void Add(ColorPalette p) { if (p != null) foreach (var n in p.ColorNames()) names.Add(n); }
            Add(entry.palettePastel);
            Add(entry.palettePlain);
            return names;
        }

        static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            string cur = parts[0]; // expected "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        static string NormalizeHex(string h)
        {
            if (string.IsNullOrEmpty(h)) return string.Empty;
            h = h.Trim().ToLowerInvariant();
            if (!h.StartsWith("#")) h = "#" + h;
            return h;
        }

        static int ParseTrailingInt(string s, int fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            int end = s.Length, start = end;
            while (start > 0 && char.IsDigit(s[start - 1])) start--;
            if (start == end) return fallback;
            return int.TryParse(s.Substring(start, end - start), out int v) ? v : fallback;
        }

        static object Get(Dictionary<string, object> d, string k) => (d != null && d.TryGetValue(k, out var v)) ? v : null;
        static string GetString(Dictionary<string, object> d, string k, string def) => Get(d, k) as string ?? def;
        static double GetDouble(Dictionary<string, object> d, string k, double def) => Get(d, k) is double dd ? dd : def;

        void Log(string line)
        {
            log.AppendLine(line);
            Debug.Log($"[LevelJsonImport] {line}");
        }

        // ---- Project-window context menu (handles multi-selection of .json) ---

        [MenuItem("Assets/Sort/Import JSON → LevelData", true)]
        static bool ValidateImportSelected()
        {
            foreach (var o in Selection.objects)
                if (o is TextAsset ta && AssetDatabase.GetAssetPath(ta).EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        [MenuItem("Assets/Sort/Import JSON → LevelData")]
        static void ImportSelectedFromContextMenu()
        {
            var win = GetWindow<LevelJsonImporterWindow>("Level JSON Import");
            win.ImportTextAssets(Selection.objects);
        }
    }

    /// <summary>
    /// Tiny dependency-free JSON parser (the project has no Newtonsoft). Returns a plain object graph:
    /// <see cref="Dictionary{TKey,TValue}"/>(string,object) for objects, <see cref="List{T}"/>(object)
    /// for arrays, <see cref="string"/>, <see cref="double"/>, <see cref="bool"/>, or null. Handles the
    /// nested <c>stacks</c> array that Unity's JsonUtility can't. Editor-only, so robustness over speed.
    /// </summary>
    static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = 0;
            object v = ParseValue(json, ref i);
            SkipWs(json, ref i);
            return v;
        }

        static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new Exception("Unexpected end of JSON.");
            switch (s[i])
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': ParseLiteral(s, ref i, "true"); return true;
                case 'f': ParseLiteral(s, ref i, "false"); return false;
                case 'n': ParseLiteral(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>();
            i++; // '{'
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) throw new Exception("Unterminated object.");
                if (s[i] == '}') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new Exception("Expected ':' in object.");
                i++; // ':'
                dict[key] = ParseValue(s, ref i);
            }
            return dict;
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // '['
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) throw new Exception("Unterminated array.");
                if (s[i] == ']') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                list.Add(ParseValue(s, ref i));
            }
            return list;
        }

        static string ParseString(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"') throw new Exception("Expected a string.");
            i++; // opening quote
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= s.Length) break;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= s.Length &&
                                int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            { sb.Append((char)code); i += 4; }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            throw new Exception("Unterminated string.");
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string num = s.Substring(start, i - start);
            if (double.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;
            throw new Exception($"Invalid number '{num}'.");
        }

        static void ParseLiteral(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || s.Substring(i, literal.Length) != literal)
                throw new Exception($"Expected '{literal}'.");
            i += literal.Length;
        }
    }
}
