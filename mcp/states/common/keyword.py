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
        return Keywords(keywords=combined_keywords)
    
    def to_markdown(self) -> str:
        """Convert the keywords to a markdown string."""
        if not self.keywords:
            return ""
        lines = ["## Keyword Glossary"]
        for keyword in self.keywords.values():
            lines.append(f"- {keyword.to_markdown()}")
        return "\n".join(lines)

    @model_validator(mode="before")
    @classmethod
    def from_keyword_list(cls, keyword_list: list[dict]) -> dict:
        """Create a keywords dict from a list of keyword dicts."""
        keywords = dict()
        for keyword in keyword_list:
            keywords[keyword["name"]] = keyword
        return {"keywords": keywords}
