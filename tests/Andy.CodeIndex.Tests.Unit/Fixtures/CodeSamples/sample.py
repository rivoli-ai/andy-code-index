"""Sample module for golden-file analysis tests."""
import asyncio


class Repository:
    """Stores widgets."""

    def __init__(self, name):
        self.name = name

    async def fetch(self, widget_id, timeout=30) -> dict:
        await asyncio.sleep(0)
        return {}

    @property
    def label(self) -> str:
        return self.name

    @staticmethod
    def default() -> "Repository":
        return Repository("default")

    def _internal(self):
        pass


def build_repository(name: str) -> Repository:
    return Repository(name)


async def load_all(source):
    return []


def _private_helper():
    pass
