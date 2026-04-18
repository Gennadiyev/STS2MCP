import httpx


async def _get(url: str, timeout: int, trust_env: bool, params: dict | None = None) -> str:
    """Make a GET request to the given URL with the given parameters."""
    async with httpx.AsyncClient(timeout=timeout, trust_env=trust_env) as client:
        response = await client.get(url, params=params)
        response.raise_for_status()
        return response.text


async def _post(url: str, timeout: int, trust_env: bool, body: dict | None = None) -> str:
    """Make a POST request to the given URL with the given body."""
    async with httpx.AsyncClient(timeout=timeout, trust_env=trust_env) as client:
        response = await client.post(url, json=body)
        response.raise_for_status()
        return response.text


def _handle_error(e: Exception) -> str:
    if isinstance(e, httpx.ConnectError):
        return "Error: Cannot connect to STS2_MCP mod. Is the game running with the mod enabled?"
    if isinstance(e, httpx.HTTPStatusError):
        return f"Error: HTTP {e.response.status_code} — {e.response.text}"
    return f"Error: {e}"
