pub trait Person {
    fn name(&self) -> &str;
    fn language(&self) -> &str;
    fn speak(&self) -> String;

    fn hello(&self) -> String {
        format!("[{}] {}: {}", self.language(), self.name(), self.speak())
    }
}

pub struct PersonBase {
    pub name: String,
    pub language: String,
}

impl PersonBase {
    pub fn new(name: &str, language: &str) -> Self {
        Self {
            name: name.to_string(),
            language: language.to_string(),
        }
    }
}
