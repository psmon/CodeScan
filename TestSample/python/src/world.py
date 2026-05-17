from src.person import Person


class World:
    def __init__(self) -> None:
        self._people: list[Person] = []

    def add(self, person: Person) -> None:
        self._people.append(person)

    def hello_all(self) -> list[str]:
        return [p.hello() for p in self._people]
