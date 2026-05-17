use crate::person::{Person, PersonBase};

pub struct JaSpeaker {
    base: PersonBase,
}

impl JaSpeaker {
    pub fn new(name: &str) -> Self {
        Self { base: PersonBase::new(name, "ja") }
    }
}

impl Person for JaSpeaker {
    fn name(&self) -> &str { &self.base.name }
    fn language(&self) -> &str { &self.base.language }
    fn speak(&self) -> String { String::from("こんにちは、世界！") }
}
