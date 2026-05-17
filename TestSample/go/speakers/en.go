package speakers

import "helloworld/person"

type EnSpeaker struct {
	person.Base
}

func NewEnSpeaker(name string) *EnSpeaker {
	return &EnSpeaker{Base: person.NewBase(name, "en")}
}

func (s *EnSpeaker) Speak() string { return "Hello, World!" }
func (s *EnSpeaker) Hello() string { return person.Format(s) }
