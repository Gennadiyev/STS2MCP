from pydantic import BaseModel, model_validator

from states.common.keyword import Keywords


class Relic(BaseModel):
    """The relic object."""

    id: str
    name: str
    description: str
    counter: int | None = None  # number if relic shows a counter, null otherwise
    keywords: Keywords

    def to_markdown(self) -> str:
        """Convert the relic to a markdown string."""
        pass
