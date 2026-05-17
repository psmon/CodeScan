from src.person import Person


class JaSpeaker(Person):
    def __init__(self, name: str) -> None:
        super().__init__(name, "ja")

    def speak(self) -> str:
        return "こんにちは、世界！"
