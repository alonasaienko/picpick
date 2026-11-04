"""Робить src/worker імпортованим у тестах: Worker запускається з цієї теки."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src" / "worker"))
