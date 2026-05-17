use crate::person::{Person, PersonBase};

pub struct KoSpeaker {
    base: PersonBase,
}

impl KoSpeaker {
    pub fn new(name: &str) -> Self {
        Self { base: PersonBase::new(name, "ko") }
    }
}

impl Person for KoSpeaker {
    fn name(&self) -> &str { &self.base.name }
    fn language(&self) -> &str { &self.base.language }
    fn speak(&self) -> String { String::from("안녕, 세상!") }
}
