using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Nodes;

namespace Content.Server._Misfits.Reactor;

public static class ReactorPowerNetworkHelper
{
    public static HashSet<EntityUid> GetNetworkMembers(IEntityManager entMan, NodeContainerSystem nodeContainer, EntityUid start)
    {
        var visitedGroups = new HashSet<INodeGroup>();
        var members = new HashSet<EntityUid>();
        var queue = new Queue<Node>();

        if (nodeContainer.TryGetNode<CableDeviceNode>(start, "output", out var startNode))
            queue.Enqueue(startNode);

        while (queue.TryDequeue(out var node))
        {
            if (node.NodeGroup is not { } group || !visitedGroups.Add(group))
                continue;

            foreach (var member in group.Nodes)
            {
                members.Add(member.Owner);

                if (!entMan.HasComponent<PowerNetworkBatteryComponent>(member.Owner) ||
                    !entMan.TryGetComponent<PowerMonitoringDeviceComponent>(member.Owner, out var device) ||
                    !entMan.TryGetComponent<NodeContainerComponent>(member.Owner, out var nodeContainerComp))
                {
                    continue;
                }

                if (nodeContainerComp.Nodes.TryGetValue(device.SourceNode, out var sourceNode))
                    queue.Enqueue(sourceNode);

                if (nodeContainerComp.Nodes.TryGetValue(device.LoadNode, out var loadNode))
                    queue.Enqueue(loadNode);
            }
        }

        return members;
    }
}
