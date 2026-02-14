using System.Text.RegularExpressions;
using ZeroHourStudio.Infrastructure.Archives;
using ZeroHourStudio.Infrastructure.Logging;

namespace ZeroHourStudio.Infrastructure.Services;

// ════════════════════════════════════════════════════════
//  نماذج الخرج — شريط أوامر المبنى
// ════════════════════════════════════════════════════════

/// <summary>
/// نتيجة شريط أوامر مبنى واحد (مثل War Factory)
/// </summary>
public class CommandBarResult
{
    public string ObjectName { get; set; } = string.Empty;
    public string CommandSetName { get; set; } = string.Empty;
    public List<CommandBarSlot> Slots { get; set; } = new();

    public int TotalSlots => Slots.Count;
    public int OccupiedSlots => Slots.Count(s => s.IsOccupied);
    public int EmptySlots => Slots.Count(s => !s.IsOccupied);

    /// <summary>أول خانة فارغة أو null</summary>
    public CommandBarSlot? FirstEmptySlot => Slots.FirstOrDefault(s => !s.IsOccupied);
}

/// <summary>
/// خانة واحدة في شريط الأوامر — مع كل المعلومات العلائقية
/// </summary>
public class CommandBarSlot
{
    public int SlotNumber { get; set; }
    public bool IsOccupied { get; set; }

    // Step 2: من CommandSet
    public string? ButtonName { get; set; }       // مثل Command_ConstructDragonTank

    // Step 3: من CommandButton
    public string? ButtonImage { get; set; }      // مثل SNDragonTank
    public string? Command { get; set; }          // مثل DO_PRODUCE
    public string? UnitObject { get; set; }       // مثل ChinaDragonTank (Object المرتبط)
    public string? Label { get; set; }            // اسم التسمية (يُحل من CSF لاحقاً)

    // Step 4: من MappedImages
    public string? TexturePath { get; set; }      // مسار TGA/DDS الفعلي
    public int TextureLeft { get; set; }
    public int TextureTop { get; set; }
    public int TextureRight { get; set; }
    public int TextureBottom { get; set; }
}

// ════════════════════════════════════════════════════════
//  CommandChainService — الدماغ العلائقي
// ════════════════════════════════════════════════════════

/// <summary>
/// محرك بيانات علائقي لـ SAGE — يربط سلسلة:
/// Object → CommandSet → CommandButton → ButtonImage → MappedImage
/// 
/// بدلاً من "التجميع الأعمى" بالفصيل، يعطيك شريط أوامر **مبنى محدد** بكل تفاصيله.
/// </summary>
public class CommandChainService
{
    // ── الفهارس المبنية ──
    private readonly Dictionary<string, string> _objectToCommandSet
        = new(StringComparer.OrdinalIgnoreCase);                          // Object → CommandSet name

    private readonly Dictionary<string, Dictionary<int, string>> _commandSetSlots
        = new(StringComparer.OrdinalIgnoreCase);                          // SetName → {SlotNum → ButtonName}

    private readonly Dictionary<string, Dictionary<string, string>> _commandButtons
        = new(StringComparer.OrdinalIgnoreCase);                          // ButtonName → {Key → Value}

    // ── Property-Driven: Side + Production ──
    private readonly Dictionary<string, string> _objectSides
        = new(StringComparer.OrdinalIgnoreCase);                          // Object → Side (faction)

    private readonly HashSet<string> _productionObjects
        = new(StringComparer.OrdinalIgnoreCase);                          // Objects with ProductionUpdate

    // ── المكونات الخارجية ──
    private MappedImageIndex? _mappedImageIndex;

