from src.person import Person


class EnSpeaker(Person):
    def __init__(self, name: str) -> None:
        super().__init__(name, "en")

    def speak(self) -> str:
        return "Hello, World!"
