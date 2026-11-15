using JBZUniversalTester.ModelEditor.Models;

namespace JBZUniversalTester.ModelEditor.Services;

/// <summary>Operator ordering is independent of native row order and physical IO addresses.</summary>
public static class EditorDisplayOrder
{
    private static readonly IComparer<string> Names = new ConnectorNameComparer();

    public static IOrderedEnumerable<Connector> Connectors(IEnumerable<Connector> connectors) =>
        connectors.OrderBy(connector => connector.Name, Names).ThenBy(connector => connector.StartIo);

    public static IOrderedEnumerable<Pin> Pins(IEnumerable<Pin> pins) =>
        pins.OrderBy(pin => pin.Connector, Names).ThenBy(pin => pin.PinNo)
            .ThenBy(pin => pin.Io).ThenBy(pin => pin.SourceThtRowIndex);

    public static IOrderedEnumerable<Pin> StorageIdentity(IEnumerable<Pin> pins) =>
        pins.OrderBy(pin => pin.Io).ThenBy(pin => pin.SourceThtRowIndex)
            .ThenBy(pin => pin.Connector, Names).ThenBy(pin => pin.PinNo)
            .ThenBy(pin => pin.Line, StringComparer.Ordinal).ThenBy(pin => pin.Add, StringComparer.Ordinal);

    private sealed class ConnectorNameComparer : IComparer<string>
    {
        private static int Category(string name) => name.Equals("CLIP", StringComparison.OrdinalIgnoreCase) ? 1 :
            name.Equals("_DISCARD", StringComparison.OrdinalIgnoreCase) || name.Equals("_Interlock", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

        public int Compare(string? x, string? y)
        {
            string left = x?.Trim() ?? "", right = y?.Trim() ?? "";
            int category = Category(left).CompareTo(Category(right));
            if (category != 0) return category;
            int i = 0, j = 0;
            while (i < left.Length && j < right.Length)
            {
                if (char.IsAsciiDigit(left[i]) && char.IsAsciiDigit(right[j]))
                {
                    int endLeft = i, endRight = j;
                    while (endLeft < left.Length && char.IsAsciiDigit(left[endLeft])) endLeft++;
                    while (endRight < right.Length && char.IsAsciiDigit(right[endRight])) endRight++;
                    while (i < endLeft && left[i] == '0') i++;
                    while (j < endRight && right[j] == '0') j++;
                    int length = (endLeft - i).CompareTo(endRight - j);
                    if (length != 0) return length;
                    while (i < endLeft)
                    {
                        int digit = left[i++].CompareTo(right[j++]);
                        if (digit != 0) return digit;
                    }
                    i = endLeft; j = endRight;
                }
                else
                {
                    int character = char.ToUpperInvariant(left[i++]).CompareTo(char.ToUpperInvariant(right[j++]));
                    if (character != 0) return character;
                }
            }
            int remaining = (left.Length - i).CompareTo(right.Length - j);
            return remaining != 0 ? remaining : StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }
    }
}
