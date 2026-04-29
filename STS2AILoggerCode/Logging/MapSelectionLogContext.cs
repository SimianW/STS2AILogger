using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace STS2AILogger.STS2AILoggerCode.Logging;

public sealed record MapSelectionLogState(
    string SelectionId,
    object Selected,
    object? Player,
    object? Run);

public static class MapSelectionLogContext
{
    private static long _nextSelectionId;
    private static MapSelectionLogState? _pending;

    public static void LogSelection(NMapScreen screen, NMapPoint selectedPoint, RunState? runState)
    {
        if (runState == null)
        {
            return;
        }

        EventLogger.SetRunContext(runState);

        string selectionId = $"map-choice-{Interlocked.Increment(ref _nextSelectionId)}";
        List<NMapPoint> availableNodes = FindAvailableNodes(screen, selectedPoint);
        int selectedIndex = availableNodes.FindIndex(node => node.Point.coord.Equals(selectedPoint.Point.coord));
        object selected = SelectedSnapshot(selectedPoint, selectedIndex);
        object? player = GameSnapshots.Player(LocalContext.GetMe(runState));
        object? run = GameSnapshots.Run(runState);

        _pending = new MapSelectionLogState(selectionId, selected, player, run);

        EventLogger.Write("map_nodes_offered", new
        {
            selection_id = selectionId,
            selection_kind = "map_node",
            can_skip = false,
            current_coord = Coord(runState.CurrentMapCoord),
            available_nodes = availableNodes.Select(NodeSnapshot).ToList(),
            run,
            player
        }, "map nodes offered");

        EventLogger.Write("map_node_selected", new
        {
            selection_id = selectionId,
            selection_kind = "map_node",
            selected,
            run,
            player
        }, "map node selected");
    }

    public static void WriteResolvedIfPending(RunState? runState)
    {
        MapSelectionLogState? pending = _pending;
        if (pending == null)
        {
            return;
        }

        _pending = null;
        EventLogger.Write("map_node_resolved", new
        {
            selection_id = pending.SelectionId,
            selection_kind = "map_node",
            selected = pending.Selected,
            resolved_room = GameSnapshots.Room(runState?.CurrentRoom),
            run = GameSnapshots.Run(runState),
            player = runState == null ? pending.Player : GameSnapshots.Player(LocalContext.GetMe(runState))
        }, "map node resolved");
    }

    private static List<NMapPoint> FindAvailableNodes(NMapScreen screen, NMapPoint selectedPoint)
    {
        var result = new List<NMapPoint>();
        foreach (NMapPoint node in EnumerateMapPoints(screen))
        {
            if (node.State == MapPointState.Travelable || node.Point.coord.Equals(selectedPoint.Point.coord))
            {
                result.Add(node);
            }
        }

        if (result.All(node => !node.Point.coord.Equals(selectedPoint.Point.coord)))
        {
            result.Add(selectedPoint);
        }

        return result
            .OrderBy(node => node.Point.coord.row)
            .ThenBy(node => node.Point.coord.col)
            .ToList();
    }

    private static IEnumerable<NMapPoint> EnumerateMapPoints(Godot.Node node)
    {
        foreach (Godot.Node child in node.GetChildren())
        {
            if (child is NMapPoint mapPoint)
            {
                yield return mapPoint;
            }

            foreach (NMapPoint descendant in EnumerateMapPoints(child))
            {
                yield return descendant;
            }
        }
    }

    private static object SelectedSnapshot(NMapPoint node, int index)
    {
        return new
        {
            index,
            coord = Coord(node.Point.coord),
            map_point_type_before_reveal = node.Point.PointType.ToString(),
            state = node.State.ToString()
        };
    }

    private static object NodeSnapshot(NMapPoint node, int index)
    {
        return new
        {
            index,
            coord = Coord(node.Point.coord),
            map_point_type = node.Point.PointType.ToString(),
            state = node.State.ToString(),
            children = node.Point.Children
                .OrderBy(child => child.coord.row)
                .ThenBy(child => child.coord.col)
                .Select(child => Coord(child.coord))
                .ToList()
        };
    }

    private static object? Coord(MapCoord? coord)
    {
        return coord.HasValue ? Coord(coord.Value) : null;
    }

    private static object Coord(MapCoord coord)
    {
        return new
        {
            row = coord.row,
            col = coord.col
        };
    }
}
