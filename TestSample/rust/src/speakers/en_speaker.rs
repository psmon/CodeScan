use crate::person::{Person, PersonBase};

pub struct EnSpeaker {
    base: PersonBase,
}

impl EnSpeaker {
    pub fn new(name: &str) -> Self {
        Self { base: PersonBase::new(name, "en") }
    }
}

impl Person for EnSpeaker {
    fn name(&self) -> &str { &self.base.name }
    fn language(&self) -> &str { &self.base.language }
    fn speak(&self) -> String { String::from("Hello, World!") }
}
