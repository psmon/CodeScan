package main

import (
	"fmt"

	"helloworld/speakers"
	"helloworld/world"
)

func main() {
	w := world.New()
	w.Add(speakers.NewEnSpeaker("Alice"))
	w.Add(speakers.NewKoSpeaker("진수"))
	w.Add(speakers.NewJaSpeaker("ハナコ"))

	for _, line := range w.HelloAll() {
		fmt.Println(line)
	}
}
