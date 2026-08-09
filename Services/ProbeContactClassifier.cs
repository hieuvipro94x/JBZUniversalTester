using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// Nhận dạng contact của que dò ngay trong stream Production.
///
/// V12.8: một frame có thể chứa nhiều điểm contact hợp lệ (thực tế cần tối thiểu
/// hai chân I/O cùng lúc). Classifier trả về tối đa số contact được yêu cầu,
/// ưu tiên các I/O mạnh nhất thay vì ép về một I/O duy nhất. Các contact này chỉ dùng cho UI
/// chẩn đoán; tuyệt đối không được biến thành WrongWiring/Short và không điều khiển relay.
/// </summary>
public static class ProbeContactClassifier
{
    public sealed record Detection(int Io, int Score, string Signature);

    private sealed record Candidate(
        int Io,
        int Hits,
        int FanIn,
        bool Mapped,
        bool SourceWordMissing,
        int Score,
        bool IsProbeLike);

    public static bool TryDetect(
        ScanFrame frame,
        ProductModel? model,
        out Detection detection)
    {
        IReadOnlyList<Detection> detections = DetectMany(frame, model, 1);
        if (detections.Count > 0)
        {
            detection = detections[0];
            return true;
        }

        detection = new Detection(0, 0, string.Empty);
        return false;
    }

    /// <summary>
    /// Trả về các I/O đang có chữ ký contact que dò trong cùng một frame.
    /// Sắp xếp theo độ tin cậy giảm dần và loại trùng I/O.
    /// </summary>
    public static IReadOnlyList<Detection> DetectMany(
        ScanFrame frame,
        ProductModel? model,
        int maxContacts = 2,
        BoardCapacity? boardCapacity = null)
    {
        if (maxContacts <= 0 ||
            frame.Mode != BoardScanMode.Production ||
            frame.Connections.Count == 0)
        {
            return Array.Empty<Detection>();
        }

        int sourceCount = frame.Connections.Count;
        int capacityIo = boardCapacity?.TotalIoCapacity ?? Math.Max(
            BoardIoDecoder.IoPerExpansionCard,
            frame.CardNumber * BoardIoDecoder.IoPerScanCard);

        var fanInByTarget = new Dictionary<int, int>();
        int edgeCount = 0;

        foreach (KeyValuePair<int, IReadOnlySet<int>> pair in frame.Connections)
        {
            foreach (int target in pair.Value)
            {
                fanInByTarget[target] = fanInByTarget.GetValueOrDefault(target) + 1;
                edgeCount++;
            }
        }

        if (fanInByTarget.Count == 0 && frame.TargetHits.Count == 0)
            return Array.Empty<Detection>();

        int repeatedThreshold = Math.Clamp(sourceCount / 8, 6, 24);

        Candidate[] candidates = fanInByTarget.Keys
            .Concat(frame.TargetHits.Keys)
            .Distinct()
            .Where(io => boardCapacity?.ContainsGlobalIo(io) ??
                         (io > 0 && io <= BoardIoDecoder.MaxIoCount))
            .Select(io =>
            {
                int hits = frame.TargetHits.GetValueOrDefault(io);
                int fanIn = fanInByTarget.GetValueOrDefault(io);
                bool mapped = IsMapped(model, io);
                bool sourceWordMissing = !frame.Connections.ContainsKey(io);

                int score =
                    hits * 5 +
                    fanIn * 6 +
                    (mapped ? 16 : 0) +
                    (sourceWordMissing ? 8 : 0);

                bool repeatedTarget =
                    hits >= repeatedThreshold ||
                    fanIn >= repeatedThreshold;

                bool diagnosticSweep =
                    sourceCount >= Math.Max(16, (capacityIo * 3) / 5) &&
                    fanInByTarget.Count <= 6 &&
                    edgeCount >= Math.Max(6, sourceCount / 5) &&
                    (fanIn >= 6 || hits >= 6);

                bool dominantTarget =
                    edgeCount >= 8 &&
                    fanIn >= 6 &&
                    fanIn * 100 >= edgeCount * 28;

                return new Candidate(
                    io,
                    hits,
                    fanIn,
                    mapped,
                    sourceWordMissing,
                    score,
                    repeatedTarget || diagnosticSweep || dominantTarget);
            })
            .Where(candidate => candidate.IsProbeLike)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Mapped)
            .ThenByDescending(candidate => candidate.Hits)
            .ThenByDescending(candidate => candidate.FanIn)
            .ThenBy(candidate => candidate.Io)
            .ToArray();

        if (candidates.Length == 0)
            return Array.Empty<Detection>();

        // Nếu có hai đầu dò/contact thật, cả hai target thường đều có fan-in đáng kể.
        // Không lấy các target yếu chỉ vì frame có nhiều nhiễu: candidate thứ hai phải
        // đạt tối thiểu 35% score của candidate mạnh nhất hoặc tự đạt repeatedThreshold.
        int bestScore = Math.Max(1, candidates[0].Score);
        var result = new List<Detection>(Math.Min(maxContacts, candidates.Length));

        foreach (Candidate candidate in candidates)
        {
            if (result.Count >= maxContacts)
                break;

            if (result.Count > 0 &&
                candidate.Score * 100 < bestScore * 35 &&
                candidate.FanIn < repeatedThreshold &&
                candidate.Hits < repeatedThreshold)
            {
                continue;
            }

            string signature =
                $"TARGET IO{candidate.Io}: hits={candidate.Hits}, fan-in={candidate.FanIn}, " +
                $"sources={sourceCount}, edges={edgeCount}";

            result.Add(new Detection(candidate.Io, candidate.Score, signature));
        }

        return result;
    }

    private static bool IsMapped(ProductModel? model, int io)
    {
        if (model is null)
            return false;

        if (model.Pins.Any(pin => pin.IoNumber == io))
            return true;

        if (model.Clip is null)
            return false;

        return model.Clip.CommonIo == io ||
               model.Clip.Branches.Any(branch => branch.TargetIo == io);
    }
}
