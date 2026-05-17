package world

import "helloworld/person"

type World struct {
	people []person.Person
}

func New() *World {
	return &World{people: make([]person.Person, 0)}
}

func (w *World) Add(p person.Person) {
	w.people = append(w.people, p)
}

func (w *World) HelloAll() []string {
	out := make([]string, 0, len(w.people))
	for _, p := range w.people {
		out = append(out, p.Hello())
	}
	return out
}