    // ── Regex مترجمة ──
    private static readonly Regex CommandSetHeaderRx =
        new(@"^\s*CommandSet\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CommandButtonHeaderRx =
        new(@"^\s*CommandButton\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SlotRx =
        new(@"^\s*(\d+)\s*=\s*(\S+)", RegexOptions.Compiled);

    private static readonly Regex KeyValueRx =
        new(@"^\s*(\w+)\s*=\s*(.+)$", RegexOptions.Compiled);

    private static readonly Regex ObjectHeaderRx =
        new(@"^\s*Object\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CommandSetAssignRx =
        new(@"^\s*CommandSet\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SideAssignRx =
        new(@"^\s*Side\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProductionBehaviorRx =
        new(@"Behavior\s*=\s*ProductionUpdate", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // حالة البناء
    public bool IsBuilt { get; private set; }
    public int ObjectCount => _objectToCommandSet.Count;
    public int CommandSetCount => _commandSetSlots.Count;
    public int CommandButtonCount => _commandButtons.Count;

    // ════════════════════════════════════════════════════
    //  الخطوة 1: بناء الفهارس
    // ════════════════════════════════════════════════════

    /// <summary>
    /// بناء جميع الفهارس من ملفات المود (INI + أرشيفات BIG)
    /// </summary>
    public async Task BuildIndexAsync(string modPath, MappedImageIndex? mappedImageIndex = null)
    {
        _objectToCommandSet.Clear();
        _commandSetSlots.Clear();
        _commandButtons.Clear();
        _mappedImageIndex = mappedImageIndex;
        IsBuilt = false;

        try
        {
            // Phase 1: CommandSet.ini → فهرس الخانات
            await IndexCommandSetsAsync(modPath);

            // Phase 2: CommandButton.ini → فهرس الأزرار
            await IndexCommandButtonsAsync(modPath);

            // Phase 3: Object INI files → ربط Object بـ CommandSet
            await IndexObjectCommandSetsAsync(modPath);

            // Phase 4: MappedImages (إذا لم يُمرر من الخارج)
            if (_mappedImageIndex == null)
            {
                _mappedImageIndex = new MappedImageIndex();
                await _mappedImageIndex.BuildIndexAsync(modPath);
            }

            IsBuilt = true;
            BlackBoxRecorder.Record("COMMAND_CHAIN", "INDEX_BUILT",
                $"Objects={ObjectCount}, Sets={CommandSetCount}, Buttons={CommandButtonCount}, Images={_mappedImageIndex.Count}");
        }
        catch (Exception ex)
        {
            BlackBoxRecorder.Record("COMMAND_CHAIN", "INDEX_ERROR", ex.Message);
        }
    }

    // ════════════════════════════════════════════════════
    //  الخطوة 2: الاستعلام — شريط أوامر مبنى محدد
    // ════════════════════════════════════════════════════

    /// <summary>
    /// أعطني شريط الأوامر الكامل لمبنى محدد مع كل التفاصيل العلائقية
    /// </summary>
    public CommandBarResult GetBuildingCommandBar(string objectName)
    {
        var result = new CommandBarResult { ObjectName = objectName };

        // Step 1: Object → CommandSet name
        if (!_objectToCommandSet.TryGetValue(objectName, out var commandSetName))
        {
            // لا يوجد CommandSet لهذا الـ Object → 12 خانة فارغة
            result.CommandSetName = "UNKNOWN";
            FillEmptySlots(result, 12);
            return result;
        }

        result.CommandSetName = commandSetName;

        // Step 2: CommandSet → Slots {num → buttonName}
        if (!_commandSetSlots.TryGetValue(commandSetName, out var slots))
        {
            FillEmptySlots(result, 12);
            return result;
        }

        // بناء 12 خانة (أو 14 إذا موجودة)
        var maxSlot = slots.Count > 0 ? Math.Max(slots.Keys.Max(), 12) : 12;
        maxSlot = Math.Min(maxSlot, 18); // حد أقصى معقول

        for (int i = 1; i <= maxSlot; i++)
        {
            var slot = new CommandBarSlot { SlotNumber = i };

            if (slots.TryGetValue(i, out var buttonName) &&
                !string.IsNullOrWhiteSpace(buttonName) &&
                !buttonName.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                slot.IsOccupied = true;
                slot.ButtonName = buttonName;

                // Step 3: CommandButton → ButtonImage, Cmd, Object
                ResolveButtonDetails(slot, buttonName);

                // Step 4: ButtonImage → MappedImage → Texture
                ResolveImageDetails(slot);
            }

            result.Slots.Add(slot);
        }

        return result;
    }

    /// <summary>
    /// البحث عن أول خانة فارغة في مبنى محدد (ليس الفصيل!)
    /// </summary>
    public (bool hasSpace, int slotNumber, string message) FindEmptySlotInBuilding(string objectName)
    {
        var bar = GetBuildingCommandBar(objectName);
        var empty = bar.FirstEmptySlot;

        if (empty != null)
            return (true, empty.SlotNumber,
                $"✓ الخانة {empty.SlotNumber} متاحة في {objectName} ({bar.CommandSetName})");

        return (false, -1,
            $"✗ لا توجد خانات فارغة في {objectName} ({bar.OccupiedSlots}/{bar.TotalSlots} مشغولة)");
    }

    /// <summary>
    /// جميع المباني التي فيها CommandSet (للعرض في UI)
    /// </summary>
    public IReadOnlyList<string> GetAllBuildingObjects()
    {
        return _objectToCommandSet.Keys.OrderBy(k => k).ToList();
    }

    /// <summary>
    /// البحث عن المبنى الذي يستخدم CommandSet محدد
    /// </summary>
    public string? FindObjectByCommandSet(string commandSetName)
    {
        return _objectToCommandSet
            .FirstOrDefault(kvp => kvp.Value.Equals(commandSetName, StringComparison.OrdinalIgnoreCase))
            .Key;
    }

    /// <summary>
    /// اكتشاف مباني الإنتاج للفصيل — Hybrid 3-Layer:
    /// 1) Side property (إذا وُجد في Object INI)
    /// 2) Dynamic CamelCase name matching + ProductionUpdate behavior
    /// 3) ALL production objects (fallback نهائي)
    /// يعمل مع أي فصيل مخصص بلا hardcoding.
    /// </summary>
    public List<(string ObjectName, string CommandSetName)> GetFactionProductionBuildings(string factionName)
    {
        System.Diagnostics.Debug.WriteLine(
            $"\n[GetFactionBuildings] Faction='{factionName}'");
        System.Diagnostics.Debug.WriteLine(
            $"[GetFactionBuildings] Index: Objects={_objectToCommandSet.Count}, " +
            $"Sides={_objectSides.Count}, ProductionObjects={_productionObjects.Count}");

        // ═══ Layer 1: Side property (EXACT match) ═══
        if (_objectSides.Count > 0)
        {
            var sideResults = FindBySide(factionName);
            if (sideResults.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GetFactionBuildings] ✓ Layer 1 (Side): {sideResults.Count} buildings");
                return sideResults.OrderBy(r => r.Item1).ToList();
            }
        }

        // ═══ Layer 2: Dynamic CamelCase name matching + ProductionUpdate ═══
        var namePrefixes = ExtractCamelCasePrefixes(factionName);
        System.Diagnostics.Debug.WriteLine(
            $"[GetFactionBuildings] Layer 2 prefixes: [{string.Join(", ", namePrefixes)}]");

        var nameResults = FindByNameAndBehavior(namePrefixes);
        if (nameResults.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[GetFactionBuildings] ✓ Layer 2 (Name+Behavior): {nameResults.Count} buildings");
            foreach (var (obj, cs) in nameResults)
                System.Diagnostics.Debug.WriteLine($"  → {obj} → {cs}");
            return nameResults.OrderBy(r => r.Item1).ToList();
        }

        // ═══ Layer 3: ALL production objects ═══
        System.Diagnostics.Debug.WriteLine(
            $"[GetFactionBuildings] ⚠ Layer 1+2 empty. Layer 3: ALL production objects");
        var allResults = GetAllProductionBuildings();
        System.Diagnostics.Debug.WriteLine(
            $"[GetFactionBuildings] Layer 3: {allResults.Count} total production objects");
        return allResults;
    }

    /// <summary>Layer 1: فلتر بـ Side property</summary>
    private List<(string, string)> FindBySide(string factionName)
    {
        var results = new List<(string, string)>();
        foreach (var (obj, cs) in _objectToCommandSet)
        {
            if (!_objectSides.TryGetValue(obj, out var side)) continue;
            if (!side.Equals(factionName, StringComparison.OrdinalIgnoreCase)) continue;

            if (_productionObjects.Contains(obj) || HasDoProduceButtons(cs))
            {
                results.Add((obj, cs));
                System.Diagnostics.Debug.WriteLine(
                    $"[FindBySide] ✓ {obj} (Side={side}) → {cs}");
            }
        }
        return results;
    }

    /// <summary>Layer 2: مطابقة بالاسم (StartsWith) + سلوك إنتاجي</summary>
    private List<(string, string)> FindByNameAndBehavior(List<string> prefixes)
    {
        var results = new List<(string, string)>();
        foreach (var (obj, cs) in _objectToCommandSet)
        {
            // يجب أن يبدأ الاسم بأحد البادئات (SAGE convention: FactionPrefix + BuildingType)
            if (!prefixes.Any(p => obj.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            // يجب أن يكون مبنى إنتاجي (ProductionUpdate أو DO_PRODUCE)
            if (_productionObjects.Contains(obj))
            {
                results.Add((obj, cs));
            }
            else if (HasDoProduceButtons(cs))
            {
                results.Add((obj, cs));
            }
        }
        return results;
    }

    /// <summary>هل CommandSet يحتوي أزرار DO_PRODUCE؟</summary>
    private bool HasDoProduceButtons(string commandSetName)
    {
        if (!_commandSetSlots.TryGetValue(commandSetName, out var slots))
            return false;

        return slots.Values.Any(btn =>
        {
            if (_commandButtons.TryGetValue(btn, out var props))
            {
                return props.TryGetValue("Command", out var cmd) &&
                       cmd.Equals("DO_PRODUCE", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        });
    }

    /// <summary>
    /// استخراج بادئات ديناميكية من CamelCase بلا hardcoding:
    /// "ChinaTankGeneral" → ["ChinaTankGeneral", "ChinaTank", "China"]
    /// "GLAToxinGeneral" → ["GLAToxinGeneral", "GLAToxin", "GLA"]
    /// "America" → ["America"]
    /// </summary>
    internal static List<string> ExtractCamelCasePrefixes(string factionName)
    {
        if (string.IsNullOrWhiteSpace(factionName))
            return new List<string>();

        var prefixes = new List<string> { factionName };

        // تقسيم عند حدود CamelCase
        var splits = System.Text.RegularExpressions.Regex.Split(factionName, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])");

        // بناء بادئات تراكمية: ["China", "ChinaTank", "ChinaTankGeneral"]
        if (splits.Length > 1)
        {
            var cumulative = "";
            for (int i = 0; i < splits.Length - 1; i++)
            {
                cumulative += splits[i];
                if (cumulative.Length >= 3 && !prefixes.Contains(cumulative, StringComparer.OrdinalIgnoreCase))
                    prefixes.Add(cumulative);
            }
        }

        // ترتيب: الأطول أولاً (أكثر تحديداً)
        return prefixes.OrderByDescending(p => p.Length).ToList();
    }

    /// <summary>
    /// ALL production buildings (any faction) — behavioral check
    /// </summary>
    public List<(string ObjectName, string CommandSetName)> GetAllProductionBuildings()
    {
        var results = new List<(string, string)>();

        foreach (var (obj, cs) in _objectToCommandSet)
        {
            if (_productionObjects.Contains(obj) || HasDoProduceButtons(cs))
            {
                results.Add((obj, cs));
            }
        }

        return results.OrderBy(r => r.Item1).ToList();
    }

    // ════════════════════════════════════════════════════
    //  فهرسة CommandSet.ini
    // ════════════════════════════════════════════════════

    private async Task IndexCommandSetsAsync(string modPath)
    {
        var paths = GetIniPaths(modPath, "CommandSet.ini");

        foreach (var path in paths)
        {
            try
            {
                var content = await File.ReadAllTextAsync(path,
                    System.Text.Encoding.GetEncoding(1252));
                ParseCommandSets(content);
            }
            catch (Exception ex)
            {
                BlackBoxRecorder.Record("COMMAND_CHAIN", "CMDSET_READ_ERROR",
                    $"File={path}, Error={ex.Message}");
            }
        }

        // أيضاً من أرشيفات BIG
        await IndexFromArchivesAsync(modPath, "CommandSet", ParseCommandSets);
    }

    private void ParseCommandSets(string content)
    {
        var lines = content.Split('\n');
        string? currentSetName = null;
        var currentSlots = new Dictionary<int, string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("//"))
                continue;

            // بداية CommandSet جديد
            var headerMatch = CommandSetHeaderRx.Match(line);
            if (headerMatch.Success)
            {
                // حفظ السابق
                FlushCommandSet(currentSetName, currentSlots);

                currentSetName = headerMatch.Groups[1].Value;
                currentSlots = new Dictionary<int, string>();
                continue;
            }

            // نهاية البلوك
            if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                FlushCommandSet(currentSetName, currentSlots);
                currentSetName = null;
                continue;
            }

            // خانة: 1 = Command_ConstructSomething
            if (currentSetName != null)
            {
                var slotMatch = SlotRx.Match(line);
                if (slotMatch.Success && int.TryParse(slotMatch.Groups[1].Value, out var slotNum))
                {
                    currentSlots[slotNum] = slotMatch.Groups[2].Value.Trim();
                }
            }
        }

        FlushCommandSet(currentSetName, currentSlots);
    }

    private void FlushCommandSet(string? name, Dictionary<int, string> slots)
    {
        if (string.IsNullOrWhiteSpace(name) || slots.Count == 0) return;
        _commandSetSlots[name] = new Dictionary<int, string>(slots);
    }

    // ════════════════════════════════════════════════════
    //  فهرسة CommandButton.ini
    // ════════════════════════════════════════════════════

    private async Task IndexCommandButtonsAsync(string modPath)
    {
        var paths = GetIniPaths(modPath, "CommandButton.ini");

        foreach (var path in paths)
        {
            try
            {
                var content = await File.ReadAllTextAsync(path,
                    System.Text.Encoding.GetEncoding(1252));
                ParseCommandButtons(content);
            }
            catch (Exception ex)
            {
                BlackBoxRecorder.Record("COMMAND_CHAIN", "CMDBTN_READ_ERROR",
                    $"File={path}, Error={ex.Message}");
            }
        }

        await IndexFromArchivesAsync(modPath, "CommandButton", ParseCommandButtons);
    }

    private void ParseCommandButtons(string content)
    {
        var lines = content.Split('\n');
        string? currentButtonName = null;
        Dictionary<string, string>? currentProps = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("//"))
                continue;

            var headerMatch = CommandButtonHeaderRx.Match(line);
            if (headerMatch.Success)
            {
                FlushCommandButton(currentButtonName, currentProps);
                currentButtonName = headerMatch.Groups[1].Value;
                currentProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                FlushCommandButton(currentButtonName, currentProps);
                currentButtonName = null;
                currentProps = null;
                continue;
            }

            if (currentProps != null)
            {
                var kvMatch = KeyValueRx.Match(line);
                if (kvMatch.Success)
                {
                    currentProps[kvMatch.Groups[1].Value] = kvMatch.Groups[2].Value.Trim();
                }
            }
        }

        FlushCommandButton(currentButtonName, currentProps);
    }

    private void FlushCommandButton(string? name, Dictionary<string, string>? props)
    {
        if (string.IsNullOrWhiteSpace(name) || props == null || props.Count == 0) return;
        _commandButtons[name] = new Dictionary<string, string>(props, StringComparer.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════
    //  فهرسة Object → CommandSet
    // ════════════════════════════════════════════════════

    private async Task IndexObjectCommandSetsAsync(string modPath)
    {
        // قراءة من Object INI files (المجلدات الشائعة)
        var dirs = new[]
        {
            Path.Combine(modPath, "Data", "INI", "Object"),
            Path.Combine(modPath, "Data", "INI"),
            Path.Combine(modPath, "INI", "Object"),
            Path.Combine(modPath, "INI"),
        };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.GetFiles(dir, "*.ini", SearchOption.AllDirectories))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file,
                        System.Text.Encoding.GetEncoding(1252));
                    ParseObjectCommandSets(content);
                }
                catch { /* تجاهل الملفات التالفة */ }
            }
        }

        // من الأرشيفات
        await IndexFromArchivesAsync(modPath, "Object", ParseObjectCommandSets);
    }

