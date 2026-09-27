using System.Text.Json;
using System.Text.Json.Nodes;


namespace PdnWin.Docking;

/// <summary>Which way a split lays out its children; the layout model's own, so that it belongs
/// to no UI framework.</summary>
public enum DockOrientation
{
    /// <summary>Side by side.</summary>
    Horizontal,

    /// <summary>Stacked.</summary>
    Vertical,
}

/// <summary>Where a dragged pane lands relative to a group.</summary>
public enum DockZone
{
    /// <summary>As another tab in the group.</summary>
    Center,

    /// <summary>In a new group to the left.</summary>
    Left,

    /// <summary>In a new group to the right.</summary>
    Right,

    /// <summary>In a new group above.</summary>
    Top,

    /// <summary>In a new group below.</summary>
    Bottom,
}

/// <summary>A node of the layout tree.</summary>
public abstract class DockNode
{
    /// <summary>The split this node sits in, or null for the root.</summary>
    public DockSplit? Parent { get; internal set; }

    internal abstract JsonNode ToJson();

    internal static DockNode FromJson(JsonNode node)
    {
        if (node["panes"] is JsonArray panes)
        {
            var group = new DockGroup(panes.Select(p => p!.GetValue<string>()));
            group.Selected = node["selected"]?.GetValue<string>();
            return group;
        }

        var split = new DockSplit(
            node["orientation"]!.GetValue<string>() == "h" ? DockOrientation.Horizontal : DockOrientation.Vertical);
        JsonArray children = node["children"]!.AsArray();
        JsonArray weights = node["weights"]!.AsArray();
        for (int i = 0; i < children.Count; i++)
        {
            split.Add(FromJson(children[i]!), weights[i]!.GetValue<double>());
        }

        return split;
    }
}

/// <summary>A row or column of nodes, each with a relative size.</summary>
public sealed class DockSplit(DockOrientation orientation) : DockNode
{
    /// <summary>Horizontal lays children side by side; vertical stacks them.</summary>
    public DockOrientation Orientation { get; } = orientation;

    /// <summary>The children, in order.</summary>
    public List<DockNode> Children { get; } = [];

    /// <summary>Star weights, one per child.</summary>
    public List<double> Weights { get; } = [];

    /// <summary>Adds a child at the end.</summary>
    public DockSplit Add(DockNode child, double weight = 1)
    {
        Insert(Children.Count, child, weight);
        return this;
    }

    internal void Insert(int index, DockNode child, double weight)
    {
        child.Parent = this;
        Children.Insert(index, child);
        Weights.Insert(index, weight);
    }

    internal override JsonNode ToJson() => new JsonObject
    {
        ["orientation"] = Orientation == DockOrientation.Horizontal ? "h" : "v",
        ["weights"] = new JsonArray(Weights.Select(w => JsonValue.Create(Math.Round(w, 4))).ToArray<JsonNode?>()),
        ["children"] = new JsonArray(Children.Select(c => c.ToJson()).ToArray<JsonNode?>()),
    };
}

/// <summary>A set of panes shown as tabs, one at a time.</summary>
public sealed class DockGroup(IEnumerable<string> panes) : DockNode
{
    /// <summary>Pane IDs, in tab order.</summary>
    public List<string> Panes { get; } = [.. panes];

    /// <summary>The pane showing.</summary>
    public string? Selected { get; set; }

    internal override JsonNode ToJson() => new JsonObject
    {
        ["panes"] = new JsonArray(Panes.Select(p => JsonValue.Create(p)).ToArray<JsonNode?>()),
        ["selected"] = Selected,
    };
}

/// <summary>
/// The layout tree and the operations on it: moving a pane, closing one, bringing one back. Kept
/// apart from the controls so that the tree's rules (no empty groups, no one-child splits) are in
/// one place.
/// </summary>
public sealed class DockLayout(DockNode root)
{
    /// <summary>The top of the tree.</summary>
    public DockNode Root { get; private set; } = root;

    /// <summary>Every pane that is placed somewhere.</summary>
    public IEnumerable<string> PlacedPanes => Groups().SelectMany(g => g.Panes);

    /// <summary>Every group, depth first.</summary>
    public IEnumerable<DockGroup> Groups() => Walk(Root).OfType<DockGroup>();

    /// <summary>The group holding <paramref name="pane"/>, if it is placed.</summary>
    public DockGroup? GroupOf(string pane) => Groups().FirstOrDefault(g => g.Panes.Contains(pane));

