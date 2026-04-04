from __future__ import annotations

from typing import Literal

from pydantic import BaseModel


class Node(BaseModel):
    """The node on the map."""

    col: int
    row: int


class NodeWithType(Node):
    """Node with type."""

    type: str


class NextNode(NodeWithType):
    """The next node on the map with 1-level lookahead."""

    index: int
    leads_to: list[NodeWithType]


class DAGNode(NodeWithType):
    """The node in the DAG map."""

    children: list[tuple[int, int]]  # list of (col, row) of the children nodes


class Map(BaseModel):
    """The map navigation information."""

    current_position: Node
    visited: list[NodeWithType]
    next_options: list[NextNode]
    nodes: list[DAGNode]
    boss: Node

    def _build_future_path_tree(self, start_node: NextNode, node_lookup: dict[str, DAGNode]) -> str:
        """BFS from a node through its children to build a future path tree string."""
        start_key = f"{start_node.col},{start_node.row}"
        canonical = node_lookup.get(start_key)
        current_keys: set[str] = set()
        if canonical:
            current_keys = {f"{c},{r}" for c, r in canonical.children}

        parts: list[str] = []
        while current_keys:
            level_nodes: list[tuple[str, int, int]] = []
            next_keys: set[str] = set()

            for key in sorted(current_keys):
                node = node_lookup.get(key)
                if node:
                    level_nodes.append((node.type, node.col, node.row))
                    for c, r in node.children:
                        next_keys.add(f"{c},{r}")

            if not level_nodes:
                break

            level_str = " or ".join(f"{t} ({c},{r})" for t, c, r in level_nodes)
            parts.append(f"-> {level_str}")
            current_keys = next_keys

        return " ".join(parts)

    def to_markdown(self) -> str:
        lines: list[str] = []

        # Path taken
        if self.visited:
            lines.append("## Path Taken\n")
            parts = [f"{i + 1}. {v.type} ({v.col},{v.row})" for i, v in enumerate(self.visited)]
            lines.append(" -> ".join(parts) + " <- current\n\n")

        # Build node lookup
        node_lookup: dict[str, DAGNode] = {}
        for node in self.nodes:
            node_lookup[f"{node.col},{node.row}"] = node

        # Next options
        if self.next_options:
            lines.append("## Choose Next Node\n")
            for opt in self.next_options:
                lines.append(f"- [{opt.index}] **{opt.type}** ({opt.col},{opt.row})\n")
                tree = self._build_future_path_tree(opt, node_lookup)
                if tree:
                    lines.append(f"  Future paths: {tree}\n")
            lines.append("\n")
        else:
            lines.append("## Map\n")
            lines.append("No travelable nodes available.\n\n")

        return "".join(lines)


class MapState(BaseModel):
    """The state when the scenario is on the map."""

    state_type: Literal["map"]
    map: Map

    def to_markdown(self) -> str:
        return self.map.to_markdown()
