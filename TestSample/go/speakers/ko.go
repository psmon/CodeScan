package speakers

import "helloworld/person"

type KoSpeaker struct {
	person.Base
}

func NewKoSpeaker(name string) *KoSpeaker {
	return &KoSpeaker{Base: person.NewBase(name, "ko")}
}

func (s *KoSpeaker) Speak() string { return "안녕, 세상!" }
func (s *KoSpeaker) Hello() string { return person.Format(s) }
