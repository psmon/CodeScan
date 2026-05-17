package person

import "fmt"

type Person interface {
	Speak() string
	Hello() string
	Name() string
	Language() string
}

type Base struct {
	name     string
	language string
}

func NewBase(name, language string) Base {
	return Base{name: name, language: language}
}

func (b Base) Name() string     { return b.name }
func (b Base) Language() string { return b.language }

func Format(p Person) string {
	return fmt.Sprintf("[%s] %s: %s", p.Language(), p.Name(), p.Speak())
}
