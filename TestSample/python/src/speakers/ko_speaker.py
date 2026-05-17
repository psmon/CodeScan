from src.person import Person


class KoSpeaker(Person):
    def __init__(self, name: str) -> None:
        super().__init__(name, "ko")

    def speak(self) -> str:
        return "안녕, 세상!"
