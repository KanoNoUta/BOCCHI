using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Treasure;

/// <summary>
/// The hand-authored South Horn coffer order from upstream BOCCHI 4.2
/// (treasure_route.json, seven area segments). The hunt follows it from the
/// nearest coffer, or from the start of a chosen segment, wrapping around.
/// </summary>
public static class SouthHornTreasureRoute
{
    public readonly record struct Segment(string Id, uint[] Nodes);

    public static readonly Segment[] Segments =
    [
        new("base-camp", [1804, 1801, 1802, 1798, 1797, 1799, 1800, 1803, 1789, 1806, 1805, 1836, 1837, 1838]),
        new("wanderers-haven", [1811, 1841, 1842, 1794, 1840, 1839, 1812, 1810, 1808, 1809, 1807]),
        new("crystallized-caverns-1", [1818, 1814, 1813]),
        new("crystallized-caverns-2", [1856, 1817, 1819, 1820, 1791, 1821, 1822, 1843, 1844, 1795, 1845, 1846]),
        new("eldergrowth-1", [1816, 1793, 1827, 1828, 1823, 1825, 1829, 1830, 1792, 1853]),
        new("eldergrowth-2", [1790, 1833, 1832, 1831, 1835, 1826, 1824, 1854, 1815, 1855]),
        new("stonemarsh", [1834, 1848, 1847, 1850, 1849, 1852, 1851, 1796]),
    ];

    private static readonly uint[] Order = Segments.SelectMany(segment => segment.Nodes).ToArray();

    /// <param name="startSegment">A segment id, or null to start at the nearest coffer.</param>
    public static List<uint> OrderNodes(
        Vector3 start,
        IEnumerable<uint> validNodeIds,
        IReadOnlyDictionary<uint, Vector3> positions,
        string? startSegment)
    {
        var valid = validNodeIds.ToHashSet();
        // Authored order first; anything the route does not know goes last.
        var ordered = Order.Where(valid.Contains).Distinct().ToList();
        ordered.AddRange(valid.Where(id => !ordered.Contains(id)));
        if (ordered.Count == 0)
        {
            return ordered;
        }

        var entry = FindSegmentEntry(startSegment, valid) ?? ordered
            .Where(positions.ContainsKey)
            .OrderBy(id => Vector3.DistanceSquared(start, positions[id]))
            .Cast<uint?>()
            .FirstOrDefault() ?? ordered[0];

        var index = ordered.IndexOf(entry);
        return ordered.Skip(index).Concat(ordered.Take(index)).ToList();
    }

    private static uint? FindSegmentEntry(string? startSegment, HashSet<uint> valid)
    {
        if (string.IsNullOrEmpty(startSegment))
        {
            return null;
        }

        var segmentIndex = System.Array.FindIndex(Segments, segment => segment.Id == startSegment);
        if (segmentIndex < 0)
        {
            return null;
        }

        // The chosen segment may be cleared already: use the next one that is not.
        for (var offset = 0; offset < Segments.Length; offset++)
        {
            var segment = Segments[(segmentIndex + offset) % Segments.Length];
            foreach (var node in segment.Nodes)
            {
                if (valid.Contains(node))
                {
                    return node;
                }
            }
        }

        return null;
    }
}