    /// <summary>Moves <paramref name="pane"/> to <paramref name="zone"/> of <paramref name="target"/>.</summary>
    public void Move(string pane, DockGroup target, DockZone zone)
    {
        DockGroup? source = GroupOf(pane);
        if (source is null)
        {
            return;
        }

        if (ReferenceEquals(source, target) && (zone == DockZone.Center || source.Panes.Count == 1))
        {
            return;
        }

        source.Panes.Remove(pane);
        if (source.Selected == pane)
        {
            source.Selected = source.Panes.FirstOrDefault();
        }

        Place(pane, target, zone);
        if (source.Panes.Count == 0)
        {
            Remove(source);
        }
    }

    /// <summary>Takes a pane out of the layout.</summary>
    public void Close(string pane)
    {
        DockGroup? group = GroupOf(pane);
        if (group is null)
        {
            return;
        }

        group.Panes.Remove(pane);
        if (group.Selected == pane)
        {
            group.Selected = group.Panes.FirstOrDefault();
        }

        if (group.Panes.Count == 0)
        {
            Remove(group);
        }
    }

    /// <summary>Puts a closed pane back: as a tab beside <paramref name="near"/> if that is placed,
    /// otherwise in a new group down the right-hand side.</summary>
    public void Show(string pane, string? near = null)
    {
        if (GroupOf(pane) is { } already)
        {
            already.Selected = pane;
            return;
        }

        if (near is not null && GroupOf(near) is { } neighbour)
        {
            Place(pane, neighbour, DockZone.Center);
            return;
        }

        if (Root is DockGroup only)
        {
            Place(pane, only, DockZone.Right);
            return;
        }

        var split = (DockSplit)Root;
        var group = new DockGroup([pane]) { Selected = pane };
        if (split.Orientation == DockOrientation.Horizontal)
        {
            split.Add(group, split.Weights.Average() * 0.5);
        }
        else
        {
            var row = new DockSplit(DockOrientation.Horizontal);
            Root = row;
            row.Add(split, 3).Add(group, 1);
        }
    }

    /// <summary>The layout as JSON.</summary>
    public JsonElement ToJson() => JsonSerializer.SerializeToElement(Root.ToJson());

    /// <summary>A layout read back from <see cref="ToJson"/>, or null if it cannot be.</summary>
    public static DockLayout? FromJson(JsonElement json)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(json.GetRawText());
            return node is null ? null : new DockLayout(DockNode.FromJson(node));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NullReferenceException or FormatException)
        {
            return null;
        }
    }

    private void Place(string pane, DockGroup target, DockZone zone)
    {
        if (zone == DockZone.Center)
        {
            target.Panes.Add(pane);
            target.Selected = pane;
            return;
        }

        var group = new DockGroup([pane]) { Selected = pane };
        DockOrientation orientation = zone is DockZone.Left or DockZone.Right ? DockOrientation.Horizontal : DockOrientation.Vertical;
        bool before = zone is DockZone.Left or DockZone.Top;
        DockSplit? parent = target.Parent;

        if (parent is not null && parent.Orientation == orientation)
        {
            // A sibling in the existing row or column, halving the target's share.
            int index = parent.Children.IndexOf(target);
            double half = parent.Weights[index] / 2;
            parent.Weights[index] = half;
            parent.Insert(before ? index : index + 1, group, half);
            return;
        }

        var split = new DockSplit(orientation);
        Replace(target, split);
        split.Add(before ? group : target).Add(before ? target : group);
    }

    private void Remove(DockNode node)
    {
        DockSplit? parent = node.Parent;
        if (parent is null)
        {
            // The last group went; keep an empty one so there is somewhere to drop into.
            Root = new DockGroup([]);
            return;
        }

        int index = parent.Children.IndexOf(node);
        parent.Children.RemoveAt(index);
        parent.Weights.RemoveAt(index);
        if (parent.Children.Count == 1)
        {
            DockNode last = parent.Children[0];
            Replace(parent, last);
        }
    }

    private void Replace(DockNode old, DockNode replacement)
    {
        DockSplit? parent = old.Parent;
        if (parent is null)
        {
            replacement.Parent = null;
            Root = replacement;
            return;
        }

        int index = parent.Children.IndexOf(old);
        parent.Children[index] = replacement;
        replacement.Parent = parent;
        old.Parent = null;
    }

    private static IEnumerable<DockNode> Walk(DockNode node)
    {
        yield return node;
        if (node is DockSplit split)
        {
            foreach (DockNode child in split.Children)
            {
                foreach (DockNode n in Walk(child))
                {
                    yield return n;
                }
            }
        }
    }
}
