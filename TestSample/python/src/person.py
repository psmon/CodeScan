from abc import ABC, abstractmethod


class Person(ABC):
    def __init__(self, name: str, language: str) -> None:
        self.name = name
        self.language = language

    @abstractmethod
    def speak(self) -> str:
        ...

    def hello(self) -> str:
        return f"[{self.language}] {self.name}: {self.speak()}"
