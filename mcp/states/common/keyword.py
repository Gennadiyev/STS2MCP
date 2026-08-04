from pydantic import BaseModel, model_validator


class Keyword(BaseModel):
    """The keyword object."""

    name: str
    description: str

    def to_markdown(self):
        """Convert the keyword to a markdown string."""
        return f"**{self.name}**: {self.description}"


class Keywords(BaseModel):
    """The keywords object, which is a collection of keywords."""

    keywords: dict[str, Keyword]  # keyword name -> keyword

    def __add__(self, other_keywords: "Keywords") -> "Keywords":
        """Combine two sets of keywords."""
        combined_keywords = self.keywords.copy()
        for name, keyword in other_keywords.keywords.items():
            if name not in combined_keywords:
                combined_keywords[name] = keyword
        return Keywords.model_construct(keywords=combined_keywords)

    def get_keyword_names_str(self) -> str:
        """Get the names of the keywords, separated by commas."""
        if not self.keywords:
            return ""
        keyword_names = [name for name in self.keywords]
        return f" [{', '.join(keyword_names)}]"

    def to_markdown(self) -> str:
        """Convert the keywords to a markdown string."""
        if not self.keywords:
            return ""
        lines = []
        for keyword in self.keywords.values():
            lines.append(f"- {keyword.to_markdown()}\n")
        return "".join(lines)

    @model_validator(mode="before")
    @classmethod
    def from_keyword_list(cls, keyword_list: list[dict]) -> dict:
        """Create a keywords dict from a list of keyword dicts."""
        keywords = dict()
        for keyword in keyword_list:
            keywords[keyword["name"]] = Keyword.model_validate(keyword)
        return {"keywords": keywords}


def _get_keywords(state, keywords: Keywords) -> Keywords:
    """Get the keywords from the state."""
    if isinstance(state, Keywords):
        keywords += state
    elif isinstance(state, BaseModel):
        # iterate field values directly to preserve pydantic types
        for _, value in state:
            keywords = _get_keywords(value, keywords)
    elif isinstance(state, (list, tuple)):
        for item in state:
            keywords = _get_keywords(item, keywords)
    elif isinstance(state, dict):
        for item in state.values():
            keywords = _get_keywords(item, keywords)
    return keywords


def collect_keywords(state: BaseModel) -> Keywords:
    """Collect all keywords from the state."""
    keywords = Keywords.model_construct(keywords=dict())
    return _get_keywords(state, keywords)
