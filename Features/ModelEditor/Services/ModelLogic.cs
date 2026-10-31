using JBZUniversalTester.ModelEditor.Models;

namespace JBZUniversalTester.ModelEditor.Services;

public static class ModelLogic
{
    public static IReadOnlyDictionary<string, string> BuildPreferredWireColorMap(IEnumerable<Pin> pins)
    {
        return pins
            .Select(pin => (Pin: pin, IsNumber: int.TryParse(pin.Connector.Trim(), out var connectorNumber), ConnectorNumber: connectorNumber))
            .Where(item => !string.IsNullOrWhiteSpace(item.Pin.Line) && !string.IsNullOrWhiteSpace(item.Pin.Color))
            .OrderBy(item => item.IsNumber && item.ConnectorNumber is >= 1 and <= 5 ? 0 : 1)
            .ThenBy(item => item.IsNumber ? item.ConnectorNumber : int.MaxValue)
            .ThenBy(item => item.Pin.Io)
            .GroupBy(item => item.Pin.Line.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Pin.Color.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public static void InsertPinAfter(List<Pin> pins, int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= pins.Count)
            throw new ValidationException("Hãy chọn dòng cần chèn thêm chân.");
        var selected = pins[selectedIndex];
        pins.Insert(selectedIndex + 1, new Pin { Connector = selected.Connector });
        RenumberPinRows(pins);
    }

    public static void DeletePinAt(List<Pin> pins, int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= pins.Count)
            throw new ValidationException("Hãy chọn dòng cần xóa.");
        if (pins.Count == 1)
            throw new ValidationException("Model phải còn ít nhất một PIN.");
        pins.RemoveAt(selectedIndex);
        RenumberPinRows(pins);
    }