    private void ParseObjectCommandSets(string content)
    {
        var lines = content.Split('\n');
        string? currentObject = null;
        int depth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("//"))
                continue;

            // بداية Object
            if (depth == 0)
            {
                var objMatch = ObjectHeaderRx.Match(line);
                if (objMatch.Success)
                {
                    currentObject = objMatch.Groups[1].Value;
                    depth = 1;
                    continue;
                }
            }

            if (currentObject != null)
            {
                // تعقب العمق (sub-blocks مثل Body, Draw)
                if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
                {
                    depth--;
                    if (depth <= 0)
                    {
                        currentObject = null;
                        depth = 0;
                    }
                    continue;
                }

                if (depth == 1)
                {
                    // CommandSet = SetName (المستوى الأول فقط)
                    var csMatch = CommandSetAssignRx.Match(line);
                    if (csMatch.Success)
                    {
                        _objectToCommandSet[currentObject] = csMatch.Groups[1].Value;
                    }

                    // Side = FactionName (المستوى الأول)
                    var sideMatch = SideAssignRx.Match(line);
                    if (sideMatch.Success)
                    {
                        _objectSides[currentObject] = sideMatch.Groups[1].Value;
                    }
                }

                // ProductionUpdate — أي مستوى عمق (يمكن أن يكون داخل Body أو Behavior)
                if (ProductionBehaviorRx.IsMatch(line))
                {
                    _productionObjects.Add(currentObject);
                }

                // كشف بلوكات فرعية تزيد العمق
                if (IsSubBlockStart(line))
                {
                    depth++;
                }
            }
        }
    }

    private static bool IsSubBlockStart(string line)
    {
        // بلوكات SAGE الفرعية: Draw = ..., Body = ..., Behavior = ..., etc.
        // تبدأ بكلمة ثم = ثم نوع ثم اسم أو ModuleTag
        if (line.Contains('=') && !line.StartsWith("CommandSet", StringComparison.OrdinalIgnoreCase))
        {
            var parts = line.Split('=', 2);
            var right = parts[1].Trim();
            // إذا الجزء الأيمن يحتوي ModuleTag أو كلمتين+ → بلوك فرعي
            if (right.Contains("ModuleTag", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // أنماط بدون = مثل: DefaultConditionState, ConditionState MOVING
        var blockKeywords = new[] { "DefaultConditionState", "ConditionState", "TransitionState",
            "AnimationState", "ModelConditionState" };
        return blockKeywords.Any(kw => line.StartsWith(kw, StringComparison.OrdinalIgnoreCase));
    }

    // ════════════════════════════════════════════════════
    //  حل التفاصيل العلائقية
    // ════════════════════════════════════════════════════

    private void ResolveButtonDetails(CommandBarSlot slot, string buttonName)
    {
        if (!_commandButtons.TryGetValue(buttonName, out var props))
            return;

        props.TryGetValue("ButtonImage", out var buttonImage);
        props.TryGetValue("Command", out var command);
        props.TryGetValue("Object", out var unitObject);
        props.TryGetValue("TextLabel", out var label);

        slot.ButtonImage = buttonImage;
        slot.Command = command;
        slot.UnitObject = unitObject;
        slot.Label = label;
    }

    private void ResolveImageDetails(CommandBarSlot slot)
    {
        if (string.IsNullOrWhiteSpace(slot.ButtonImage) || _mappedImageIndex == null)
            return;

        var entry = _mappedImageIndex.Find(slot.ButtonImage);
        if (entry == null) return;

        slot.TexturePath = entry.TextureFile;
        slot.TextureLeft = entry.Left;
        slot.TextureTop = entry.Top;
        slot.TextureRight = entry.Right;
        slot.TextureBottom = entry.Bottom;
    }

    // ════════════════════════════════════════════════════
    //  أدوات مساعدة
    // ════════════════════════════════════════════════════

    private static List<string> GetIniPaths(string modPath, string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(modPath, "Data", "INI", fileName),
            Path.Combine(modPath, "Data", "INI", fileName.ToLowerInvariant()),
            Path.Combine(modPath, "INI", fileName),
            Path.Combine(modPath, fileName),
        };

        return candidates.Where(File.Exists).Distinct().ToList();
    }

    private async Task IndexFromArchivesAsync(string modPath, string filterKeyword, Action<string> parser)
    {
        if (!Directory.Exists(modPath)) return;

        var bigFiles = Directory.GetFiles(modPath, "*.big", SearchOption.TopDirectoryOnly);
        foreach (var bigPath in bigFiles)
        {
            try
            {
                using var manager = new BigArchiveManager(bigPath);
                await manager.LoadAsync();

                var entries = manager.GetFileList()
                    .Where(e => e.Contains(filterKeyword, StringComparison.OrdinalIgnoreCase) &&
                                e.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var entry in entries)
                {
                    try
                    {
                        var data = await manager.ExtractFileAsync(entry);
                        var content = System.Text.Encoding.GetEncoding(1252).GetString(data);
                        parser(content);
                    }
                    catch { /* تجاهل */ }
                }
            }
            catch { /* أرشيف تالف */ }
        }
    }

    private static void FillEmptySlots(CommandBarResult result, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            result.Slots.Add(new CommandBarSlot
            {
                SlotNumber = i,
                IsOccupied = false
            });
        }
    }
}
