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


class MapState(BaseModel):
    """The state when the scenario is on the map."""

    state_type: Literal["map"]
    map: Map
