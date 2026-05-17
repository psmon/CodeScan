package speakers

import "helloworld/person"

type JaSpeaker struct {
	person.Base
}

func NewJaSpeaker(name string) *JaSpeaker {
	return &JaSpeaker{Base: person.NewBase(name, "ja")}
}

func (s *JaSpeaker) Speak() string { return "こんにちは、世界！" }
func (s *JaSpeaker) Hello() string { return person.Format(s) }