    private static void RenumberPinRows(List<Pin> pins)
    {
        var firstIo = pins.Select(pin => pin.Io).Where(io => io > 0).DefaultIfEmpty(1).Min();
        if ((long)firstIo + pins.Count - 1 > int.MaxValue)
            throw new ValidationException("Dãy I/O sau khi thêm dòng vượt giới hạn số nguyên.");
        var pinNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < pins.Count; index++)
        {
            var pin = pins[index];
            pin.Io = firstIo + index;
            pin.PinNo = pinNumbers.TryGetValue(pin.Connector, out var number) ? number + 1 : 1;
            pinNumbers[pin.Connector] = pin.PinNo;
        }
        RebuildConnections(pins);
    }

    public static void UpdateConnectorLayout(IList<Connector> connectors, IReadOnlyList<Pin> pins)
    {
        for (var index = connectors.Count - 1; index >= 0; index--)
        {
            var connector = connectors[index];
            var members = pins.Where(pin => pin.Connector.Equals(connector.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (members.Count == 0)
            {
                connectors.RemoveAt(index);
                continue;
            }
            connector.PinCount = members.Count;
            connector.StartIo = members.Min(pin => pin.Io);
        }
    }

    public static void ApplyAutomaticWireColors(IList<Pin> pins, int changedIndex, bool lineChanged)
    {
        if (lineChanged)
            pins[changedIndex].Color = "";

        // Một chân không còn tên dây thì không còn màu gắn với dây đó.
        foreach (var pin in pins.Where(pin => string.IsNullOrWhiteSpace(pin.Line)))
            pin.Color = "";

        var colorsByWire = BuildPreferredWireColorMap(pins);
        foreach (var pin in pins)
        {
            var wireName = pin.Line.Trim();
            if (wireName.Length > 0 && string.IsNullOrWhiteSpace(pin.Color) &&
                colorsByWire.TryGetValue(wireName, out var color))
                pin.Color = color;
        }
    }

    /// <summary>
    /// Xác định trạng thái kết nối hiển thị cho cột TÊN DÂY.
    /// Một chân được xem là đã có kết nối khi TÊN DÂY (Line) hoặc DÂY NỐI
    /// (Add) trùng với tên ở một trong hai cột của ít nhất một chân khác.
    /// Cả hai tên đều được xét để nhận diện các chuỗi dây nối tiếp nhau.
    ///
    /// AO/aN là PIN TYPE đặc biệt của HarnProg. Khi có AO và ít nhất một
    /// branch aN, toàn bộ nhóm special được xem là đã kết nối dù tên mỗi
    /// branch là khác nhau.
    /// </summary>
    public static IReadOnlyDictionary<int, bool> BuildWireConnectionStatus(IEnumerable<Pin> pins)
    {
        var list = pins.ToList();
        var status = list.ToDictionary(pin => pin.Io, _ => false);

        var groups = new Dictionary<string, List<Pin>>(StringComparer.Ordinal);
        foreach (var pin in list)
        {
            var names = new[] { pin.Line.Trim(), pin.Add.Trim() }
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (!groups.TryGetValue(name, out var members))
                    groups[name] = members = [];
                members.Add(pin);
            }
        }

        foreach (var members in groups.Values)
        {
            if (members.Select(pin => pin.Io).Distinct().Count() < 2)
                continue;
            foreach (var pin in members)
                status[pin.Io] = true;
        }

        var special = list
            .Where(pin => TryNormalizeHarnProgPinTypeLabel(pin.Line, out _))
            .ToList();
        var hasAo = special.Any(pin =>
            TryNormalizeHarnProgPinTypeLabel(pin.Line, out var label) && label == "AO");
        if (hasAo && special.Select(pin => pin.Io).Distinct().Count() >= 2)
        {
            foreach (var pin in special)
                status[pin.Io] = true;
        }

        return status;
    }

    /// <summary>
    /// Chuẩn hóa cụm CLIP đặc biệt của HarnProg V1.110.
    ///
    /// File gốc không lưu AO/a1..aN như connector/pin thông thường. Trong UI
    /// chúng ta vẫn biểu diễn CLIP như một connector liên tục, nhưng quan hệ
    /// dập/nối phải luôn là AO(common) -> toàn bộ branch. Với layout 17 chân
    /// của file WH322244 gốc, nhãn logic phải giữ đúng mapping legacy:
    /// IO+0=AO, IO+1..+6=a1..a6, IO+7=a17, IO+8..+16=a8..a16.
    /// </summary>
    public static bool TryNormalizeHarnProgPinTypeLabel(string? value, out string label)
    {
        label = "";
        var text = value?.Trim() ?? "";
        if (text.Length == 0)
            return false;

        if (text.Equals("AO", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("A0", StringComparison.OrdinalIgnoreCase))
        {
            label = "AO";
            return true;
        }

        if (text.Length >= 2 &&
            (text[0] == 'a' || text[0] == 'A') &&
            int.TryParse(text[1..], out var branch) &&
            branch > 0)
        {
            label = $"a{branch}";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Chuẩn hóa cụm PIN TYPE đặc biệt AO/a1..aN của HarnProg V1.110.
    ///
    /// Quy tắc UI mới:
    /// - người dùng NHẬP AO/a1/a2... ở cột TÊN DÂY;
    /// - trong document vẫn giữ nguyên Line để người dùng nhìn/sửa được;
    /// - TestNo chỉ là bản sao nội bộ dùng khi export;
    /// - khi ghi THT, ThtFileService sẽ chuyển Line AO/aN sang cột
    ///   PIN table[4] = 핀종류 và để PIN table[1] = 선이름 rỗng.
    ///
    /// Không còn bắt buộc connector phải có tên CLIP. Một chân AO ở TÊN DÂY
    /// là anchor; mọi AO/aN trong model được coi là cùng cụm special.
    /// Connector CLIP vẫn được hỗ trợ để round-trip file cũ.
    /// </summary>
    public static void NormalizeHarnProgClip(IList<Pin> pins)
    {
        var explicitCommonPins = pins
            .Where(pin =>
                (TryNormalizeHarnProgPinTypeLabel(pin.Line, out var lineLabel) && lineLabel == "AO") ||
                (TryNormalizeHarnProgPinTypeLabel(pin.TestNo, out var testLabel) && testLabel == "AO"))
            .OrderBy(pin => pin.Io)
            .ToList();

        if (explicitCommonPins.Count > 1)
            throw new ValidationException(
                "HarnProg THT hiện chỉ hỗ trợ một cụm AO/aN. Có nhiều hơn một chân AO.");

        List<Pin> specialPins;
        Pin? common = null;

        if (explicitCommonPins.Count == 1)
        {
            common = explicitCommonPins[0];

            // AO/aN là tên reserved của HarnProg. Khi đã có đúng một AO,
            // mọi AO/aN nhập ở TÊN DÂY đều là special PIN TYPE, không phụ
            // thuộc tên connector nội bộ của ModelCreator.
            specialPins = pins
                .Where(pin =>
                    TryNormalizeHarnProgPinTypeLabel(pin.Line, out _) ||
                    TryNormalizeHarnProgPinTypeLabel(pin.TestNo, out _))
                .OrderBy(pin => pin.Io)
                .ToList();
        }
        else
        {
            // Backward compatibility: file THT cũ được loader biểu diễn bằng
            // connector nội bộ CLIP. Nếu chưa có AO nhập ở TÊN DÂY thì vẫn
            // tự sinh AO/a1... như trước.
            specialPins = pins
                .Where(pin => pin.Connector.Trim().Equals("CLIP", StringComparison.OrdinalIgnoreCase))
                .OrderBy(pin => pin.Io)
                .ToList();

            if (specialPins.Count == 0)
                return;

            common = specialPins[0];
        }

        if (specialPins.Count == 0 || common is null)
            return;

        var firstIo = specialPins.Min(pin => pin.Io);
        var contiguous = specialPins
            .Select(pin => pin.Io)
            .SequenceEqual(Enumerable.Range(firstIo, specialPins.Count));
        var reference17 =
            specialPins.Count == 17 &&
            contiguous &&
            common.Io == firstIo;

        for (var index = 0; index < specialPins.Count; index++)
        {
            var pin = specialPins[index];

            string label;
            if (TryNormalizeHarnProgPinTypeLabel(pin.Line, out var lineLabel))
            {
                label = lineLabel;
            }
            else if (TryNormalizeHarnProgPinTypeLabel(pin.TestNo, out var testLabel))
            {
                label = testLabel;
            }
            else if (reference17)
            {
                var offset = pin.Io - firstIo;
                label = offset switch
                {
                    0 => "AO",
                    7 => "a17",
                    _ => $"a{offset}"
                };
            }
            else
            {
                label = pin.Io == common.Io ? "AO" : $"a{index}";
            }

            // QUAN TRỌNG: giữ label trong TÊN DÂY của ModelCreator.
            // Exporter mới chỉ xóa nó ở cột 선이름 của PIN table khi serialize,
            // đồng thời ghi chính label này sang cột 핀종류.
            pin.Line = label;
            pin.TestNo = label;

            // Dữ liệu phụ của special wire catalog trong file gốc là trống.
            pin.Add = "";
            pin.Thickness = "";
            pin.Color = "";
        }

        // Nếu AO được nhập ở vị trí không phải phần tử đầu, vẫn dùng AO thật
        // làm common/source.
        common = specialPins.FirstOrDefault(pin =>
            TryNormalizeHarnProgPinTypeLabel(pin.Line, out var label) && label == "AO")
            ?? common;

        common.MainIo = -1;
        common.Coupling = specialPins
            .Where(pin => pin.Io != common.Io)
            .Select(pin => pin.Io)
            .OrderBy(io => io)
            .ToList();

        foreach (var branch in specialPins.Where(pin => pin.Io != common.Io))
        {
            branch.MainIo = common.Io;
            branch.Coupling.Clear();
        }
    }

    public static void ValidateConnectors(IReadOnlyList<Connector> connectors)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < connectors.Count; i++)
        {
            var c = connectors[i];
            if (string.IsNullOrWhiteSpace(c.Name)) throw new ValidationException($"Connector dòng {i + 1} chưa có tên.");
            if (!names.Add(c.Name.Trim())) throw new ValidationException($"Tên connector bị trùng: {c.Name}");
            if (c.PinCount <= 0 || c.StartIo <= 0)
                throw new ValidationException($"Connector {c.Name}: số pin và I/O bắt đầu phải lớn hơn 0.");
        }
    }

    public static List<Pin> RegeneratePins(IReadOnlyList<Connector> connectors, IReadOnlyList<Pin> existing)
    {
        ValidateConnectors(connectors);
        var byIdentity = existing
            .GroupBy(p => $"{p.Connector.ToUpperInvariant()}\u0001{p.PinNo}")
            .ToDictionary(g => g.Key, g => g.Last());
        var byIo = existing.GroupBy(p => p.Io).ToDictionary(g => g.Key, g => g.First());
        var result = new List<Pin>();
        var validIos = new HashSet<int>();

        static int PreferredIo(Connector connector, int localPin)
        {
            var value = (long)connector.StartIo + localPin - 1;
            if (value <= 0 || value > int.MaxValue)
                throw new ValidationException($"Connector {connector.Name}: I/O sinh tự động vượt phạm vi hợp lệ.");
            return (int)value;
        }

        int NextFreeIo(int preferred)
        {
            var candidate = Math.Max(1, preferred);
            while (validIos.Contains(candidate))
            {
                if (candidate == int.MaxValue)
                    throw new ValidationException("Không còn I/O trống để tạo pin mới.");
                candidate++;
            }
            return candidate;
        }

        foreach (var connector in connectors)
        {
            for (var localPin = 1; localPin <= connector.PinCount; localPin++)
            {
                var preferred = PreferredIo(connector, localPin);
                byIdentity.TryGetValue($"{connector.Name.ToUpperInvariant()}\u0001{localPin}", out var old);
                old ??= byIo.GetValueOrDefault(preferred);

                if (old is null)
                {
                    var io = NextFreeIo(preferred);
                    validIos.Add(io);
                    result.Add(new Pin { Io = io, Connector = connector.Name, PinNo = localPin });
                }
                else
                {
                    var copy = old.Clone();
                    var io = old.Io > 0 && !validIos.Contains(old.Io) ? old.Io : NextFreeIo(preferred);
                    validIos.Add(io);
                    copy.Io = io;
                    copy.Connector = connector.Name;
                    copy.PinNo = localPin;
                    result.Add(copy);
                }
            }
        }
        foreach (var pin in result)
        {
            pin.Coupling = pin.Coupling.Distinct().Where(v => validIos.Contains(v) && v != pin.Io).ToList();
            if (!validIos.Contains(pin.MainIo) || pin.MainIo == pin.Io) pin.MainIo = -1;
        }
        result = result.OrderBy(p => p.Io).ToList();
        NormalizeHarnProgClip(result);
        return result;
    }

    public static void RebuildConnections(IList<Pin> pins)
    {
        foreach (var p in pins) { p.MainIo = -1; p.Coupling.Clear(); }
        var groups = new Dictionary<string, List<Pin>>(StringComparer.Ordinal);
        foreach (var pin in pins)
        {
            var key = !string.IsNullOrWhiteSpace(pin.Add) ? pin.Add.Trim() : pin.Line.Trim();
            if (string.IsNullOrEmpty(key)) continue;
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(pin);
        }
        foreach (var pair in groups)
        {
            var key = pair.Key;
            var members = pair.Value.GroupBy(p => p.Io).Select(g => g.First()).OrderBy(p => p.Io).ToList();
            if (members.Count < 2) continue;
            var exactSources = members.Where(p => p.Line.Trim() == key && string.IsNullOrWhiteSpace(p.Add)).ToList();
            var source = (exactSources.Count > 0 ? exactSources : members).OrderBy(p => p.Io).First();
            var normalTargets = members.Where(p => p.Io != source.Io && p.Line.Trim() == key && string.IsNullOrWhiteSpace(p.Add)).ToList();
            var addTargets = members.Where(p => p.Io != source.Io && p.Add.Trim() == key).ToList();
            var known = new HashSet<Pin>(normalTargets.Concat(addTargets));
            var otherTargets = members.Where(p => p.Io != source.Io && !known.Contains(p)).ToList();
            var targets = normalTargets.Concat(addTargets).Concat(otherTargets).ToList();
            source.MainIo = -1;
            source.Coupling = targets.Select(p => p.Io).ToList();
            foreach (var target in targets) { target.MainIo = source.Io; target.Coupling.Clear(); }
        }

        // Quan hệ dập/nối CLIP là metadata đặc biệt của HarnProg, không phụ
        // thuộc TÊN DÂY/DÂY NỐI thông thường. Luôn khôi phục sau rebuild.
        NormalizeHarnProgClip(pins);
    }

    public static void ValidatePinsForEditing(IReadOnlyList<Connector> connectors, IReadOnlyList<Pin> pins)
    {
        ValidateConnectors(connectors);
        var expected = connectors.Sum(c => (long)c.PinCount);
        if (pins.Count != expected) throw new ValidationException($"Tổng pin connector={expected} nhưng bảng pin có {pins.Count} dòng.");

        var connectorByName = connectors.ToDictionary(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var pin in pins)
        {
            if (pin.Io <= 0)
                throw new ValidationException($"I/O phải lớn hơn 0: {pin.Io}.");
            if (!connectorByName.TryGetValue(pin.Connector.Trim(), out var connector))
                throw new ValidationException($"Pin I/O {pin.Io} dùng connector không tồn tại: {pin.Connector}");
            if (pin.PinNo <= 0 || pin.PinNo > connector.PinCount)
                throw new ValidationException(
                    $"Pin I/O {pin.Io} có số PIN {pin.PinNo} ngoài phạm vi connector {connector.Name} (1..{connector.PinCount}).");
        }
    }

    public static void ValidatePinsForSave(IReadOnlyList<Connector> connectors, IReadOnlyList<Pin> pins)
    {
        ValidatePinsForEditing(connectors, pins);
        // Duplicate I/O is editable data, not a reason to discard or renumber rows.
        // Relationships cannot be checked unambiguously by I/O until duplicates are resolved.
        if (!pins.GroupBy(pin => pin.Io).Any(group => group.Count() > 1))
            ValidatePins(connectors, pins);
    }

    public static void ValidatePins(IReadOnlyList<Connector> connectors, IReadOnlyList<Pin> pins)
    {
        ValidatePinsForEditing(connectors, pins);
        var byIo = new Dictionary<int, Pin>();
        foreach (var pin in pins)
            if (!byIo.TryAdd(pin.Io, pin))
                throw new ValidationException($"Bảng pin có I/O bị trùng: {pin.Io}.");

        foreach (var pin in pins)
        {
            if (pin.MainIo != -1 && !byIo.ContainsKey(pin.MainIo))
                throw new ValidationException($"Pin I/O {pin.Io} tham chiếu I/O chính không tồn tại: {pin.MainIo}");
            var invalid = pin.Coupling.Where(v => !byIo.ContainsKey(v) || v == pin.Io).ToList();
            if (invalid.Count > 0) throw new ValidationException($"Pin I/O {pin.Io} có danh sách nối không hợp lệ: {string.Join(',', invalid)}");
            if (pin.Coupling.Count != pin.Coupling.Distinct().Count())
                throw new ValidationException($"Pin I/O {pin.Io} có I/O nối bị lặp.");
            if (pin.MainIo != -1 && pin.Coupling.Count > 0)
                throw new ValidationException($"Pin I/O {pin.Io} vừa là đích vừa chứa danh sách nối.");

            if (pin.MainIo != -1)
            {
                var source = byIo[pin.MainIo];
                if (source.MainIo != -1 || !source.Coupling.Contains(pin.Io))
                    throw new ValidationException(
                        $"Liên kết I/O {pin.MainIo} -> {pin.Io} không đồng nhất giữa I/O chính và danh sách nối.");
            }

            foreach (var targetIo in pin.Coupling)
            {
                var target = byIo[targetIo];
                if (target.MainIo != pin.Io)
                    throw new ValidationException(
                        $"Liên kết I/O {pin.Io} -> {targetIo} không đồng nhất giữa danh sách nối và I/O chính.");
            }
        }
    }
}
